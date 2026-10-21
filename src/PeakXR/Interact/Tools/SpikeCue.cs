using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Interact.Tools;

/// <summary>
/// Piton range, made visible. <c>CharacterItems.RaycastClimbingSpikeStart</c> (CharacterItems.cs:1072-1076) only
/// starts a cast on terrain within <c>climbingSpikeStartDistance</c> (climbing) or <c>...Grounded</c>, and
/// <c>WithinClimbingSpikePreviewRange</c> (:971-979) cancels it once the aim moves past the preview disable
/// distance. Both aim from the hand in VR (interact.aim), so the hand suggests a reach the game does not have.
/// While a finger rests on the aiming trigger and the wall is past range, the wall gets a red ring and the end of the
/// real range a teal one ("Move 0.6 m closer"); a press there, or a cast lost by moving away, is denied with the same
/// cue. Runs in the <c>UpdateClimbingSpikeUse</c> patch, where the camera is still at the aiming hand.
/// </summary>
internal static class SpikeCue
{
    /// <summary>How far past the range a wall still gets the "move closer" cue.</summary>
    private const float LookPast = 4f;

    private static bool within = true;

    /// <summary>The aimed wall is out of range this frame (for the aim line's colour).</summary>
    public static bool OutOfRange { get; private set; }

    public static void After(
        Character c,
        float tickBefore,
        float tickAfter,
        bool slotKept,
        ClimbingSpikeComponent spike,
        in RaycastHit preview
    )
    {
        OutOfRange = false;
        var cam = MainCamera.instance;
        if (
            c == null
            || !c.IsLocal
            || spike == null
            || cam == null
            || c.data.climbingSpikeCount <= 0
            || !Affordance.Enabled
        )
            return;
        var d = c.data;
        bool climbing = d.isClimbingAnything;
        var held = d.currentItem;
        bool heldSpike = held != null && held.TryGetComponent<ClimbingSpikeComponent>(out _);
        // The hand interact.aim aims from (AimPatches.ClimbingSpike): the holding hand, else the secondary-use hand.
        bool left = heldSpike
            ? LocalHold.ItemInLeftHand
            : held == null || !LocalHold.ItemInLeftHand;
        var origin = cam.transform.position;
        var dir = cam.transform.forward;

        // A cast that ended with the button still down and the piton not placed (placing clears the slot,
        // CharacterItems.cs:1086-1087): the hand moved out of range.
        var input = c.input;
        if (
            tickBefore > 0f
            && tickAfter == 0f
            && slotKept
            && (input.usePrimaryIsPressed || input.useSecondaryIsPressed)
        )
        {
            float max = climbing
                ? spike.climbingSpikePreviewDisableDistance
                : spike.climbingSpikePreviewDisableDistanceGrounded;
            Affordance.Deny(
                left,
                preview.point,
                DenyReason.MovedAway,
                DenyHint.Range(origin, preview.point, max)
            );
            return;
        }
        if (tickAfter > 0f)
        {
            within = true;
            return;
        }

        bool attempt =
            (input.usePrimaryWasPressed && !climbing && heldSpike)
            || (input.useSecondaryWasPressed && (heldSpike || climbing));
        ref var hand = ref VRInput.Hand(left);
        if (!attempt && !hand.TriggerTouch && hand.Trigger <= 0.05f)
        {
            within = true;
            return;
        }
        float range = climbing
            ? spike.climbingSpikeStartDistance
            : spike.climbingSpikeStartDistanceGrounded;
        bool hit = Physics.Raycast(
            origin,
            dir,
            out var wall,
            range + LookPast,
            HelperFunctions.GetMask(HelperFunctions.LayerType.TerrainMap)
        );
        // The hover cue has hysteresis so it does not flicker at the edge; a press the game just refused (it would
        // have started the cast above) is denied whatever the cue showed.
        within = hit && DenyRange.Within(wall.distance, range, within, 0.1f);
        if (attempt ? hit && wall.distance <= range : within)
            return;
        OutOfRange = !within;
        var end = origin + dir * range;
        var spot = hit && wall.distance > range ? wall.point : end;
        var hint =
            hit && wall.distance > range ? DenyHint.Closer(end, wall.distance - range) : default;
        if (attempt)
            Affordance.Deny(left, spot, DenyReason.NoWall, hint);
        else
            Affordance.Hover(left, spot, DenyReason.NoWall, hint);
    }
}
