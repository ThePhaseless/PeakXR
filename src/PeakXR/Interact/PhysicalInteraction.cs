using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.VR;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace PeakXR.Interact;

/// <summary>
/// Per-frame hand logic for the local VR player, run right after the game samples its input
/// (<c>CharacterInput.Sample</c> postfix). It turns grips, triggers and gestures into the game's own inputs, so
/// every effect goes through vanilla paths and RPCs: interact (pickup), use, drop/throw and wall climbing.
/// Hands are independent: each has its own grip state and action; only one item is held at a time.
/// </summary>
internal static class PhysicalInteraction
{
    public delegate void TryStartClimb(
        CharacterClimbing self,
        bool forceAttempt,
        Vector3 overide,
        bool botGrab,
        float raycastDistance
    );

    /// <summary><c>CharacterClimbing.TryToStartWallClimb</c> (CharacterClimbing.cs:534, private), resolved in Install.</summary>
    public static TryStartClimb StartWallClimb;

    private sealed class Hand
    {
        public readonly bool Left;
        public readonly AnalogButton Grip = new(0.6f, 0.4f);
        public readonly AnalogButton Trigger = new(0.6f, 0.4f);
        public readonly ThrowEstimator Throw = new();
        public GripPressAction Action;
        public IInteractible Target;

        public Hand(bool left) => Left = left;
    }

    private static readonly Hand LeftHand = new(true);
    private static readonly Hand RightHand = new(false);
    private static readonly Hand[] Hands = { LeftHand, RightHand };
    private static readonly ButtonEdge InteractEdge = new();
    private static readonly ButtonEdge PrimaryEdge = new();
    private static readonly ButtonEdge SecondaryEdge = new();
    private static readonly ButtonEdge DropEdge = new();
    private static readonly MouthGesture Mouth = new();
    private static readonly ArmLocomotion Arms = new();
    private static readonly ItemHoldInfo Hold = new();
    private static readonly Collider[] Overlap = new Collider[32];
    private static readonly NVector3[] CandidatePositions = new NVector3[32];
    private static readonly IInteractible[] Candidates = new IInteractible[32];

    private static Item lastItem;
    private static bool pendingLeft;
    private static float pendingTime = -10f;
    private static bool physicalClimb;
    private static Vector2 climbInput;
    private static bool dropPending;
    private static bool useBlocked;
    private static int throwFrame = -10;

    /// <summary>What a grabbing hand selected; the interaction raycast patch hands it to the game.</summary>
    public static IInteractible GrabTarget { get; private set; }

    /// <summary>A grip is acting as the interact button (nothing grabbable was in reach).</summary>
    public static bool GripInteractActive { get; private set; }

    /// <summary>Hand whose aim ray replaces the head for interaction this frame: -1 none, 0 left, 1 right.</summary>
    public static int AimHand { get; private set; } = -1;
    public static bool ThrowPending => Time.frameCount - throwFrame <= 2;
    public static Vector3 ThrowVelocity { get; private set; }

    public static void ConsumeThrow() => throwFrame = -10;

    public static void Tick(Character c, CharacterInput input, bool active)
    {
        var rig = VRRig.Instance;
        if (!VRSession.Active || rig == null || c != Character.localCharacter)
        {
            AimHand = -1;
            return;
        }
        var cfg = InteractConfig.Instance;
        float dt = Time.deltaTime;
        float now = Time.time;

        TrackHeldItem(c, now);
        UpdateHand(LeftHand, c, rig, cfg, active, dt, now);
        UpdateHand(RightHand, c, rig, cfg, active, dt, now);
        UpdateAim(cfg);
        UpdateClimb(c, rig, cfg, dt);
        if (active)
            Inject(c, input, rig, cfg, dt);
    }

    private static void TrackHeldItem(Character c, float now)
    {
        var item = c.data.currentItem;
        if (item == lastItem)
            return;
        lastItem = item;
        LocalHold.TwoHand = false;
        EndAction(LeftHand, GripPressAction.SecondHand);
        EndAction(RightHand, GripPressAction.SecondHand);
        Mouth.Reset();
        if (item != null)
            LocalHold.ItemInLeftHand = now - pendingTime < 3f && pendingLeft;
        pendingTime = -10f;
    }

    private static void EndAction(Hand hand, GripPressAction action)
    {
        if (hand.Action == action)
            hand.Action = GripPressAction.None;
    }

