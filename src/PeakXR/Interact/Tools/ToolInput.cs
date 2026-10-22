using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Interact.Tools;

/// <summary>
/// Scroll gestures for held tools. A wrist twist works as a knob: the holding hand's twist holds the spool's scroll
/// buttons while it keeps turning, and each step of either hand's twist presses the cannon's angle buttons once.
/// The free hand closing on a held spool and pulling away pays rope out through <c>scrollInput</c>. Values are
/// added to whatever the player or other features already put in the scroll fields.
/// </summary>
internal static class ToolInput
{
    /// <summary>Twist axis: the controller's forward, along the forearm in the grip pose.</summary>
    private static readonly System.Numerics.Vector3 TwistAxis = new(0f, 0f, 1f);

    /// <summary>A single twist step keeps the spool scrolling this long, so slow twists still move the rope.</summary>
    private const float StepHoldSeconds = 0.2f;

    /// <summary>Most queued cannon steps; the game takes one press per frame.</summary>
    private const int MaxPendingSteps = 8;

    private sealed class Knob
    {
        public readonly TwistKnob Twist = new();
        public Quaternion Previous;
        public bool HasPrevious;

        public void Reset()
        {
            Twist.Reset();
            HasPrevious = false;
        }
    }

    private static readonly Knob LeftKnob = new();
    private static readonly Knob RightKnob = new();
    private static readonly RopePull Pull = new();
    private static readonly AnalogButton PullGrip = new(0.6f, 0.4f);
    private static readonly LimitStop SpoolStop = new();
    private static readonly LimitStop CannonStop = new();

    private static Item cachedItem;
    private static RopeSpool spool;
    private static Action_ConstructableScoutCannonScroll cannon;
    private static bool holdLeft;
    private static int pendingSteps;
    private static float stepHold;
    private static int stepHoldDir;

    public static void Tick(Character c, CharacterInput input, bool playerMovementActive)
    {
        var item = c.data.currentItem;
        if (item != cachedItem)
        {
            cachedItem = item;
            spool = item != null ? item.GetComponent<RopeSpool>() : null;
            cannon =
                item != null ? item.GetComponent<Action_ConstructableScoutCannonScroll>() : null;
            ResetAll();
        }
        var rig = VRRig.Instance;
        if ((spool == null && cannon == null) || rig == null || !playerMovementActive)
        {
            ResetAll();
            return;
        }
        if (LocalHold.ItemInLeftHand != holdLeft)
        {
            holdLeft = LocalHold.ItemInLeftHand;
            ResetAll();
        }

        var cfg = ToolsConfig.Instance;
        float dt = Time.deltaTime;
        int holdSteps = 0;
        int freeSteps = 0;
        if (cfg.TwistScroll)
        {
            holdSteps = Turn(holdLeft, cfg.TwistStepDegrees, dt);
            if (cannon != null)
                freeSteps = Turn(!holdLeft, cfg.TwistStepDegrees, dt);
            else
                KnobFor(!holdLeft).Reset();
        }
        else
        {
            LeftKnob.Reset();
            RightKnob.Reset();
        }

        if (spool != null)
            DriveSpool(input, rig, cfg, holdSteps, dt);
        if (cannon != null)
            DriveCannon(input, holdSteps, freeSteps);
    }

    private static void DriveSpool(
        CharacterInput input,
        VRRig rig,
        ToolsConfig cfg,
        int steps,
        float dt
    )
    {
        if (steps != 0)
        {
            stepHold = StepHoldSeconds;
            stepHoldDir = steps > 0 ? 1 : -1;
            if (!SpoolStop.Pressing)
                Haptics.Pulse(holdLeft, 0.15f, 0.02f);
        }
        else if (stepHold > 0f)
        {
            stepHold -= dt;
        }
        int dir = KnobFor(holdLeft).Twist.Direction;
        if (dir == 0 && stepHold > 0f)
            dir = stepHoldDir;
        if (dir > 0)
            input.scrollForwardIsPressed = true;
        else if (dir < 0)
            input.scrollBackwardIsPressed = true;
        SpoolEndStop(holdLeft, dir);

        bool freeLeft = !holdLeft;
        ref var free = ref VRInput.Hand(freeLeft);
        var freeHand = rig.Hand(freeLeft);
        if (!cfg.PullRope || !free.Tracked || freeHand == null || spool.ropeBase == null)
        {
            Pull.Reset();
            PullGrip.Reset();
            return;
        }
        PullGrip.Update(free.Grip, dt);
        float distance = Vector3.Distance(freeHand.position, spool.ropeBase.position);
        float scroll = Pull.Update(PullGrip.Held, distance, dt, out int ticks);
        if (!Pull.Active)
            return;
        input.scrollInput += scroll;
        if (dir == 0)
            SpoolEndStop(
                freeLeft,
                scroll > 0f ? 1
                    : scroll < 0f ? -1
                    : 0
            );
        if (ticks != 0 && !SpoolStop.Pressing)
            Haptics.Pulse(freeLeft, 0.2f, 0.02f);
    }

