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
/// <item>Body press (G8): an item applied to the body (<see cref="UseGesture.Apply"/>) rested against your own torso or
/// other forearm holds primary (<see cref="SelfUseDwell"/>: the belt sits in that zone, so a carry past it must not
/// count, #147). Feeding a friend's body is in <c>SocialHands</c>.</item>
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
        Dwell = 0f,
    };
    private static readonly SelfUseDwell BodyUse = new();

    /// <summary>Torso radius around the spine, with the hand's own thickness (tune on headset).</summary>
    private const float TorsoRadius = 0.22f;

    /// <summary>Forearm press distance, metres (audit G8: 15 cm).</summary>
    private const float ForearmRadius = 0.12f;

    private static readonly ContactDwell Face = new()
    {
        Enter = 0f,
        Exit = 0.06f,
        Dwell = 0f,
    };

    // Deliberate: both uses are dangerous, so a hand passing the face never starts one.
    private static readonly SelfUseDwell FaceUse = new() { Dwell = SelfUseDwell.FaceDwell };

    /// <summary>Eye to eye: the holding hand within this of a point just in front of the eyes, metres.</summary>
    private const float CommuneAhead = 0.12f;
    private const float CommuneRadius = 0.14f;

    private static bool tooting;
    private static bool facing;

    public static void Reset()
    {
        Mouth.Reset();
        ResetBody();
        ResetFace();
        tooting = false;
    }

    private static void ResetBody()
    {
        Body.Reset();
        BodyUse.Reset();
        BodyGap = float.PositiveInfinity;
        HandAtBody = false;
    }

    private static void ResetFace()
    {
        Face.Reset();
        FaceUse.Reset();
        facing = false;
    }

    /// <summary>The held item is at your own body this frame, whether or not it counts yet (self-test vr.495).</summary>
    public static bool AtBody => Body.Active;

    /// <summary>How far the held item is from your own torso or other forearm this frame, m (self-test vr.495).</summary>
    public static float BodyGap { get; private set; } = float.PositiveInfinity;

    /// <summary>The hand itself is at the body this frame, so an item there means it (self-test vr.495).</summary>
    public static bool HandAtBody { get; private set; }

    /// <summary>The face gesture holds primary this frame (self-test reads it).</summary>
    public static bool AtFace => facing;

    /// <summary>Where a resting hand is measured (<see cref="HandStill.OnBody"/>).</summary>
    internal static System.Numerics.Vector3 RestPoint(bool left) =>
        HandStill.OnBody(
            VRInput.Hand(left).Position.N(),
            VRInput.HeadPosition.N(),
            VRInput.HeadRotation.N()
        );

    /// <summary>An item arrived in this hand (drawn from a slot, picked up at the body): it did not come here to be used,
    /// so the hand has to move once first.</summary>
    internal static void Arrive(bool left) => BodyUse.Arrive(RestPoint(left));

    public static void Tick(VRRig rig, ItemGesturesConfig cfg, float dt)
    {
        var hand = ItemGestures.HoldingHand(rig);
        if (ItemGestures.ItemChanged)
        {
            Mouth.Reset();
            ResetBody();
            // Back from a menu with the item still in hand. A new item's hand is not known yet: TrackHeldItem runs later
            // this frame and calls this again for the right hand.
            Arrive(ItemGestures.HoldLeft);
            ResetFace();
            tooting = false;
        }
        if (hand == null || rig.Head == null || !VRInput.HeadTracked)
        {
            Mouth.Reset();
            ResetBody();
            ResetFace();
            tooting = false;
            // No face to hold it to: a trigger refused for a face-only item (TriggerRefused) says so at the hand.
            bool left = ItemGestures.HoldLeft;
            if (TriggerPressed(left) && ItemGestures.TriggerRefused(ItemGestures.Held))
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
        if (!read && !commune)
        {
            ResetFace();
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
        bool on = FaceUse.Update(
            RestPoint(left),
            Face.Update(distance, dt),
            Inventory.BodyInventory.Instance.TakesHeld(Character.localCharacter, left),
            dt
        );
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
        else if (TriggerPressed(left))
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

    /// <summary>The holding hand's trigger made a use press: one squeezed with a closing fist or on the climber is
    /// muted (Core <see cref="UseTrigger"/>) and must not be refused either (#74). Read before this frame's use pass
    /// (CharacterInput.Sample runs this first), so it is last frame's press.</summary>
    private static bool TriggerPressed(bool left) => PhysicalInteraction.UsePressed(left);

    /// <summary>Clear of the mouth zone (interact.physical eats within its 0.12 m radius), metres.</summary>
    internal const float AwayFromMouth = 0.2f;

    /// <summary>Your own mouth, in world space (the eat zone's point, placed in real metres around the eyes).</summary>
    internal static Vector3 OwnMouth(Transform head) =>
        RealScale.World(
            MouthGesture
                .MouthPoint(head.position.N(), head.rotation.N(), MouthGesture.DefaultOffset)
                .U(),
            head.position
        );

    /// <summary>Real metres from the held item, as the tracked hand carries it, to <paramref name="mouth"/>: under
    /// <see cref="AwayFromMouth"/> a trigger on food is an eat try at your own mouth, never refused.</summary>
    internal static float OwnMouthGap(Transform hand, bool left, Vector3 mouth) =>
        RealScale.Real(
            Vector3.Distance(ItemGestures.MouthContact(ItemGestures.Held, hand, left, mouth), mouth)
        );

    /// <summary>
    /// The trigger on food or drink held away from the mouth (Interaction.TriggerEats off): it is not eaten, so say
    /// where it goes instead of doing nothing. A soft refusal at the hand with the would-work ring at the mouth.
    /// </summary>
    private static void MouthHint(VRRig rig, Transform hand, bool left, UseGesture use)
    {
        // At a friend's mouth the trigger is the hand-over's squeeze (interact.social), not a try at eating here.
        if (
            Social.SocialHands.Instance.HandOverOwnsTrigger
            || !TriggerPressed(left)
            || (use != UseGesture.Eat && use != UseGesture.Drink)
            || !ItemGestures.TriggerRefused(ItemGestures.Held)
        )
            return;
        var mouth = OwnMouth(rig.Head);
        if (OwnMouthGap(hand, left, mouth) < AwayFromMouth)
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
            ResetBody();
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
        float torso = Mathf.Min(
            BodyZones.TorsoDistance(point, head.position.N(), flat.N(), TorsoRadius),
            BodyZones.TorsoDistance(itemPoint, head.position.N(), flat.N(), TorsoRadius)
        );

        // The other forearm, unless both hands are on the item.
        float arm = float.PositiveInfinity;
        bool handAtArm = false;
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
            arm =
                Mathf.Min(
                    BodyZones.ForearmDistance(point, otherReal, other.Fingers().N()),
                    BodyZones.ForearmDistance(onArm, otherReal, other.Fingers().N())
                ) - ForearmRadius;
            handAtArm = BodyZones.HandAtForearm(
                point,
                otherReal,
                other.Fingers().N(),
                ForearmRadius
            );
        }
        float distance = BodyZones.Rest(
            torso,
            arm,
            BodyZones.HandAtTorso(point, head.position.N(), flat.N(), TorsoRadius),
            handAtArm,
            out bool handThere,
            out bool onForearm
        );
        BodyGap = distance;
        HandAtBody = handThere;

        // Rested against the body, not carried past it to a slot or swung across it in a throw.
        bool left = ItemGestures.HoldLeft;
        bool was = BodyUse.Active;
        bool on = BodyUse.Update(
            RestPoint(left),
            // The item's reach decides contact, the hand decides whether it means it; a use under way rides that out.
            Body.Update(distance, dt) && (was || handThere),
            Inventory.BodyInventory.Instance.TakesHeld(Character.localCharacter, left),
            dt
        );
        if (BodyUse.Settling || BodyUse.NeedsStill)
        {
            // Nothing is spent yet, and leaving is free: say what it takes before the game's cast ring takes over. At
            // the hand, as the chest point is inside the torso and unseen.
            var spot = onForearm ? other.position : hand.position;
            Affordance.Mark(left, spot, DenyReason.HappenedHere, DenyHint.At(spot), "Hold still");
            if (BodyUse.Settling)
                Affordance.Progress(left, BodyUse.Fraction);
        }
        else if (BodyUse.Parked)
            SlotCue(rig, left);
        if (on && !was)
        {
            // A short wrap: the holding hand, and the arm being wrapped.
            Haptics.Pulse(ItemGestures.HoldLeft, 0.35f, 0.12f);
            if (onForearm)
                Haptics.Pulse(!ItemGestures.HoldLeft, 0.35f, 0.12f);
        }
        if (on)
            GestureRequests.Primary();
    }

    /// <summary>A rest at a slot started nothing, on purpose (<see cref="SelfUseDwell"/>): say what the slot does instead,
    /// which matters most with the <c>Chest</c> layout, whose slots cover most of the chest.</summary>
    private static void SlotCue(VRRig rig, bool left)
    {
        var inv = Inventory.BodyInventory.Instance;
        var c = Character.localCharacter;
        int slot = inv.HeldSlot(c, left);
        if (slot < 0)
            return;
        var at = rig.Origin.TransformPoint(inv.Anchor(slot));
        var refusal = inv.StowRefusal(c, slot);
        if (refusal == DenyReason.None)
            Affordance.Mark(left, at, DenyReason.HappenedHere, DenyHint.At(at), "Let go to stow");
        else
            Affordance.Hover(left, at, refusal);
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
