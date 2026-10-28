using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Rig;
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

    public static void Tick(VRRig rig, ItemGesturesConfig cfg, float dt)
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
            pressing = tooting = facing = haveBodyHand = false;
            // No face to hold it to: a trigger refused for a face-only item (TriggerRefused) says so at the hand.
            bool left = ItemGestures.HoldLeft;
            Trigger.Update(VRInput.Hand(left).Trigger, dt);
            if (
                Trigger.Pressed
                && InteractConfig.Instance.TriggerUse
                && ItemGestures.TriggerRefused(ItemGestures.Held)
            )
                Affordance.DenyAtHand(left, DenyReason.NotNow, default, ItemGestures.Held);
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
        var spot = FaceSpot(rig.Head, read);
        // The item, not the controller: the open book's middle at the read spot, the item itself eye to eye (the game's
        // props are big; a cursed skull held at the eyes by the hand sat beside the head).
        var item = ItemGestures.Held;
        var at = read
            ? ItemMiddle(item, hand, left)
            : HeldItemPoints.Nearest(item, hand, left, spot);
        float distance = Mathf.Max(
            RealScale.Real(Vector3.Distance(at, spot)) - (read ? ReadZone.Radius : CommuneRadius),
            0f
        );
        // The book over bones revives (interact.world); it must never read there.
        if (read && !ReadZone.Allowed(hand.position.N(), ItemGestures.NearHips))
            distance = float.PositiveInfinity;
        bool on = Face.Update(distance, dt);
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

    /// <summary>Where the face gesture holds the item: the read spot (<see cref="ReadZone"/>) or just in front of the
    /// eyes, placed in real metres around them (Camera.WorldScale) like the mouth and chest zones.</summary>
    internal static Vector3 FaceSpot(Transform head, bool read) =>
        RealScale.World(
            read
                ? ReadZone.Spot(head.position.N(), head.forward.N()).U()
                : head.position + head.forward * CommuneAhead,
            head.position
        );

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
        var mouth = RealScale.World(
            MouthGesture
                .MouthPoint(head.position.N(), head.rotation.N(), MouthGesture.DefaultOffset)
                .U(),
            head.position
        );
        if (
            RealScale.Real(
                Vector3.Distance(
                    ItemGestures.MouthContact(ItemGestures.Held, hand, left, mouth),
                    mouth
                )
            ) < AwayFromMouth
        )
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
        // The mouthpiece, not the hand: the game's bugle is held under its loop, its mouthpiece 0.2 m above the grip
        // (tester: "the trigger point should be where the mouthpiece is, not where the hand is").
        BuglePoint = Mouthpiece(ItemGestures.Held, hand, ItemGestures.HoldLeft);
        bool at = Mouth.Update(
            rig.Head.position.N(),
            rig.Head.rotation.N(),
            RealScale.Real(BuglePoint, rig.Head.position).N(),
            dt
        );
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
        // The hand or the item it holds against the body, whichever is nearer: a bandage roll is pressed with the item.
        // Your own chest and forearm: judged in real metres around the eyes (Camera.WorldScale).
        var eye = head.position;
        var point = RealScale.Real(hand.position, eye).N();
        var chest = eye + Vector3.down * RealScale.World(ChestBelowEyes);
        var itemPoint = RealScale
            .Real(
                HeldItemPoints.Nearest(ItemGestures.Held, hand, ItemGestures.HoldLeft, chest),
                eye
            )
            .N();
        float distance = Mathf.Min(
            BodyZones.TorsoDistance(point, head.position.N(), flat.N(), TorsoRadius),
            BodyZones.TorsoDistance(itemPoint, head.position.N(), flat.N(), TorsoRadius)
        );

        // The other forearm, unless both hands are on the item.
        bool onForearm = false;
        var other = rig.Hand(!ItemGestures.HoldLeft);
        if (!LocalHold.TwoHand && other != null && VRInput.Hand(!ItemGestures.HoldLeft).Tracked)
        {
            var onArm = RealScale
                .Real(
                    HeldItemPoints.Nearest(
                        ItemGestures.Held,
                        hand,
                        ItemGestures.HoldLeft,
                        other.position
                    ),
                    eye
                )
                .N();
            var otherReal = RealScale.Real(other.position, eye).N();
            float arm =
                Mathf.Min(
                    BodyZones.ForearmDistance(point, otherReal, other.Fingers().N()),
                    BodyZones.ForearmDistance(onArm, otherReal, other.Fingers().N())
                ) - ForearmRadius;
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

    /// <summary>The chest the held item is pressed against: this far under the eyes, metres.</summary>
    private const float ChestBelowEyes = 0.4f;

    /// <summary>Where the held bugle's mouthpiece is drawn this frame (self-test vr.21 steers it to the mouth).</summary>
    public static Vector3 BuglePoint { get; private set; }

    private static Item mouthpieceItem;
    private static Vector3 mouthpieceLocal;

    /// <summary>The bugle's mouthpiece on the holding hand: the end of its longest side away from the bell (its
    /// play particles, BugleSFX.cs:22), else the end nearer the grip. The hand itself when unknown.</summary>
    private static Vector3 Mouthpiece(Item item, Transform hand, bool left)
    {
        var info = HeldItemPoints.Info(item);
        if (info == null)
            return hand.position;
        if (!ReferenceEquals(item, mouthpieceItem))
        {
            mouthpieceItem = item;
            var bugle = ItemGestures.Bugle;
            // No bell known: the mouthpiece is the end nearer the hand, so look away from the far end.
            var bell =
                bugle != null && bugle.particle1 != null
                    ? item.transform.InverseTransformPoint(bugle.particle1.transform.position)
                    : info.BoundsMin + info.BoundsMax - info.GripLocal;
            mouthpieceLocal = HeldItemShape
                .FarEnd(info.BoundsMin.N(), info.BoundsMax.N(), bell.N())
                .U();
        }
        return HeldItemPoints.World(item, hand, left, mouthpieceLocal, out var p)
            ? p
            : hand.position;
    }

    /// <summary>The middle of the held item's drawn bounds on the holding hand (the read point; self-test vr.165
    /// steers it to the read spot).</summary>
    internal static Vector3 ItemMiddle(Item item, Transform hand, bool left)
    {
        var info = HeldItemPoints.Info(item);
        return
            info != null
            && HeldItemPoints.World(
                item,
                hand,
                left,
                (info.BoundsMin + info.BoundsMax) * 0.5f,
                out var p
            )
            ? p
            : hand.position;
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
