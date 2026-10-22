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

    /// <summary>Food within this of the heat column feels the warmth ramp (WS-W12), up to this fraction of the
    /// progress ramp at the column's edge, so the cook ramp that follows still feels like a new, stronger stage.</summary>
    private const float HeatSearch = 0.5f;
    private const float HeatSearchMax = 0.25f;

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

    /// <summary>Dwell before a refused cook (item in the column of a fire that will not cook it) is reported.</summary>
    private static readonly ContactDwell CookRefused = new()
    {
        Enter = 0f,
        Exit = 0.1f,
        Dwell = 0.2f,
    };

    private static Campfire cookFire;
    private static IInteractible touchTarget;
    private static IInteractible refusedTouch;
    private static Campfire farFire;
    private static float nextShiver;

    /// <summary>Warm pulses sent so far; the self-test reads it (vr.27).</summary>
    internal static int WarmPulses;

    private static Item flameItem;
    private static HeldLightKind flameKind;
    private static Transform flamePoint;

    public static void Reset()
    {
        Cook.Reset();
        CookRefused.Reset();
        Touch.Reset();
        cookFire = null;
        touchTarget = null;
        refusedTouch = null;
        farFire = null;
    }

    public static void Tick(Character c, VRRig rig, ItemGesturesConfig cfg, float dt)
    {
        float now = Time.time;
        UpdateCook(c, cfg, dt);
        UpdateLight(c, cfg, dt);
        UpdateFriendsFar();
        UpdateWarmth(c, rig, cfg, now);
    }

    /// <summary>
    /// Any hold on an unlit campfire, by flame or bare grip (WS-W11): with a living scout outside
    /// <c>moraleBoostRadius</c> the hold ends in nothing (Campfire.cs:315-326), so it is refused as it starts.
    /// </summary>
    private static void UpdateFriendsFar()
    {
        var interaction = Interaction.instance;
        var fire = interaction != null ? interaction.currentHeldInteractible as Campfire : null;
        if (fire == null || fire.state != Campfire.FireState.Off)
        {
            farFire = null;
            return;
        }
        if (ReferenceEquals(fire, farFire))
            return;
        farFire = fire;
        if (!fire.EveryoneInRange())
            WarnFriendsFar(fire, WorldHolds.HandOn(fire, ItemGestures.HoldLeft));
    }

    private static void UpdateCook(Character c, ItemGesturesConfig cfg, float dt)
    {
        var item = ItemGestures.Held;
        if (!cfg.CookOverFire || item == null || item.cooking == null)
        {
            Cook.Reset();
            CookRefused.Reset();
            cookFire = null;
            return;
        }
        bool cookable = item.cooking.canBeCooked;
        var point = item.Center();
        var fire = cookable ? ItemGestures.NearestCampfire(point, 2f, litOnly: true) : null;
        if (fire == null)
        {
            Cook.Reset();
            cookFire = null;
            RefuseCook(item, point, cookable, dt);
            return;
        }
        CookRefused.Reset();
        if (!ReferenceEquals(fire, cookFire))
        {
            Cook.Reset();
            cookFire = fire;
        }
        float distance = HeatZone.Distance(
            point.N(),
            ItemGestures.FireBase(fire).N(),
            CookRadius,
            CookMinUp,
            CookMaxUp
        );
        // Vanilla: a lit campfire takes interact only while the held item can be cooked (Campfire.cs:341-356).
        if (!Cook.Update(distance, dt) || !fire.IsInteractible(c))
        {
            // Outside the column: a faint ramp that firms up toward it, to find the heat by feel.
            float warm = HeatGradient.Closeness(distance, HeatSearch);
            if (warm > 0f)
                Affordance.Progress(ItemGestures.HoldLeft, HeatSearchMax * warm);
            return;
        }
        GestureRequests.Interact(fire);

        // Only once the game holds this campfire: the warmth is the cook progress, not just the zone.
        var interaction = Interaction.instance;
        if (interaction == null || !ReferenceEquals(interaction.currentHeldInteractible, fire))
            return;
        Affordance.Progress(
            ItemGestures.HoldLeft,
            Mathf.Clamp01(interaction.constantInteractableProgress)
        );
    }

    /// <summary>
    /// Something held in the heat column that will not cook there (WS-W12): food over an unlit fire (NotLit) or a
    /// spent one (UsedUp), the fire red; anything else over a lit fire (CantUse, Campfire.cs:341-356), the item red.
    /// A held light is skipped: over an unlit fire it is the light gesture. Refused once the item has dwelt there,
    /// then the red ring stays while it does.
    /// </summary>
    private static void RefuseCook(Item item, Vector3 point, bool cookable, float dt)
    {
        var fire = ItemGestures.NearestCampfire(point, 2f, litOnly: false);
        bool light = ReferenceEquals(item, flameItem) && flameKind != HeldLightKind.None;
        if (fire == null || light)
        {
            CookRefused.Reset();
            return;
        }
        var reason = CookCheck.Reason(cookable, fire.Lit, fire.state == Campfire.FireState.Spent);
        float distance = HeatZone.Distance(
            point.N(),
            ItemGestures.FireBase(fire).N(),
            CookRadius,
            CookMinUp,
            CookMaxUp
        );
        bool was = CookRefused.Active;
        if (reason == DenyReason.None || !CookRefused.Update(distance, dt))
            return;
        bool left = ItemGestures.HoldLeft;
        var at = ItemGestures.FireBase(fire);
        if (!was)
        {
            Component tint = reason == DenyReason.CantUse ? item : fire;
            Affordance.Deny(left, at, reason, default, tint);
        }
        Affordance.Hover(left, at, reason);
    }

    private static void UpdateLight(Character c, ItemGesturesConfig cfg, float dt)
    {
        var item = ItemGestures.Held;
        if (ItemGestures.ItemChanged)
            BindFlame(item);
        if (!cfg.LightWithFlame || item == null || !FlameLit(item))
        {
            if (cfg.LightWithFlame && item != null && flameKind != HeldLightKind.None)
                WarnUnlit(c, item);
            Touch.Reset();
            touchTarget = null;
            refusedTouch = null;
            return;
        }
        var flame = flamePoint != null ? flamePoint.position : item.Center();
        var target = FindLightable(c, flame, out float distance, out var refused);
        RefuseTouch(c, flame, refused);
        if (!ReferenceEquals(target, touchTarget))
        {
            Touch.Reset();
            touchTarget = target;
            if (target != null)
                Affordance.Contact(ItemGestures.HoldLeft);
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
        bool touching = target != null && Touch.Update(distance, dt);
        TraceLight(item, flame, target, distance, touching, lit: true);
        if (!touching)
            return;
        GestureRequests.Interact(target);
        // The progress ramp while the fuse or fire takes (the game holds it).
        var interaction = Interaction.instance;
        if (interaction != null && ReferenceEquals(interaction.currentHeldInteractible, target))
            Affordance.Progress(
                ItemGestures.HoldLeft,
                Mathf.Clamp01(interaction.constantInteractableProgress)
            );
    }

    /// <summary>
    /// A flame touched to a fuse or ghost fire that no longer takes one (WS-W17): lit or burning (UsedUp), or a
    /// rocket pack someone holds (Occupied, RocketFuse.cs:134-139). Refused once per touch, the fuse red.
    /// </summary>
    private static void RefuseTouch(Character c, Vector3 flame, IInteractible refused)
    {
        if (ReferenceEquals(refused, refusedTouch))
            return;
        refusedTouch = refused;
        if (refused is not Component comp)
            return;
        var reason =
            refused is GhostFire || refused.IsInteractible(c)
                ? DenyReason.UsedUp
                : DenyReason.Occupied;
        Affordance.Deny(ItemGestures.HoldLeft, flame, reason, default, comp);
    }

    /// <summary>
    /// An unlit torch, lantern, candle or flare held to something it would light: say so where the flame is, and show
    /// what would work, the use trigger of the holding hand (a ring on that controller). Its own use lights it
    /// (Action_LightLantern, Lantern.cs:193); the touch alone never does.
    /// </summary>
    private static void WarnUnlit(Character c, Item item)
    {
        var flame = flamePoint != null ? flamePoint.position : item.Center();
        var target = FindLightable(c, flame, out float distance);
        TraceLight(item, flame, target, distance, false, lit: false);
        if (target == null || distance > LightHintRange)
            return;
        var rig = VRRig.Instance;
        var hand = rig != null ? rig.Hand(ItemGestures.HoldLeft) : null;
        Affordance.Hover(ItemGestures.HoldLeft, flame, DenyReason.NotLit, DenyHint.On(hand));
    }

    private static Item lightTraceItem;
    private static int lightTrace = -1;
    private static int lightTraceLines;

    /// <summary>Self-test diagnosis of the light gesture (vr.26): one Debug line whenever a gate on the way from a
    /// held flame to the game's light hold flips, at most 80 per held item. Names the nearest listed campfire
    /// (<c>Campfire.ALL_CAMPFIRES</c>, the list the gesture searches) so a fire missing from it shows.</summary>
    private static void TraceLight(
        Item item,
        Vector3 flame,
        IInteractible target,
        float distance,
        bool touching,
        bool lit
    )
    {
        if (!ReferenceEquals(item, lightTraceItem))
        {
            lightTraceItem = item;
            lightTrace = -1;
            lightTraceLines = 0;
        }
        var interaction = Interaction.instance;
        var hovered = interaction != null ? interaction.currentHovered : null;
        var held = interaction != null ? interaction.currentHeldInteractible : null;
        var near = ItemGestures.NearestCampfire(flame, 2f, litOnly: false);
        int mask =
            (lit ? 1 : 0)
            | (target != null ? 2 : 0)
            | (target != null && distance <= TouchEnter ? 4 : 0)
            | (touching ? 8 : 0)
            | (hovered != null && ReferenceEquals(hovered, target) ? 16 : 0)
            | (held != null && ReferenceEquals(held, target) ? 32 : 0)
            | (near != null ? 64 : 0)
            | (near != null && ReferenceEquals(near, target) ? 128 : 0);
        if (mask == lightTrace || lightTraceLines >= 80)
            return;
        lightTrace = mask;
        lightTraceLines++;
        var all = ItemGestures.Campfires;
        string nearText =
            near != null
                ? $"{near.name} ({near.state}, {(ItemGestures.FireBase(near) - flame).magnitude:0.00} m)"
                : "none";
        Plugin.Log.LogDebug(
            $"[interact.light] f{Time.frameCount} {item.name}: lit {lit}, flame kind {flameKind}, target "
                + $"{LightName(target)} at {distance:0.00} m (enter {TouchEnter:0.00}), touching {touching} "
                + $"(interact requested {touching}); nearest listed campfire within 2 m {nearText} of "
                + $"{(all != null ? all.Count : -1)} listed; hovered {LightName(hovered)}, hold on {LightName(held)}"
        );
    }

    private static string LightName(IInteractible it) =>
        it is Component comp && comp != null ? comp.name
        : it == null ? "nothing"
        : it.GetType().Name;

    /// <summary>
    /// An unlit campfire lights only with every living scout within <c>moraleBoostRadius</c> of it
    /// (Campfire.cs:322, EveryoneInRange :375-386, distances from the fire to <c>Center</c> :364); otherwise the
    /// whole hold ends in nothing. The logs turn red. The farthest missing friend gets the teal ring, joined to the
    /// fire, if in sight; out of sight, the ring sits a hand's length from the hand in their direction.
    /// </summary>
    private static void WarnFriendsFar(Campfire fire, bool left)
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
        var rig = VRRig.Instance;
        var hand = rig != null ? rig.Hand(left) : null;
        var hint =
            seen ? DenyHint.At(far.Center)
            : hand != null
                ? DenyHint.At(FriendBearing.Spot(hand.position.N(), far.Center.N(), 0.25f).U())
            : default;
        Affordance.Deny(left, ItemGestures.FireBase(fire), DenyReason.FriendsFar, hint, fire);
    }

    private static void BindFlame(Item item)
    {
        flameItem = item;
        flameKind = Kind(item);
        flamePoint = FlamePoint(item);
    }

    /// <summary>Which held light <paramref name="item"/> is, in HandLights' order (HandLights.Bind). The torch item
    /// carries a <c>Lantern</c> on its root (its <c>Action_Torch</c> is not there; vr13 traced "flame kind
    /// Lantern"), so it is lit only after its use, like a lantern (FlareActive, Lantern.cs:193-208).</summary>
    private static HeldLightKind Kind(Item item)
    {
        if (item == null)
            return HeldLightKind.None;
        if (item.TryGetComponent(out Flare _))
            return HeldLightKind.Flare;
        if (item.TryGetComponent(out Candle _))
            return HeldLightKind.Candle;
        if (item.TryGetComponent(out Lantern _))
            return HeldLightKind.Lantern;
        if (item.TryGetComponent(out Action_Torch _))
            return HeldLightKind.Torch;
        return HeldLightKind.None;
    }

    /// <summary>Where a held light's flame is: the candle's or lantern's fire particles (Candle.cs:25, Lantern.cs:31),
    /// a torch light, else null (use the item's centre). The self-test carries the same point (vr.26).</summary>
    internal static Transform FlamePoint(Item item)
    {
        if (item == null)
            return null;
        if (item.TryGetComponent(out Candle candle) && candle.fireParticle != null)
            return candle.fireParticle.transform;
        if (item.TryGetComponent(out Lantern lantern) && lantern.fireParticle != null)
            return lantern.fireParticle.transform;
        var torch = item.GetComponentInChildren<Action_Torch>();
        return torch != null && torch.torchLight != null ? torch.torchLight.transform : null;
    }

    /// <summary>A bare torch light (Action_Torch.cs:3, no on state) burns while held; the rest keep FlareActive
    /// (HandLights.IsLit).</summary>
    private static bool FlameLit(Item item) =>
        ReferenceEquals(item, flameItem)
        && flameKind switch
        {
            HeldLightKind.None => false,
            HeldLightKind.Torch => true,
            _ => HandLights.IsLit(item),
        };

    /// <summary>The closest fuse, ghost fire or unlit campfire the flame touches that still takes a light.</summary>
    private static IInteractible FindLightable(Character c, Vector3 flame, out float distance) =>
        FindLightable(c, flame, out distance, out _);

    /// <summary>As above; <paramref name="refused"/> is the closest fuse or ghost fire within touch that no longer
    /// takes a light (lit, or a rocket pack someone holds), or null.</summary>
    private static IInteractible FindLightable(
        Character c,
        Vector3 flame,
        out float distance,
        out IInteractible refused
    )
    {
        refused = null;
        float refusedDistance = TouchEnter;
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
            float d = (col.bounds.ClosestPoint(flame) - flame).magnitude;
            if (!Lightable(c, it))
            {
                if (it is not Campfire && d <= refusedDistance)
                {
                    refusedDistance = d;
                    refused = it;
                }
                continue;
            }
            if (d < distance)
            {
                distance = d;
                best = it;
            }
        }

        // Campfire logs: by distance to the light zone over the fire base, since the campfire is found through its
        // own list.
        var fire = ItemGestures.NearestCampfire(flame, 1.5f, litOnly: false, unlitOnly: true);
        if (fire != null && fire.state == Campfire.FireState.Off && Lightable(c, fire))
        {
            float d = LightZoneDistance(fire, flame);
            if (d < distance)
            {
                distance = d;
                best = fire;
            }
        }
        return best;
    }

    /// <summary>How far <paramref name="flame"/> is outside <paramref name="fire"/>'s light zone (0 inside); the
    /// light starts within <c>TouchEnter</c> of it. The self-test holds a torch in it (vr.26).</summary>
    internal static float LightZoneDistance(Campfire fire, Vector3 flame) =>
        HeatZone.Distance(
            flame.N(),
            ItemGestures.FireBase(fire).N(),
            CampfireLightRadius,
            CampfireLightMinUp,
            CampfireLightMaxUp
        );

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