    private static void UpdateHand(
        Hand hand,
        Character c,
        VRRig rig,
        InteractConfig cfg,
        bool active,
        float dt,
        float now
    )
    {
        ref var state = ref VRInput.Hand(hand.Left);
        hand.Grip.Update(state.Tracked ? state.Grip : 0f, dt);
        hand.Trigger.Update(state.Tracked ? state.Trigger : 0f, dt);
        if (state.Tracked)
            hand.Throw.AddSample(state.Position.N(), now);

        if (hand.Grip.Pressed && active && rig.Hand(hand.Left) != null)
            OnGripPress(hand, c, rig, cfg, now);
        else if (hand.Grip.Released)
            OnGripRelease(hand, c, rig, cfg);
    }

    private static bool Holding(Character c, out Item item)
    {
        item = c.data.currentItem;
        return item != null && Hold.Bind(item);
    }

    private static void OnGripPress(
        Hand hand,
        Character c,
        VRRig rig,
        InteractConfig cfg,
        float now
    )
    {
        var t = rig.Hand(hand.Left);
        var pos = t.position;
        bool holding = Holding(c, out var item);
        bool thisHolds = holding && LocalHold.ItemInLeftHand == hand.Left;

        var ctx = new GripContext
        {
            PhysicalGrab = cfg.PhysicalGrab,
            PhysicalClimb = cfg.PhysicalClimb,
            HoldingItem = holding,
            ThisHandHolds = thisHolds,
            HeldHandedness = holding ? Hold.Profile.Handedness : Handedness.OneHanded,
            NearHeldItem = holding && !thisHolds && NearItem(item, pos, cfg.GrabRadius * 1.5f),
            NearClimbable =
                cfg.PhysicalClimb
                && Physics.CheckSphere(
                    pos,
                    cfg.ClimbRadius,
                    HelperFunctions.terrainMapMask,
                    QueryTriggerInteraction.Ignore
                ),
        };
        IInteractible target = null;
        if (cfg.PhysicalGrab)
            ctx.Candidate = FindCandidate(c, pos, t.forward, cfg.GrabRadius, out target);

        var action = GripIntent.OnPress(ctx);
        switch (action)
        {
            case GripPressAction.GrabItem:
                hand.Target = target;
                pendingLeft = hand.Left;
                pendingTime = now;
                Haptics.Pulse(hand.Left, 0.6f, 0.05f);
                break;
            case GripPressAction.GrabInteractible:
                hand.Target = target;
                Haptics.Pulse(hand.Left, 0.6f, 0.05f);
                break;
            case GripPressAction.SecondHand:
                LocalHold.TwoHand = true;
                Haptics.Pulse(hand.Left, 0.4f, 0.04f);
                break;
            case GripPressAction.Climb:
                if (StartClimb(c, rig, pos, cfg))
                    Haptics.Pulse(hand.Left, 0.8f, 0.06f);
                else
                    action = GripPressAction.None;
                break;
            case GripPressAction.Regrip:
                Haptics.Pulse(hand.Left, 0.2f, 0.03f);
                break;
        }
        hand.Action = action;
    }

    private static void OnGripRelease(Hand hand, Character c, VRRig rig, InteractConfig cfg)
    {
        var action = hand.Action;
        hand.Action = GripPressAction.None;
        hand.Target = null;

        switch (action)
        {
            case GripPressAction.SecondHand:
                LocalHold.TwoHand = false;
                break;
            case GripPressAction.Climb:
                if (Other(hand).Action != GripPressAction.Climb)
                    EndClimb(c);
                break;
            case GripPressAction.GrabItem:
            case GripPressAction.Regrip:
                if (!Holding(c, out var item) || LocalHold.ItemInLeftHand != hand.Left)
                    break;
                var velocity = ReleaseVelocity(hand, c, rig);
                var result = GripIntent.OnRelease(
                    true,
                    item.UIData.canDrop,
                    velocity.magnitude,
                    cfg.ThrowSpeed,
                    cfg.PhysicalThrow,
                    cfg.ReleaseToDrop
                );
                if (result == GripReleaseAction.Throw && !item.UIData.canThrow)
                    result = GripReleaseAction.Keep;
                if (result == GripReleaseAction.Throw)
                {
                    ThrowVelocity = velocity * cfg.ThrowScale;
                    throwFrame = Time.frameCount;
                    dropPending = true;
                    Haptics.Pulse(hand.Left, 0.5f, 0.05f);
                }
                else if (result == GripReleaseAction.Drop)
                {
                    dropPending = true;
                }
                break;
        }
    }

    private static Hand Other(Hand hand) => hand.Left ? RightHand : LeftHand;

    /// <summary>World release velocity: the tracked hand's fitted velocity, rotated from tracking space, plus the
    /// body's own velocity.</summary>
    private static Vector3 ReleaseVelocity(Hand hand, Character c, VRRig rig)
    {
        var local = hand.Throw.GetReleaseVelocity().U();
        var world = rig.Origin != null ? rig.Origin.TransformVector(local) : local;
        if (c.refs.hip != null)
            world += c.refs.hip.Rig.linearVelocity;
        return world;
    }

