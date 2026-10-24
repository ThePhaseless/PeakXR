using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Interact.Physical;

/// <summary>
/// Held-item gestures that end in the item's own primary use (<c>CharacterItems.DoUsing</c>, CharacterItems.cs:197-215):
/// <list type="bullet">
/// <item>Bugle (G5): the holding hand at the mouth holds primary, so <c>BugleSFX.UpdateTooting</c> starts the toot
/// (BugleSFX.cs:43-61). Its pitch argument comes from the bell direction via <see cref="BeforeTooting"/>.</item>
/// <item>Body press (G8): an item applied to the body (<see cref="UseGesture.Apply"/>) held against your own torso or
/// other forearm holds primary. Feeding a friend's body is in <c>SocialHands</c>.</item>
/// <item>Face: the Book of Bones (<see cref="UseGesture.Read"/>, which turns its reader into a skeleton) held open at
/// eye level, or a lethal or warping item (<see cref="UseGesture.Commune"/>) held eye to eye, holds primary for the
/// game's own cast after a deliberate dwell, with a danger warning as it starts and the progress feel after. A bare
/// trigger on them is refused (interact.physical asks <see cref="ItemGestures.TriggerRefused"/>) with a warning ring
/// at the face, where it has to go. Near a skeleton the read is off: the book there revives (interact.world), and
/// the two never overlap (<see cref="ReadZone"/>).</item>
/// </list>
/// The holding hand's tracked grip is used, not the physics item: held items collide with the holder's own body.
/// </summary>
internal static class HeldGestures
{
    private static readonly MouthGesture Mouth = new()
    {
        Radius = 0.16f,
        ExitRadius = 0.22f,
        DwellTime = 0.1f,
    };
    private static readonly ContactDwell Body = new()
    {
        Enter = 0f,
        Exit = 0.05f,
        Dwell = 0.35f,
    };

    /// <summary>Torso radius around the spine, with the hand's own thickness (tune on headset).</summary>
    private const float TorsoRadius = 0.22f;

    /// <summary>Forearm press distance, metres (audit G8: 15 cm).</summary>
    private const float ForearmRadius = 0.12f;

    private static readonly ContactDwell Face = new()
    {
        Enter = 0f,
        Exit = 0.06f,
        // Deliberate: both uses are dangerous, so a hand passing the face never starts one.
        Dwell = 0.5f,
    };

    /// <summary>Eye to eye: the holding hand within this of a point just in front of the eyes, metres.</summary>
    private const float CommuneAhead = 0.12f;
    private const float CommuneRadius = 0.14f;

    private static readonly AnalogButton Trigger = new(0.6f, 0.4f);

    private static bool pressing;
    private static bool tooting;
    private static bool facing;

    public static void Reset()
    {
        Mouth.Reset();
        Body.Reset();
        Face.Reset();
        Trigger.Reset();
        pressing = false;
        tooting = false;
        facing = false;
    }

    /// <summary>The face gesture holds primary this frame (self-test reads it).</summary>
    public static bool AtFace => facing;

    public static void Tick(Character c, VRRig rig, ItemGesturesConfig cfg, float dt)
    {
        var hand = ItemGestures.HoldingHand(rig);
        if (ItemGestures.ItemChanged)
        {
            Mouth.Reset();
            // An item drawn from a chest slot starts at the chest: it must leave once before a press counts.
            Body.Disarm();
            Face.Reset();
            pressing = tooting = facing = false;
        }
        if (hand == null || rig.Head == null || !VRInput.HeadTracked)
        {
            Mouth.Reset();
            Body.Reset();
            Face.Reset();
            facing = false;
            return;
        }
        UpdateBugle(rig, cfg, hand, dt);
        UpdateBody(rig, cfg, hand, dt);
        UpdateFace(rig, cfg, hand, dt);
    }

    private static void UpdateFace(VRRig rig, ItemGesturesConfig cfg, Transform hand, float dt)
    {
        var use = ItemGestures.Use;
        bool read = use == UseGesture.Read && cfg.ReadAtFace;
        bool commune = use == UseGesture.Commune && cfg.CommuneAtFace;
        bool left = ItemGestures.HoldLeft;
        Trigger.Update(VRInput.Hand(left).Trigger, dt);
        if (!read && !commune)
        {
            Face.Reset();
            facing = false;
            MouthHint(rig, hand, left, use);
            return;
        }
        var head = rig.Head;
        var spot = read
            ? ReadZone.Spot(head.position.N(), head.forward.N()).U()
            : head.position + head.forward * CommuneAhead;
        float distance = Mathf.Max(
            Vector3.Distance(hand.position, spot) - (read ? ReadZone.Radius : CommuneRadius),
            0f
        );
        // The book over bones revives (interact.world); it must never read there.
        if (read && !ReadZone.Allowed(hand.position.N(), ItemGestures.NearHips))
            distance = float.PositiveInfinity;
        bool on = Face.Update(distance, dt);
        var item = ItemGestures.Held;
        if (on && !facing)
        {
            // It starts: warn once that this harms (the warning's tick is the start tick), then the cast's progress.
            Affordance.Danger(left, spot, 0.5f);
        }
        facing = on;
        if (on)
        {
            GestureRequests.Primary();
            if (item != null)
                Affordance.Progress(left, item.castProgress);
        }
        else if (Trigger.Pressed && InteractConfig.Instance.TriggerUse)
        {
            // The trigger is refused for it: warn, and ring the face where it has to go.
            Affordance.DenyAtHand(
                left,
                DenyReason.DangerSoon,
                DenyHint.At(spot),
                ItemGestures.Held
            );
        }
    }

