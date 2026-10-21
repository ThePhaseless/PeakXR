using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Interact.Lights;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Interact.Physical;

/// <summary>
/// Fire gestures of the local VR player, all ending in the game's own hold-interact so the cook, light and fuse
/// RPCs are vanilla:
/// <list type="bullet">
/// <item>Cook (G3): a cookable held item in the heat column over a lit campfire holds interact on that campfire.
/// <c>Campfire.Interact</c> starts the cooking visuals (Campfire.cs:300-306) and the hold finishes after
/// <c>cookTime</c> (Campfire.cs:40, :309-320); pulling the item out releases interact, which is the vanilla
/// <c>CancelCast</c> (Campfire.cs:328, Interaction.cs:109-112).</item>
/// <item>Light (G6): a lit held flame touched to a scout cannon fuse (ScoutCannonFuse.cs:99), rocket fuse
/// (RocketFuse.cs:101), ghost fire (GhostFire.cs:149) or unlit campfire (Campfire.cs:322; its own solid colliders, or the
/// zone over its fire base) holds interact on it.</item>
/// <item>Warm hands (G10): open palms toward a lit campfire give a slow warm pulse; cold (status Cold) a faint
/// shiver while near one. Feel only: campfire warmth stays the game's passive range (Campfire.cs:225).</item>
/// </list>
/// </summary>
internal static class FireGestures
{
    /// <summary>Heat column over the fire base (audit G3; tune against the prefab in game).</summary>
    private const float CookRadius = 0.6f;
    private const float CookMinUp = 0.2f;
    private const float CookMaxUp = 1f;

    /// <summary>Light zone over an unlit campfire's base: its log pile plus a reach margin. A torch held at full
    /// reach from a body stopped by the fire ring got its flame to 0.47-0.49 m out, 0.24 m up (vr11); the old
    /// 0.35 m zone sat at the edge of what an arm can reach.</summary>
    private const float CampfireLightRadius = 0.6f;
    private const float CampfireLightMinUp = -0.1f;
    private const float CampfireLightMaxUp = 0.6f;

    /// <summary>A flame this far outside the light zone gets the "closer" cue showing where the zone starts.</summary>
    private const float LightHintRange = 0.4f;

    private const float TouchEnter = 0.12f;
    private const float TouchExit = 0.2f;
    private const float WarmRange = 1f;

    private static readonly ContactDwell Cook = new()
    {
        Enter = 0f,
        Exit = 0.1f,
        Dwell = 0.2f,
    };
    private static readonly ContactDwell Touch = new()
    {
        Enter = TouchEnter,
        Exit = TouchExit,
        Dwell = 0.1f,
    };

    // Room for clutter (rocks, props, the holder's own colliders) around a fuse.
    private static readonly Collider[] Overlap = new Collider[64];

    /// <summary>Collider instance id to its fuse or ghost fire, or null for any other collider, so a flame held
    /// in clutter does not run GetComponentInParent on every hit every frame.</summary>
    private static readonly Dictionary<int, IInteractible> LightableByCollider = new(256);
    private static readonly float[] NextWarm = new float[2];

    private static Campfire cookFire;
    private static IInteractible touchTarget;
    private static float nextCookPulse;
    private static float nextCrackle;
    private static float nextShiver;

    /// <summary>Warm pulses sent so far; the self-test reads it (vr.27).</summary>
    internal static int WarmPulses;

    private static Item flameItem;
    private static HeldLightKind flameKind;
    private static Transform flamePoint;

    public static void Reset()
    {
        Cook.Reset();
        Touch.Reset();
        cookFire = null;
        touchTarget = null;
    }

    public static void Tick(Character c, VRRig rig, ItemGesturesConfig cfg, float dt)
    {
        float now = Time.time;
        UpdateCook(c, cfg, dt, now);
        UpdateLight(c, cfg, dt, now);
        UpdateWarmth(c, rig, cfg, now);
    }

    private static void UpdateCook(Character c, ItemGesturesConfig cfg, float dt, float now)
    {
        var item = ItemGestures.Held;
        if (!cfg.CookOverFire || item == null || item.cooking == null || !item.cooking.canBeCooked)
        {
            Cook.Reset();
            cookFire = null;
            return;
        }
        var point = item.Center();
        var fire = ItemGestures.NearestCampfire(point, 2f, litOnly: true);
        if (!ReferenceEquals(fire, cookFire))
        {
            Cook.Reset();
            cookFire = fire;
        }
        if (fire == null)
            return;
        float distance = HeatZone.Distance(
            point.N(),
            ItemGestures.FireBase(fire).N(),
            CookRadius,
            CookMinUp,
            CookMaxUp
        );
        // Vanilla: a lit campfire takes interact only while the held item can be cooked (Campfire.cs:341-356).
        if (!Cook.Update(distance, dt) || !fire.IsInteractible(c))
            return;
        GestureRequests.Interact(fire);

        // Only once the game holds this campfire: the warmth is the cook progress, not just the zone.
        var interaction = Interaction.instance;
        if (interaction == null || !ReferenceEquals(interaction.currentHeldInteractible, fire))
            return;
        float progress = Mathf.Clamp01(interaction.constantInteractableProgress);
        if (now >= nextCookPulse)
        {
            nextCookPulse = now + 0.12f;
            Haptics.Pulse(ItemGestures.HoldLeft, 0.08f + 0.35f * progress, 0.08f);
        }
    }