    private static bool NearItem(Item item, Vector3 pos, float radius)
    {
        float r2 = radius * radius;
        foreach (var col in item.colliders)
        {
            if (
                col != null
                && col.enabled
                && (col.bounds.ClosestPoint(pos) - pos).sqrMagnitude <= r2
            )
                return true;
        }
        return (item.Center() - pos).sqrMagnitude <= 4f * r2;
    }

    private static GrabCandidate FindCandidate(
        Character c,
        Vector3 pos,
        Vector3 forward,
        float radius,
        out IInteractible target
    )
    {
        target = null;
        int hits = Physics.OverlapSphereNonAlloc(
            pos,
            radius,
            Overlap,
            HelperFunctions.AllPhysicalExceptCharacter,
            QueryTriggerInteraction.Collide
        );
        int count = 0;
        for (int i = 0; i < hits && count < Candidates.Length; i++)
        {
            var col = Overlap[i];
            Overlap[i] = null;
            if (col == null)
                continue;
            var it = col.GetComponentInParent<IInteractible>();
            if (it == null || it is CharacterInteractible)
                continue;
            if (it is Item held && held == c.data.currentItem)
                continue;
            if (!it.IsInteractible(c))
                continue;
            Candidates[count] = it;
            CandidatePositions[count] = col.bounds.ClosestPoint(pos).N();
            count++;
        }

        int best = GrabSelector.Select(
            pos.N(),
            forward.N(),
            CandidatePositions,
            null,
            count,
            radius
        );
        if (best >= 0)
            target = Candidates[best];
        for (int i = 0; i < count; i++)
            Candidates[i] = null;

        if (target == null)
            return GrabCandidate.None;
        return target is Item ? GrabCandidate.Item : GrabCandidate.Interactible;
    }

    private static bool StartClimb(Character c, VRRig rig, Vector3 handPos, InteractConfig cfg)
    {
        if (c.data.isClimbing)
        {
            BeginPhysicalClimb();
            return true;
        }
        if (StartWallClimb == null || !c.refs.climbing.CanClimb())
            return false;

        // Vanilla casts from the camera along a direction; aim it through the gripping hand.
        var from =
            MainCamera.instance != null
                ? MainCamera.instance.transform.position
                : rig.Head.position;
        var dir = handPos - from;
        float dist = dir.magnitude;
        if (dist < 0.01f)
            return false;
        StartWallClimb(c.refs.climbing, true, dir / dist, false, dist + cfg.ClimbRadius + 0.15f);
        if (!c.data.isClimbing)
            return false;
        BeginPhysicalClimb();
        return true;
    }

    private static void BeginPhysicalClimb()
    {
        if (!physicalClimb)
            Arms.Reset();
        physicalClimb = true;
    }

    private static void EndClimb(Character c)
    {
        if (physicalClimb && c.data.isClimbing && !PrimaryEdge.Held)
            c.refs.climbing.StopClimbing();
        physicalClimb = false;
        climbInput = Vector2.zero;
        Arms.Reset();
    }

    private static void UpdateClimb(Character c, VRRig rig, InteractConfig cfg, float dt)
    {
        if (physicalClimb && !c.data.isClimbing)
        {
            // The game ended the climb (jump, stamina, ragdoll): grips must close again to climb.
            physicalClimb = false;
            climbInput = Vector2.zero;
            EndAction(LeftHand, GripPressAction.Climb);
            EndAction(RightHand, GripPressAction.Climb);
            Arms.Reset();
        }
        if (!physicalClimb || dt <= 0f)
            return;

        bool l = LeftHand.Action == GripPressAction.Climb && VRInput.Left.Tracked;
        bool r = RightHand.Action == GripPressAction.Climb && VRInput.Right.Tracked;
        var d = Arms.Update(l, VRInput.Left.Position.N(), r, VRInput.Right.Position.N(), dt).U();
        var world = (rig.Origin != null ? rig.Origin.TransformVector(d) : d) * cfg.ClimbGain;
        var climbing = c.refs.climbing;
        var target = ClimbInput.FromBodyVelocity(
            (world / dt).N(),
            c.data.climbNormal.N(),
            climbing.climbSpeed * climbing.climbSpeedMod,
            0.1f
        );
        var smoothed = ClimbInput.Smooth(
            new System.Numerics.Vector2(climbInput.x, climbInput.y),
            target,
            12f,
            dt
        );
        climbInput = new Vector2(smoothed.X, smoothed.Y);
    }

