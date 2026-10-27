using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Inventory;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Interact;

/// <summary>
/// Starting a wall climb, a rope, a vine or a carry puts the held item away for everyone: <c>EquipSlot(None)</c>
/// (CharacterClimbing.cs:569, RopeSegment.cs:42, JungleVine.cs:113, CharacterCarrying.cs:51). An item that cannot be
/// pocketed, or one in the overflow slot, is dropped instead (CharacterItems.cs:498). Keeping it in the other hand
/// would differ from what vanilla peers see, and a climb grip is never held back (a falling player must catch the
/// wall), so VR makes it predictable instead: while the free hand is in grip range of a climbable wall and the held
/// item would drop, the item gets a red ring and "Climbing drops &lt;item&gt;"; once it fell, a teal ring marks it
/// on the ground for <see cref="MarkSeconds"/>; an item that went to the belt gets a teal line to its slot. Works from
/// the state change alone, so any path into a climb or carry is covered.
/// </summary>
internal static class ClimbStow
{
    private const float MarkSeconds = 5f;
    private const float ResolveSeconds = 1f;
    private const float FindSeconds = 1.5f;

    // The held item as of last frame.
    private static Item held;
    private static string wouldDropText,
        droppedText;
    private static bool heldDrops,
        heldLeft,
        heldSlotSome;
    private static byte heldSlot;
    private static Vector3 heldPos;
    private static bool wasBusy;

    // A climb or carry just took it: resolved once the hand is empty.
    private static float pendingUntil = -10f;
    private static bool pendingDrops,
        pendingLeft,
        pendingSlotSome;
    private static byte pendingSlot;
    private static ushort pendingId;
    private static Vector3 pendingPos;
    private static string pendingText;

    // The dropped item, looked for, then marked.
    private static float findUntil = -10f;
    private static Item marked;
    private static float markUntil = -10f;

    public static void Tick(Character c, VRRig rig, InteractConfig cfg)
    {
        var d = c.data;
        float now = Time.time;
        bool busy = d.isClimbing || d.isRopeClimbing || d.isVineClimbing || d.carriedPlayer != null;
        if (busy && !wasBusy && held != null)
        {
            pendingUntil = now + ResolveSeconds;
            pendingDrops = heldDrops;
            pendingLeft = heldLeft;
            pendingSlotSome = heldSlotSome;
            pendingSlot = heldSlot;
            pendingId = held.itemID;
            pendingPos = heldPos;
            pendingText = droppedText;
        }
        wasBusy = busy;
        if (pendingUntil > now && d.currentItem == null)
        {
            pendingUntil = -10f;
            Resolve(rig, now);
        }

        Snapshot(c, d.currentItem);
        if (held != null && heldDrops && !busy)
            WarnNearWall(c, rig, cfg);
        FindDropped(now);
        if (marked != null)
            MarkDropped(now);
    }

    private static void Resolve(VRRig rig, float now)
    {
        if (pendingDrops)
        {
            findUntil = now + FindSeconds;
            marked = null;
            return;
        }
        var hand = rig.Hand(pendingLeft);
        // From the hand that held it to its slot: a teal line and ring show where it went.
        if (
            pendingSlotSome
            && hand != null
            && BodyInventory.Instance.SlotWorld(pendingSlot, out var slot)
        )
            Affordance.Inform(pendingLeft, hand.position, DenyReason.Stowed, DenyHint.At(slot));
    }

    private static void Snapshot(Character c, Item item)
    {
        if (item != held)
        {
            held = item;
            if (item != null)
            {
                // Once per held item: the label strings allocate.
                var name = item.GetItemName();
                wouldDropText = "Climbing drops " + name;
                droppedText = name + " fell here";
            }
        }
        if (item == null)
            return;
        var selected = c.refs.items.currentSelectedSlot;
        heldSlotSome = selected.IsSome;
        heldSlot = selected.IsSome ? selected.Value : (byte)0;
        heldDrops = PutAwayGuard.PutAwayDrops(c, item);
        heldLeft = LocalHold.ItemInLeftHand;
        heldPos = item.Center();
    }

    /// <summary>The free hand is where a grab would start a climb: the same probe and verdict as the grip and its
    /// chalk ring (<see cref="PhysicalInteraction.WouldCatch"/>).</summary>
    private static void WarnNearWall(Character c, VRRig rig, InteractConfig cfg)
    {
        if (
            !cfg.PhysicalClimb
            || !VRInput.Hand(!heldLeft).Tracked
            || !PhysicalInteraction.WouldCatch(c, !heldLeft)
        )
            return;
        Affordance.Hover(
            !heldLeft,
            held.Center(),
            DenyReason.ItemWouldDrop,
            default,
            wouldDropText
        );
    }

    private static void FindDropped(float now)
    {
        if (findUntil < now || marked != null)
            return;
        foreach (var item in Item.ALL_ITEMS)
        {
            if (
                item != null
                && item.itemID == pendingId
                && item.itemState == ItemState.Ground
                && (item.Center() - pendingPos).sqrMagnitude < 4f
            )
            {
                marked = item;
                markUntil = now + MarkSeconds;
                findUntil = -10f;
                return;
            }
        }
    }

    /// <summary>A teal ring on the fallen item, with its name, until it is picked up or the time runs out.</summary>
    private static void MarkDropped(float now)
    {
        if (marked == null || marked.itemState != ItemState.Ground || now > markUntil)
        {
            marked = null;
            return;
        }
        Affordance.Mark(pendingLeft, marked.Center(), DenyReason.Dropped, default, pendingText);
    }
}
