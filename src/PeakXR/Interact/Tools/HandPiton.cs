using PeakXR.Core;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Interact.Tools;

/// <summary>
/// A piton from the pockets while climbing goes in at a hand on the climb. That hand's trigger, pulled once the hand
/// has settled on the rock and held for <see cref="UseTrigger.HoldSeconds"/> (<see cref="UseTrigger.Piton"/>), drives
/// the game's own piton cast (CharacterItems.UpdateClimbingSpikeUse, CharacterItems.cs:981-1027, the secondary use
/// while climbing at :1019-1025) for that call only, aimed on a wall from <see cref="Lift"/> outside it at the hand
/// straight into it, on a rope or vine along that hand's aim. Vanilla timing, preview ghost and placement RPC apply,
/// and letting go before the cast ends cancels it as vanilla does. A fist closing with its trigger squeezed sets
/// nothing, and the free hand's trigger no longer sets a piton: the cast sees only this hold as its secondary use. One
/// piton per hold. That is two-handed play; one-handed, the game's own press (the stick chord, or a keyboard or
/// gamepad) sets it, aimed the same way from the one hand (#22).
/// </summary>
internal static class HandPiton
{
    /// <summary>Metres out from the hand along the wall's normal where the cast's ray starts: outside the rock however
    /// the hand sits on it, well inside the game's 1 m climbing reach (<c>climbingSpikeStartDistance</c>).</summary>
    public const float Lift = 0.3f;

    /// <summary>How far from <see cref="Lift"/> the cue looks for the rock: the game's climbing reach.</summary>
    private const float Reach = 1f;

    // This frame's hold, from PhysicalInteraction's use pass (CharacterInput.Sample, before CharacterItems.Update).
    private static bool placing;

    /// <summary>A hand on the climb gives the cast its aim this frame (<see cref="origin"/>, <see cref="normal"/>).</summary>
    private static bool aiming;
    private static bool startFrame;

    /// <summary>The frame <see cref="Update"/> last ran. Its hold and aim hold for that frame and the next: Update stops
    /// while VR is off or input is inactive, and a stale aim swapped the camera to an old hand pose (#22).</summary>
    private static int frame = -1;

    // CharacterMovement.Update (which samples input) and CharacterItems.Update share the default execution order, so
    // Unity may run the items first; the previous frame's aim still counts then.
    private static bool Fresh => frame >= Time.frameCount - 1 && VRSession.Active;
    private static bool left;
    private static Vector3 origin;
    private static Vector3 normal;
    private static Vector3 point;

    // This hold: its piton went in or was refused (no more casts until the trigger opens), a cast ran.
    private static bool holdDone;
    private static bool castSeen;
    private static int pitonsAtStart;

    // The input the cast saw before Begin, put back by End.
    private static bool inputSwapped;
    private static Character swapped;
    private static bool savedIsPressed;
    private static bool savedWasPressed;

    /// <summary>A hand on the wall holds its trigger for a piton this frame and the cast is driven from it.</summary>
    internal static bool Placing => placing && Fresh;

    internal static bool PlacingLeft => left;

    /// <summary>Where the holding hand meets the wall (the last hold's), or the hand when no rock was found.</summary>
    internal static Vector3 WallPoint => point;

    /// <summary>Pitons this path has placed, for the self-test.</summary>
    internal static int Placed { get; private set; }

    /// <summary>This path drives the local climber's piton casts: VR, two-handed.</summary>
    internal static bool On(Character c) =>
        VRSession.Active && c != null && c.IsLocal && !VRInput.OneHanded;

