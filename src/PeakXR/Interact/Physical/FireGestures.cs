using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Interact.Lights;
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
/// (RocketFuse.cs:101), ghost fire (GhostFire.cs:149) or unlit campfire (Campfire.cs:322) holds interact on it.</item>
/// <item>Warm hands (G10): open palms toward a lit campfire give a slow warm pulse; cold (status Cold) a faint
/// shiver. Feel only: campfire warmth stays the game's passive range (Campfire.cs:225).</item>
/// </list>
/// </summary>
internal static class FireGestures
{
    /// <summary>Heat column over the fire base (audit G3; tune against the prefab in game).</summary>
    private const float CookRadius = 0.6f;
    private const float CookMinUp = 0.2f;
    private const float CookMaxUp = 1f;

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
    private static readonly Collider[] Overlap = new Collider[16];
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

        var interaction = Interaction.instance;
        float progress =
            interaction != null && ReferenceEquals(interaction.currentHeldInteractible, fire)
                ? Mathf.Clamp01(interaction.constantInteractableProgress)
                : 0f;
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
        if (target == null || !Touch.Update(distance, dt))
            return;
        GestureRequests.Interact(target);
        // Crackle while the fuse takes: short uneven ticks like HandLights' strike.
        if (now >= nextCrackle)
        {
            nextCrackle = now + 0.05f + 0.1f * Random.value;
            Haptics.Pulse(ItemGestures.HoldLeft, 0.15f + 0.25f * Random.value, 0.03f);
        }
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
            var it = col.GetComponentInParent<IInteractible>();
            if (it is not (ScoutCannonFuse or RocketFuse or GhostFire))
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

        // Campfire logs: by distance to the fire base, since the campfire is found through its own list.
        var fire = ItemGestures.NearestCampfire(flame, 1.5f, litOnly: false);
        if (fire != null && fire.state == Campfire.FireState.Off && Lightable(c, fire))
        {
            float d = HeatZone.Distance(
                flame.N(),
                ItemGestures.FireBase(fire).N(),
                0.35f,
                -0.1f,
                0.4f
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
        if (
            !warming
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