    /// <summary>Clear of the mouth zone (interact.physical eats within its 0.12 m radius), metres.</summary>
    private const float AwayFromMouth = 0.2f;

    /// <summary>
    /// The trigger on food or drink held away from the mouth (Interaction.TriggerEats off): it is not eaten, so say
    /// where it goes instead of doing nothing. A soft refusal at the hand with the would-work ring at the mouth.
    /// </summary>
    private static void MouthHint(VRRig rig, Transform hand, bool left, UseGesture use)
    {
        // At a friend's mouth the trigger is the hand-over's squeeze (interact.social), not a try at eating here.
        if (
            Social.SocialHands.Instance.HandOverOwnsTrigger
            || !Trigger.Pressed
            || !InteractConfig.Instance.TriggerUse
            || (use != UseGesture.Eat && use != UseGesture.Drink)
            || !ItemGestures.TriggerRefused(ItemGestures.Held)
        )
            return;
        var head = rig.Head;
        var mouth = MouthGesture
            .MouthPoint(head.position.N(), head.rotation.N(), MouthGesture.DefaultOffset)
            .U();
        if (Vector3.Distance(hand.position, mouth) < AwayFromMouth)
            return;
        Affordance.DenyAtHand(left, DenyReason.NotHere, DenyHint.At(mouth), ItemGestures.Held);
    }

    private static void UpdateBugle(VRRig rig, ItemGesturesConfig cfg, Transform hand, float dt)
    {
        if (!cfg.BugleAtMouth || ItemGestures.Bugle == null)
        {
            Mouth.Reset();
            tooting = false;
            return;
        }
        bool at = Mouth.Update(rig.Head.position.N(), rig.Head.rotation.N(), hand.position.N(), dt);
        if (at && !tooting)
            Haptics.Pulse(ItemGestures.HoldLeft, 0.3f, 0.05f);
        tooting = at;
        if (at)
            GestureRequests.Primary();
    }

    private static void UpdateBody(VRRig rig, ItemGesturesConfig cfg, Transform hand, float dt)
    {
        if (!cfg.PressToBody || !ItemGestures.StatusItem)
        {
            Body.Reset();
            pressing = false;
            haveBodyHand = false;
            return;
        }
        var head = rig.Head;
        var flat = head.forward;
        flat.y = 0f;
        flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
        var point = hand.position.N();
        float distance = BodyZones.TorsoDistance(point, head.position.N(), flat.N(), TorsoRadius);

        // The other forearm, unless both hands are on the item.
        bool onForearm = false;
        var other = rig.Hand(!ItemGestures.HoldLeft);
        if (!LocalHold.TwoHand && other != null && VRInput.Hand(!ItemGestures.HoldLeft).Tracked)
        {
            float arm =
                BodyZones.ForearmDistance(point, other.position.N(), other.forward.N())
                - ForearmRadius;
            if (arm < distance)
            {
                distance = Mathf.Max(arm, 0f);
                onForearm = true;
            }
        }

        // Held still against the body, not swept past it (a throw's swing crosses the chest).
        var tracked = VRInput.Hand(ItemGestures.HoldLeft).Position;
        float speed = haveBodyHand && dt > 0f ? Vector3.Distance(tracked, lastBodyHand) / dt : 0f;
        lastBodyHand = tracked;
        haveBodyHand = true;
        bool on = Body.Update(BodyPress.Distance(distance, speed, pressing), dt);
        if (on && !pressing)
        {
            // A short wrap: the holding hand, and the arm being wrapped.
            Haptics.Pulse(ItemGestures.HoldLeft, 0.35f, 0.12f);
            if (onForearm)
                Haptics.Pulse(!ItemGestures.HoldLeft, 0.35f, 0.12f);
        }
        pressing = on;
        if (on)
            GestureRequests.Primary();
    }

    /// <summary>The holding hand's tracked position last frame (tracking space, so walking does not count).</summary>
    private static Vector3 lastBodyHand;
    private static bool haveBodyHand;

    private static bool lookSwapped;
    private static Vector3 savedLook;

    /// <summary>
    /// <c>BugleSFX.UpdateTooting</c> prefix body (BugleSFX.cs:43): the game reads the pitch from the holder's
    /// <c>lookDirection</c> against up (BugleSFX.cs:59-60) and sends it as the <c>RPC_StartToot</c> pitch argument.
    /// For the local VR holder that direction becomes the bell's (the holding hand's aim) for this one call;
    /// <see cref="AfterTooting"/> restores it from the finalizer.
    /// </summary>
    public static void BeforeTooting(BugleSFX bugle)
    {
        lookSwapped = false;
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        if (
            !VRSession.Active
            || !ItemGesturesConfig.Instance.BugleAtMouth
            || c == null
            || rig == null
            || !ReferenceEquals(bugle, ItemGestures.Bugle)
            || c.data.currentItem == null
            || !VRInput.Hand(ItemGestures.HoldLeft).Tracked
        )
            return;
        var aim = rig.Aim(ItemGestures.HoldLeft);
        if (aim == null)
            return;
        savedLook = c.data.lookDirection;
        c.data.lookDirection = aim.forward;
        lookSwapped = true;
    }

    public static void AfterTooting()
    {
        var c = Character.localCharacter;
        if (lookSwapped && c != null)
            c.data.lookDirection = savedLook;
        lookSwapped = false;
    }
}