    private static void UpdateLight(Character c, ItemGesturesConfig cfg, float dt, float now)
    {
        var item = ItemGestures.Held;
        if (ItemGestures.ItemChanged)
            BindFlame(item);
        if (!cfg.LightWithFlame || item == null || !FlameLit(item))
        {
            Touch.Reset();
            touchTarget = null;
            return;
        }
        var flame = flamePoint != null ? flamePoint.position : item.Center();
        var target = FindLightable(c, flame, out float distance);
        if (!ReferenceEquals(target, touchTarget))
        {
            Touch.Reset();
            touchTarget = target;
            if (target != null)
                Haptics.Pulse(ItemGestures.HoldLeft, 0.4f, 0.04f);
        }
        if (target is Campfire near && distance > TouchEnter && distance <= LightHintRange)
            Affordance.Hover(
                ItemGestures.HoldLeft,
                flame,
                DenyReason.OutOfReach,
                DenyHint.Closer(
                    HeatZone
                        .Nearest(
                            flame.N(),
                            ItemGestures.FireBase(near).N(),
                            CampfireLightRadius,
                            CampfireLightMinUp,
                            CampfireLightMaxUp
                        )
                        .U(),
                    distance
                )
            );
        if (target == null || !Touch.Update(distance, dt))
            return;
        if (target is Campfire campfire && campfire.state == Campfire.FireState.Off)
            WarnFriendsFar(campfire);
        GestureRequests.Interact(target);
        // Crackle while the fuse takes (the game holds it): short uneven ticks like HandLights' strike.
        var interaction = Interaction.instance;
        bool held =
            interaction != null && ReferenceEquals(interaction.currentHeldInteractible, target);
        if (held && now >= nextCrackle)
        {
            nextCrackle = now + 0.05f + 0.1f * Random.value;
            Haptics.Pulse(ItemGestures.HoldLeft, 0.15f + 0.25f * Random.value, 0.03f);
        }
    }

    /// <summary>
    /// An unlit campfire lights only with every living scout within <c>moraleBoostRadius</c> of it
    /// (Campfire.cs:322, EveryoneInRange :375-386, distances from the fire to <c>Center</c> :364); otherwise the
    /// whole hold ends in nothing. The farthest missing friend gets the teal ring, joined to the fire, if in sight.
    /// </summary>
    private static void WarnFriendsFar(Campfire fire)
    {
        var at = fire.transform.position;
        float radius = fire.moraleBoostRadius;
        Character far = null;
        float farthest = radius;
        foreach (var ch in PlayerHandler.GetAllPlayerCharacters())
        {
            if (ch == null || ch.data.dead)
                continue;
            float d = Vector3.Distance(at, ch.Center);
            if (d > farthest)
            {
                farthest = d;
                far = ch;
            }
        }
        if (far == null)
            return;
        // The friend's ring only when you can see them: a ring through a cliff would read as the wrong place.
        var cam = MainCamera.instance;
        bool seen =
            cam != null
            && !Physics.Linecast(
                cam.transform.position,
                far.Center,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            );
        Affordance.Deny(
            ItemGestures.HoldLeft,
            ItemGestures.FireBase(fire),
            DenyReason.FriendsFar,
            seen ? DenyHint.At(far.Center) : default
        );
    }

    private static void BindFlame(Item item)
    {
        flameItem = item;
        flameKind = HeldLightKind.None;
        flamePoint = null;
        if (item == null)
            return;
        if (item.TryGetComponent(out Action_Torch torch))
        {
            flameKind = HeldLightKind.Torch;
            flamePoint = torch.torchLight != null ? torch.torchLight.transform : null;
        }
        else if (item.TryGetComponent(out Candle candle))
        {
            flameKind = HeldLightKind.Candle;
            flamePoint = candle.fireParticle != null ? candle.fireParticle.transform : null;
        }
        else if (item.TryGetComponent(out Flare _))
        {
            flameKind = HeldLightKind.Flare;
        }
        else if (item.TryGetComponent(out Lantern _))
        {
            flameKind = HeldLightKind.Lantern;
        }
    }

