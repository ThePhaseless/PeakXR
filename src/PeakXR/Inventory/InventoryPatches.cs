using System.Reflection;
using HarmonyLib;
using PeakXR.Framework;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Inventory;

/// <summary>Harmony patches of <see cref="InventoryFeature"/>. All act on the local VR player only.</summary>
internal static class InventoryPatches
{
    /// <summary>CharacterInput.Sample(bool) (CharacterInput.cs:212, internal): runs after <c>interact.physical</c>'s
    /// postfix (priority Low), whose grip edges call <see cref="BodyInventory"/> first, so the shoulder grip's held
    /// interact lands in the same frame the wheel opens.</summary>
    [HarmonyPatch]
    internal static class Sample
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterInput), "Sample", new[] { typeof(bool) });

        [HarmonyPriority(Priority.VeryLow)]
        private static void Postfix(CharacterInput __instance)
        {
            try
            {
                var c = Character.localCharacter;
                if (c == null || c.input != __instance || !VRSession.Active)
                    return;
                BodyInventory.Instance.Tick(c, __instance, Time.deltaTime);
                SlotGhosts.Ensure();
                HotbarHider.Sync(SlotGhosts.Showing);
            }
            catch (System.Exception e)
            {
                PatchGuard.Fail<InventoryFeature>(e);
            }
        }
    }

    /// <summary>CharacterInput.SelectSlotWasPressed(int) (CharacterInput.cs:176): read by
    /// <c>CharacterItems.DoSwitching</c> (CharacterItems.cs:794) for keys 0-3 after its gates pass. A body-slot draw or
    /// holster reports its key as pressed once, so the switch runs through vanilla <c>EquipSlot</c> and its RPCs.</summary>
    [HarmonyPatch]
    internal static class SelectSlot
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(
                typeof(CharacterInput),
                "SelectSlotWasPressed",
                new[] { typeof(int) }
            );

        private static void Postfix(CharacterInput __instance, int key, ref bool __result)
        {
            if (!__result && VRSession.Active)
                __result = BodyInventory.Instance.ConsumeKey(__instance, key);
        }
    }
}
