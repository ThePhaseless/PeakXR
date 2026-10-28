using System.Reflection;
using HarmonyLib;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.VR;
using UnityEngine;
using Zorro.Core;

namespace PeakXR.Interact;

/// <summary>
/// Watches the local VR player's put-aways that drop the held item (a climb, rope, vine or carry start, ClimbStow), so
/// PeakXR's own equips (a hand grab's pickup, a belt draw) wait for the put-away's delayed <c>EquipSlotRpc</c> instead of
/// being stashed by it (<see cref="PutAwayWindow"/>). Both patches only observe: the game runs exactly as before, and
/// flat players are never touched.
/// </summary>
internal static class PutAwayGuard
{
    private static readonly PutAwayWindow Window = new();

    /// <summary>An equip now would be undone by a pending put-away: hold it back for a frame or two.</summary>
    public static bool Pending => VRSession.Active && Window.Pending(Time.realtimeSinceStartup);

    /// <summary>Putting <paramref name="item"/> away drops it instead of pocketing it: EquipSlot's drop test
    /// (CharacterItems.cs:498), an unpocketable item or one held from the backpack slot (250) while that slot is
    /// full.</summary>
    internal static bool PutAwayDrops(Character c, Item item)
    {
        var selected = c.refs.items.currentSelectedSlot;
        return !item.UIData.canPocket
            || (
                selected.IsSome
                && selected.Value == 250
                && c.player != null
                && !c.player.GetItemSlot(250).IsEmpty()
            );
    }

    /// <summary>CharacterItems.EquipSlot(Optionable&lt;byte&gt;) (CharacterItems.cs:482): a put-away that drops the held
    /// item waits for frames before its RPC (:484-505).</summary>
    [HarmonyPatch]
    internal static class EquipSlot
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(
                typeof(CharacterItems),
                "EquipSlot",
                new[] { typeof(Optionable<byte>) }
            );

        private static void Prefix(CharacterItems __instance, Optionable<byte> slotID)
        {
            var c = Character.localCharacter;
            if (!VRSession.Active || c == null || !ReferenceEquals(c.refs.items, __instance))
                return;
            // A copy: Optionable's members are not readonly, and the analyzer reads a call on the parameter as a write.
            var target = slotID;
            var item = c.data.currentItem;
            if (item == null)
                return;
            // EquipSlot's own early return for the rocket slot (:484) and its drop test (:498).
            if (
                target.IsSome
                && target.Value == 3
                && (c.refs.movement.rocketActive || c.refs.movement.rocketLit)
            )
                return;
            if (PutAwayDrops(c, item))
                Window.Begin(
                    Time.realtimeSinceStartup,
                    target.IsSome ? target.Value : PutAwayWindow.NoSlot
                );
        }
    }

    /// <summary>CharacterItems.EquipSlotRpc(int, int) (CharacterItems.cs:546): the put-away's RPC has landed.</summary>
    [HarmonyPatch]
    internal static class EquipSlotRpc
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(
                typeof(CharacterItems),
                "EquipSlotRpc",
                new[] { typeof(int), typeof(int) }
            );

        private static void Postfix(CharacterItems __instance, int slotID)
        {
            var c = Character.localCharacter;
            if (c != null && ReferenceEquals(c.refs.items, __instance))
                Window.Landed(slotID);
        }
    }
}
