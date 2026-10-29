using PeakXR.Core;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Interact.Tools;

/// <summary>
/// A piton from the pockets while wall climbing goes in at a hand on the wall. That hand's trigger, pulled once the hand
/// has settled on the rock and held for <see cref="UseTrigger.HoldSeconds"/> (<see cref="UseTrigger.Piton"/>), drives
/// the game's own piton cast (CharacterItems.UpdateClimbingSpikeUse, CharacterItems.cs:981-1027, the secondary use
/// while climbing at :1019-1025) for that call only, aimed from <see cref="Lift"/> outside the wall at the hand straight
/// into it. Vanilla timing, preview ghost and placement RPC apply, and letting go before the cast ends cancels it as
/// vanilla does. A fist closing with its trigger squeezed sets nothing, and the free hand's trigger no longer sets a
/// piton: the cast sees only this hold as its secondary use. One piton per hold. Two-handed play with
/// <c>Interaction.TriggerUse</c> on; otherwise the game's own aim from the head.
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
    private static bool startFrame;
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
    internal static bool Placing => placing;

    internal static bool PlacingLeft => left;

    /// <summary>Where the holding hand meets the wall (the last hold's), or the hand when no rock was found.</summary>
    internal static Vector3 WallPoint => point;

    /// <summary>Pitons this path has placed, for the self-test.</summary>
    internal static int Placed { get; private set; }

    /// <summary>This path drives the local climber's piton casts: VR, two-handed, triggers as use buttons.</summary>
    internal static bool On(Character c) =>
        VRSession.Active
        && c != null
        && c.IsLocal
        && !VRInput.OneHanded
        && InteractConfig.Instance.TriggerUse;

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
        bool was = placing;
        placing = startFrame = false;
        if (was && !holdDone && c != null && c.data.climbingSpikeCount < pitonsAtStart)
        {
            holdDone = true;
            Placed++;
            Haptics.Pulse(left, 0.8f, 0.06f);
        }
        bool l = (left ? leftUse : rightUse).ClimbHold ? left : leftUse.ClimbHold;
        var use = l ? leftUse : rightUse;
        var hand = rig != null ? rig.Hand(l) : null;
        if (
            !On(c)
            || !c.data.isClimbing
            || !use.ClimbHold
            || !(l ? leftWall : rightWall)
            || hand == null
            || c.data.climbNormal.sqrMagnitude < 1e-4f
        )
        {
            if (was && castSeen && !holdDone)
                Affordance.Cancel(left);
            holdDone = castSeen = false;
            return;
        }
        left = l;
        normal = c.data.climbNormal.normalized;
        origin = hand.position + normal * Lift;
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
        if (!use.Piton || holdDone)
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
        if (!On(c) || !c.data.isClimbingAnything)
            return default;
        var input = c.input;
        inputSwapped = true;
        swapped = c;
        savedIsPressed = input.useSecondaryIsPressed;
        savedWasPressed = input.useSecondaryWasPressed;
        input.useSecondaryIsPressed = placing;
        input.useSecondaryWasPressed = startFrame;
        return placing
            ? CameraSwap.Begin(
                origin,
                Quaternion.LookRotation(
                    -normal,
                    Mathf.Abs(normal.y) > 0.99f ? Vector3.forward : Vector3.up
                )
            )
            : default;
    }

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
        if (!placing)
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
