using PeakXR.Core;
using PeakXR.Inventory;
using PeakXR.UI.Affordances;
using UnityEngine;

namespace PeakXR.Interact;

/// <summary>
/// Starting a wall climb, a rope or a vine puts the held item away for everyone: <c>EquipSlot(None)</c>
/// (CharacterClimbing.cs:569, RopeSegment.cs:42, JungleVine.cs:113). An item that cannot be pocketed, or one in the
/// overflow slot, is dropped instead (CharacterItems.cs:498). Keeping it in the other hand would differ from what
/// vanilla peers see, so VR makes it predictable instead: a grip that would drop the item is refused once with
/// "Would drop your item / Grip again to climb" (a second grip within <see cref="ConfirmSeconds"/> climbs), and an
/// item that goes to the belt gets a teal ring on its belt slot with "Stowed on your belt".
/// </summary>
internal static class ClimbStow
{
    private const float ConfirmSeconds = 3f;
    private const float WatchSeconds = 1f;

    private static float confirmUntil = -10f;
    private static Item confirmItem;
    private static float watchUntil = -10f;
    private static byte watchSlot;
    private static bool watchLeft;

    /// <summary>
    /// Before a climb, rope or vine grab by the <paramref name="left"/> hand. False: hold off, the item would drop
    /// and the player has not confirmed yet.
    /// </summary>
    public static bool Allow(Character c, bool left)
    {
        var item = c.data.currentItem;
        if (item == null || !Affordance.Enabled)
            return true;
        var selected = c.refs.items.currentSelectedSlot;
        bool drops =
            !item.UIData.canPocket
            || (selected.IsSome && selected.Value == 250 && !c.player.GetItemSlot(250).IsEmpty());
        float now = Time.time;
        if (!drops)
        {
            if (selected.IsSome)
            {
                watchSlot = selected.Value;
                watchUntil = now + WatchSeconds;
                watchLeft = LocalHoldLeft();
            }
            return true;
        }
        if (now < confirmUntil && confirmItem == item)
        {
            confirmUntil = -10f;
            return true;
        }
        confirmUntil = now + ConfirmSeconds;
        confirmItem = item;
        Affordance.Deny(LocalHoldLeft(), item.Center(), DenyReason.ItemWouldDrop);
        return false;
    }

    /// <summary>Per frame: once the climb took the item, show where it went.</summary>
    public static void Tick(Character c)
    {
        if (watchUntil < 0f)
            return;
        if (Time.time > watchUntil)
        {
            watchUntil = -10f;
            return;
        }
        var d = c.data;
        if (d.currentItem != null || !(d.isClimbing || d.isRopeClimbing || d.isVineClimbing))
            return;
        watchUntil = -10f;
        var rig = VR.VRRig.Instance;
        var hand = rig != null ? rig.Hand(watchLeft) : null;
        // From the hand that held it to its slot: a teal line and ring show where it went.
        if (hand != null && BodyInventory.Instance.SlotWorld(watchSlot, out var slot))
            Affordance.Inform(watchLeft, hand.position, DenyReason.Stowed, DenyHint.At(slot));
    }

    private static bool LocalHoldLeft() => Avatar.LocalHold.ItemInLeftHand;
}