    private static void UpdateAim(InteractConfig cfg)
    {
        AimHand = -1;
        if (!cfg.HandAimedInteract)
            return;
        bool l = VRInput.Left.Tracked && GripIntent.IsPointing(LocalAvatar.ControllerCurls(true));
        bool r = VRInput.Right.Tracked && GripIntent.IsPointing(LocalAvatar.ControllerCurls(false));
        if (l && LeftHand.Action == GripPressAction.Interact)
            AimHand = 0;
        else if (r)
            AimHand = 1;
        else if (l)
            AimHand = 0;
    }

    private static void Inject(
        Character c,
        CharacterInput input,
        VRRig rig,
        InteractConfig cfg,
        float dt
    )
    {
        // Interact: grabs and the grip fallback press the game's interact button while the grip stays closed.
        GrabTarget = null;
        GripInteractActive = false;
        bool interactDown = false;
        foreach (var hand in Hands)
        {
            switch (hand.Action)
            {
                case GripPressAction.GrabItem:
                case GripPressAction.GrabInteractible:
                    interactDown = true;
                    GrabTarget ??= hand.Target;
                    break;
                case GripPressAction.Interact:
                    interactDown = true;
                    GripInteractActive = true;
                    break;
            }
        }
        InteractEdge.Update(interactDown, dt);
        input.interactWasPressed |= InteractEdge.Pressed;
        input.interactIsPressed |= InteractEdge.Held;
        input.interactWasReleased |= InteractEdge.Released;

        // Use: the holding hand's trigger (right when empty-handed) is primary, the other trigger secondary.
        bool holding = Holding(c, out var item);
        var primaryHand = holding && LocalHold.ItemInLeftHand ? LeftHand : RightHand;
        bool primaryDown = cfg.TriggerUse && primaryHand.Trigger.Held;
        bool secondaryDown = cfg.TriggerUse && Other(primaryHand).Trigger.Held;

        bool atMouth = false;
        if (cfg.EatAtMouth && holding && Hold.Edible && VRInput.HeadTracked && rig.Head != null)
        {
            bool was = Mouth.Active;
            atMouth = Mouth.Update(
                rig.Head.position.N(),
                rig.Head.rotation.N(),
                item.Center().N(),
                dt
            );
            if (atMouth && !was)
                Haptics.Pulse(primaryHand.Left, 0.3f, 0.05f);
        }
        else
        {
            Mouth.Reset();
        }
        PrimaryEdge.Update(primaryDown || atMouth, dt);
        SecondaryEdge.Update(secondaryDown, dt);
        if (PrimaryEdge.Pressed && holding)
            Haptics.Pulse(primaryHand.Left, 0.3f, 0.03f);

        if (holding && !ItemHoldTable.CanUse(Hold.Profile.Handedness, LocalHold.TwoHand))
        {
            // Two-handed item held in one hand: carry only. Cancel a use that was in progress.
            bool wasUsing =
                input.usePrimaryIsPressed
                || input.useSecondaryIsPressed
                || PrimaryEdge.Held
                || SecondaryEdge.Held;
            input.usePrimaryWasPressed = input.usePrimaryIsPressed = false;
            input.useSecondaryWasPressed = input.useSecondaryIsPressed = false;
            input.usePrimaryWasReleased = input.useSecondaryWasReleased = !useBlocked && wasUsing;
            useBlocked = true;
        }
        else
        {
            useBlocked = false;
            input.usePrimaryWasPressed |= PrimaryEdge.Pressed;
            input.usePrimaryIsPressed |= PrimaryEdge.Held;
            input.usePrimaryWasReleased |= PrimaryEdge.Released;
            input.useSecondaryWasPressed |= SecondaryEdge.Pressed;
            input.useSecondaryIsPressed |= SecondaryEdge.Held;
            input.useSecondaryWasReleased |= SecondaryEdge.Released;
        }

        // Drop: button fallback with the game's hold-to-charge, or a one-frame press+release from a hand release.
        DropEdge.Update(cfg.DropButtonDown(), dt);
        input.dropWasPressed |= DropEdge.Pressed;
        input.dropIsPressed |= DropEdge.Held;
        input.dropWasReleased |= DropEdge.Released;
        if (dropPending)
        {
            dropPending = false;
            input.dropWasPressed = true;
            input.dropWasReleased = true;
            input.dropIsPressed = false;
        }

        // Climbing: arm pulls become the game's climb stick input; a released trigger must not end a grip climb.
        if (physicalClimb)
        {
            input.movementInput = Vector2.ClampMagnitude(input.movementInput + climbInput, 1f);
            input.usePrimaryWasReleased = false;
        }
    }
}