    /// <summary>
    /// Once a frame after the hands' use triggers updated: picks the hand whose deliberate trigger hold is on a wall
    /// grip (<paramref name="leftWall"/>, <paramref name="rightWall"/>), where it meets the rock, and whether the hold
    /// has lasted long enough to place. A placement felt as a tick; a hold released mid-cast as a cancel.
    /// </summary>
    public static void Update(
        Character c,
        VRRig rig,
        UseTrigger leftUse,
        bool leftWall,
        UseTrigger rightUse,
        bool rightWall
    )
    {
        // A hold carries over only from the frame before: one that ran while Update was off is a new hold.
        bool was = placing && frame == Time.frameCount - 1;
        placing = startFrame = aiming = false;
        frame = Time.frameCount;
        if (was && !holdDone && c != null && c.data.climbingSpikeCount < pitonsAtStart)
        {
            holdDone = true;
            Placed++;
            Haptics.Pulse(left, 0.8f, 0.06f);
        }
        bool trigger = On(c);
        // The trigger path takes the hand that holds its trigger; the game's own press (one-handed play's stick chord)
        // the one hand.
        bool l = trigger
            ? ((left ? leftUse : rightUse).ClimbHold ? left : leftUse.ClimbHold)
            : VRInput.LeftHanded;
        var use = l ? leftUse : rightUse;
        var hand = rig != null ? rig.Hand(l) : null;
        if (
            !VRSession.Active
            || c == null
            || !c.IsLocal
            || !c.data.isClimbingAnything
            || (trigger && !use.ClimbHold)
            || !(l ? leftWall : rightWall)
            || hand == null
        )
        {
            if (was && castSeen && !holdDone)
                Affordance.Cancel(left);
            holdDone = castSeen = false;
            return;
        }
        left = l;
        // On a wall the cast comes in along the climb's normal; on a rope or vine, along the hand's own aim (#22).
        var aim = rig.Aim(l);
        bool wall = c.data.isClimbing && c.data.climbNormal.sqrMagnitude > 1e-4f;
        normal =
            wall ? c.data.climbNormal.normalized
            : aim != null ? -aim.forward
            : -hand.forward;
        origin = wall ? hand.position + normal * Lift : hand.position;
        point = Physics.Raycast(
            origin,
            -normal,
            out var hit,
            Reach,
            HelperFunctions.terrainMapMask,
            QueryTriggerInteraction.Ignore
        )
            ? hit.point
            : hand.position;
        aiming = true;
        if (!trigger || !use.Piton || holdDone)
            return;
        if (c.data.climbingSpikeCount <= 0)
        {
            // An item in the other hand got UseOtherHand at the press (PhysicalInteraction): that hand runs it.
            if (c.data.currentItem == null)
                Affordance.DenyWithText(left, point, DenyReason.UsedUp, "No pitons");
            holdDone = true;
            return;
        }
        placing = true;
        startFrame = !was;
        if (startFrame)
        {
            pitonsAtStart = c.data.climbingSpikeCount;
            castSeen = false;
        }
    }

    /// <summary>
    /// Prefix side of one <c>UpdateClimbingSpikeUse</c> call for the local climber with no piton in hand: the cast's
    /// secondary use becomes the placing hand's hold (nothing else, so the free hand's trigger sets no piton), and the
    /// camera moves to <see cref="Lift"/> outside the wall at that hand, looking into it (CharacterInput.cs:107-109 are
    /// the fields the cast reads). Nothing when this path is off; <see cref="End"/> runs in the finalizer either way.
    /// </summary>
    public static CameraSwap Begin(Character c)
    {
        if (c == null || !c.data.isClimbingAnything)
            return default;
        if (!On(c))
            // The game's own press (one-handed play's chord, a keyboard or gamepad) is aimed from
            // the hand on the climb too, not the head (#22).
            return aiming && Fresh ? Swap() : default;
        bool now = placing && Fresh;
        var input = c.input;
        inputSwapped = true;
        swapped = c;
        savedIsPressed = input.useSecondaryIsPressed;
        savedWasPressed = input.useSecondaryWasPressed;
        input.useSecondaryIsPressed = now;
        input.useSecondaryWasPressed = now && startFrame;
        return now ? Swap() : default;
    }

    private static CameraSwap Swap() =>
        CameraSwap.Begin(
            origin,
            Quaternion.LookRotation(
                -normal,
                Mathf.Abs(normal.y) > 0.99f ? Vector3.forward : Vector3.up
            )
        );

    /// <summary>Finalizer side: the cast's input back to what the player pressed.</summary>
    public static void End()
    {
        if (!inputSwapped)
            return;
        inputSwapped = false;
        if (swapped != null)
        {
            var input = swapped.input;
            input.useSecondaryIsPressed = savedIsPressed;
            input.useSecondaryWasPressed = savedWasPressed;
        }
        swapped = null;
    }

    /// <summary>
    /// After the cast ran (SpikeCue, camera and input still swapped): the fill and its haptic ramp while it runs, and
    /// a press whose ray found no rock the game takes a piton in (RaycastClimbingSpikeStart, CharacterItems.cs:1072-1076,
    /// TerrainMap only) refused as NoWall at the hand. Placement and a cast lost by moving away are told elsewhere.
    /// </summary>
    public static void After(Character c, float tickAfter)
    {
        if (!Placing)
            return;
        if (tickAfter > 0f)
        {
            castSeen = true;
            Affordance.Progress(left, c.refs.items.climbingSpikeCastProgress);
            return;
        }
        if (castSeen)
            return;
        holdDone = true;
        Affordance.Deny(left, point, DenyReason.NoWall);
    }
}
