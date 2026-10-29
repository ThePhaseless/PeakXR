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
/// While a finger rests on a held spike's trigger and the wall is past range, the wall gets a red ring and the end of
/// the real range a teal one ("Move 0.6 m closer"); a press there, or a cast lost by moving away, is denied with the
/// same cue. Runs in the <c>UpdateClimbingSpikeUse</c> patch, where the camera is still at the aiming hand.
/// A held spike that is not the first one in the slots never places by itself (vanilla <c>climbingSpikeSelected</c>,
/// CharacterItems.cs:102-112, picks the first spike slot, :935-946): aimed at a wall in range, it gets a CantUse
/// cue whose ring marks the belt slot of the spike that places. The slot rule itself stays vanilla. A pocketed spike
/// goes in only at a climbing hand that holds its trigger (<see cref="HandPiton"/>), which has its own cues; a cast of
/// it lost by moving away is denied here too.
/// </summary>
internal static class SpikeCue
{
    /// <summary>How far past the range a wall still gets the "move closer" cue.</summary>
    private const float LookPast = 4f;

    private static bool within = true;

    private static int outOfRangeFrame = -10;

    /// <summary>The aimed wall is out of range this frame or the last (for the aim line's colour). Goes stale on its
    /// own when the spike patch stops running (item changed, no spikes left, not local).</summary>
    public static bool OutOfRange => Time.frameCount - outOfRangeFrame <= 1;

    public static void After(
        Character c,
        (float Tick, ItemSlot Slot) before,
        float tickAfter,
        ItemSlot spikeSlot,
        ClimbingSpikeComponent spike,
        in RaycastHit preview
    )
    {
        // Placing empties the slot and refills it with the next piton (CharacterItems.cs:1086-1094): only the same slot
        // object, still set, means nothing was placed.
        bool slotKept = spikeSlot != null && ReferenceEquals(spikeSlot, before.Slot);
        var cam = MainCamera.instance;
        if (
            c == null
            || !c.IsLocal
            || spike == null
            || cam == null
            || c.data.climbingSpikeCount <= 0
            || !VRSession.Active
        )
            return;
        // Cleared only for the local player: the patch runs for every character, and a remote one's pass cleared the
        // local aim line's red between this frame's local pass and the line's read.
        outOfRangeFrame = -10;
        var d = c.data;
        bool climbing = d.isClimbingAnything;
        var held = d.currentItem;
        bool heldSpike = held != null && held.TryGetComponent<ClimbingSpikeComponent>(out _);
        // The hand the cast aims from (AimPatches.ClimbingSpike): the holding hand, else the climbing hand that holds
        // its trigger for a piton; nothing else sets one.
        if (!heldSpike && !HandPiton.Placing)
            return;
        bool left = heldSpike ? LocalHold.ItemInLeftHand : HandPiton.PlacingLeft;
        var origin = cam.transform.position;
        var dir = cam.transform.forward;

        // A cast that ended with the button still down and the piton not placed (placing clears the slot,
        // CharacterItems.cs:1086-1087): the hand moved out of range.
        var input = c.input;
        if (
            before.Tick > 0f
            && tickAfter == 0f
            && slotKept
            && (input.usePrimaryIsPressed || input.useSecondaryIsPressed)
        )
        {
            float max = climbing
                ? spike.climbingSpikePreviewDisableDistance
                : spike.climbingSpikePreviewDisableDistanceGrounded;
            if (
                Affordance.Deny(
                    left,
                    preview.point,
                    DenyReason.MovedAway,
                    DenyHint.Range(origin, preview.point, max)
                )
            )
                Affordance.Preview(
                    spike.climbingSpikePreviewPrefab,
                    preview.point,
                    Quaternion.LookRotation(
                        preview.normal != Vector3.zero ? -preview.normal : dir,
                        Vector3.up
                    )
                );
            return;
        }
        if (!heldSpike)
        {
            HandPiton.After(c, tickAfter);
            return;
        }
        if (tickAfter > 0f)
        {
            within = true;
            return;
        }

        bool attempt = (input.usePrimaryWasPressed && !climbing) || input.useSecondaryWasPressed;
        ref var hand = ref VRInput.Hand(left);
        // Trying to place: a finger on the held spike's trigger. A fist on the wall squeezing it is no try: that
        // trigger is muted.
        bool trying =
            !PhysicalInteraction.UseMuted(left) && (hand.TriggerTouch || hand.Trigger > 0.05f);
        if (!attempt && !trying)
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
        // The hover cue never says "can't" inside the range (hysteresis only past it); a press the game just
        // refused (it would have started the cast above) is checked against the exact range.
        within = hit && DenyRange.Within(wall.distance, range, within, 0.1f);
        // In range, not climbing, yet no cast (the started one returned above): the held spike's slot is not the one
        // the game places from.
        var selected = c.refs.items.currentSelectedSlot;
        if (
            !climbing
            && hit
            && wall.distance <= range
            && spikeSlot != null
            && selected.IsSome
            && selected.Value != spikeSlot.itemSlotID
        )
        {
            var slotHint = PeakXR.Inventory.BodyInventory.Instance.SlotWorld(
                spikeSlot.itemSlotID,
                out var slotAt
            )
                ? DenyHint.At(slotAt)
                : default;
            if (!attempt)
                Affordance.Hover(left, wall.point, DenyReason.CantUse, slotHint);
            else
                Affordance.Deny(left, wall.point, DenyReason.CantUse, slotHint, held);
            return;
        }
        if (attempt ? hit && wall.distance <= range : within)
            return;
        if (!within)
            outOfRangeFrame = Time.frameCount;
        var end = origin + dir * range;
        var spot = hit && wall.distance > range ? wall.point : end;
        var hint =
            hit && wall.distance > range ? DenyHint.Closer(end, wall.distance - range) : default;
        // The refused piton, red, where the press would have put it (the game's preview faces into the wall,
        // CharacterItems.cs:1067-1068; with no wall, along the aim).
        if (attempt)
        {
            if (Affordance.Deny(left, spot, DenyReason.NoWall, hint))
                Affordance.Preview(
                    spike.climbingSpikePreviewPrefab,
                    spot,
                    Quaternion.LookRotation(dir, Vector3.up)
                );
        }
        else
            Affordance.Hover(left, spot, DenyReason.NoWall, hint);
    }
}