    /// <summary>The torch burns while held (Action_Torch.cs:3); the rest keep FlareActive (HandLights.IsLit).</summary>
    private static bool FlameLit(Item item) =>
        ReferenceEquals(item, flameItem)
        && flameKind switch
        {
            HeldLightKind.None => false,
            HeldLightKind.Torch => true,
            _ => HandLights.IsLit(item),
        };

    /// <summary>The closest fuse, ghost fire or unlit campfire the flame touches that still takes a light.</summary>
    private static IInteractible FindLightable(Character c, Vector3 flame, out float distance)
    {
        IInteractible best = null;
        distance = float.MaxValue;
        int hits = Physics.OverlapSphereNonAlloc(
            flame,
            TouchExit,
            Overlap,
            HelperFunctions.AllPhysical,
            QueryTriggerInteraction.Collide
        );
        for (int i = 0; i < hits; i++)
        {
            var col = Overlap[i];
            Overlap[i] = null;
            if (col == null)
                continue;
            int id = col.GetInstanceID();
            if (!LightableByCollider.TryGetValue(id, out var it))
            {
                // Destroyed colliders leave stale ids behind; a cap keeps the map small.
                if (LightableByCollider.Count >= 1024)
                    LightableByCollider.Clear();
                it = col.GetComponentInParent<IInteractible>();
                // A campfire's own solid colliders (logs) count as touched: a torch pushed into the logs stops on
                // them before the flame reaches the zone over the fire base (vr10: 0.56 m out, zone 0.47 m).
                if (
                    it is not (ScoutCannonFuse or RocketFuse or GhostFire)
                    && (it is not Campfire || col.isTrigger)
                )
                    it = null;
                LightableByCollider[id] = it;
            }
            if (it == null || (it is Object unityObject && unityObject == null))
                continue;
            if (it is Campfire touched && touched.state != Campfire.FireState.Off)
                continue;
            if (!Lightable(c, it))
                continue;
            float d = (col.bounds.ClosestPoint(flame) - flame).magnitude;
            if (d < distance)
            {
                distance = d;
                best = it;
            }
        }

        // Campfire logs: by distance to the light zone over the fire base, since the campfire is found through its
        // own list.
        var fire = ItemGestures.NearestCampfire(flame, 1.5f, litOnly: false);
        if (fire != null && fire.state == Campfire.FireState.Off && Lightable(c, fire))
        {
            float d = HeatZone.Distance(
                flame.N(),
                ItemGestures.FireBase(fire).N(),
                CampfireLightRadius,
                CampfireLightMinUp,
                CampfireLightMaxUp
            );
            if (d < distance)
            {
                distance = d;
                best = fire;
            }
        }
        return best;
    }

    private static bool Lightable(Character c, IInteractible it) =>
        it.IsInteractible(c)
        && it is IInteractibleConstant constant
        && constant.IsConstantlyInteractable(c);

    private static void UpdateWarmth(Character c, VRRig rig, ItemGesturesConfig cfg, float now)
    {
        if (!cfg.WarmHands)
            return;
        var fire = ItemGestures.NearestCampfire(c.Center, 4f, litOnly: true);
        var heat = fire != null ? (ItemGestures.FireBase(fire) + Vector3.up * 0.3f).N() : default;
        bool warming = false;
        for (int h = 0; h < 2; h++)
        {
            bool left = h == 0;
            bool free =
                ItemGestures.Held == null || (!LocalHold.TwoHand && ItemGestures.HoldLeft != left);
            ref var state = ref VRInput.Hand(left);
            var t = rig.Hand(left);
            if (fire == null || !free || !state.Tracked || state.Grip > 0.4f || t == null)
                continue;
            // Palm normal: +X of the left grip pose, -X of the right (HandRig.cs:62).
            var palm = left ? t.right : -t.right;
            float warm = PalmWarmth.Amount(palm.N(), t.position.N(), heat, WarmRange);
            if (warm <= 0f)
                continue;
            warming = true;
            if (now >= NextWarm[h])
            {
                NextWarm[h] = now + 0.5f;
                WarmPulses++;
                Haptics.Pulse(left, 0.05f + 0.2f * warm, 0.3f);
            }
        }
        // Shiver only by a lit fire, as a cue to warm up there; no endless pulses across a cold biome.
        if (
            fire != null
            && !warming
            && now >= nextShiver
            && c.refs.afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Cold) > 0.05f
        )
        {
            nextShiver = now + 1.6f;
            Haptics.Pulse(true, 0.08f, 0.04f);
            Haptics.Pulse(false, 0.08f, 0.04f);
        }
    }
}