    /// <summary>The rope can go no further (RopeSpool.cs:136-138 clamps the length to <c>minSegments</c> and to the
    /// rope left, at most <c>Rope.MaxSegments</c>): one end-stop thunk on the hand that pushed it there.</summary>
    private static void SpoolEndStop(bool left, int push)
    {
        if (push == 0)
        {
            SpoolStop.Update(0f, 0f, 0f, 0);
            return;
        }
        // The fuel entry read without RopeFuel's getter, which allocates its default delegate (RopeSpool.cs:39-44).
        var data = spool.item.data;
        float fuel =
            data != null && data.TryGetDataEntry<FloatItemData>(DataEntryKey.Fuel, out var entry)
                ? entry.Value
                : spool.ropeStartFuel;
        float max = Mathf.Min(fuel, Rope.MaxSegments);
        if (SpoolStop.Update(spool.Segments, spool.minSegments, max, push))
            UI.Affordances.Affordance.EndStop(left);
    }

    private static void DriveCannon(CharacterInput input, int holdSteps, int freeSteps)
    {
        pendingSteps += holdSteps + freeSteps;
        if (pendingSteps > MaxPendingSteps)
            pendingSteps = MaxPendingSteps;
        else if (pendingSteps < -MaxPendingSteps)
            pendingSteps = -MaxPendingSteps;
        if (pendingSteps == 0)
            return;

        int push = pendingSteps > 0 ? 1 : -1;
        if (pendingSteps > 0)
        {
            input.scrollForwardWasPressed = true;
            pendingSteps--;
        }
        else
        {
            input.scrollBackwardWasPressed = true;
            pendingSteps++;
        }
        var constructable = cannon.constructable;
        if (constructable == null || constructable.currentPreview == null)
            return;
        bool left = freeSteps != 0 ? !holdLeft : holdLeft;
        // Action_ConstructableScoutCannonScroll.Scrolled clamps the angle to +-maxAngle (:52-58).
        if (CannonStop.Update(constructable.angleOffset, -cannon.maxAngle, cannon.maxAngle, push))
        {
            pendingSteps = 0;
            UI.Affordances.Affordance.EndStop(left);
        }
        else if (!CannonStop.Pressing)
            Haptics.Pulse(left, 0.2f, 0.02f);
    }

    /// <summary>Feeds one hand's twist since last frame into its knob; returns whole steps.</summary>
    private static int Turn(bool left, float stepDegrees, float dt)
    {
        var knob = KnobFor(left);
        ref var state = ref VRInput.Hand(left);
        if (!state.Tracked)
        {
            knob.Reset();
            return 0;
        }
        var rotation = state.Rotation;
        if (!knob.HasPrevious)
        {
            knob.Previous = rotation;
            knob.HasPrevious = true;
            return 0;
        }
        float angle = TwistKnob.TwistAngle(knob.Previous.N(), rotation.N(), TwistAxis);
        knob.Previous = rotation;
        knob.Twist.StepDegrees = stepDegrees;
        return knob.Twist.Update(angle, dt);
    }

    private static Knob KnobFor(bool left) => left ? LeftKnob : RightKnob;

    private static void ResetAll()
    {
        LeftKnob.Reset();
        RightKnob.Reset();
        Pull.Reset();
        PullGrip.Reset();
        pendingSteps = 0;
        stepHold = 0f;
        stepHoldDir = 0;
        SpoolStop.Reset();
        CannonStop.Reset();
    }
}
