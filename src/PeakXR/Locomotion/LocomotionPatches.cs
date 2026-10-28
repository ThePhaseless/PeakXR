using System.Reflection;
using HarmonyLib;
using PeakXR.Framework;
using PeakXR.VR;

namespace PeakXR.Locomotion;

/// <summary>Harmony patches of <see cref="LocomotionFeature"/>. All act on the local VR player only.</summary>
internal static class LocomotionPatches
{
    /// <summary>
    /// CharacterInput.Sample(bool) (CharacterInput.cs:212, internal): the game's once-per-frame input sampling,
    /// called from CharacterMovement.Update (CharacterMovement.cs:198) before CameraLook, TryToJump,
    /// SetMovementState and CalculateWorldMovementDir read the fields in the same frame. Normal priority, so it
    /// runs before interact.physical's low-priority postfix, which adds its climb input on top.
    /// </summary>
    [HarmonyPatch]
    internal static class Sample
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterInput), "Sample", new[] { typeof(bool) });

        private static void Postfix(CharacterInput __instance, bool playerMovementActive)
        {
            try
            {
                if (!VRSession.Active)
                    return;
                var c = Character.localCharacter;
                if (c != null && c.input == __instance)
                    LocomotionInput.Apply(c, __instance, playerMovementActive);
            }
            catch (System.Exception e)
            {
                PatchGuard.Fail<LocomotionFeature>(e);
            }
        }
    }

    /// <summary>
    /// CharacterInput.HotbarKeyWasPressed(int) (CharacterInput.cs:186): reads the Hotbar actions directly, so the
    /// backpack slot (Player.cs:47, slot 3) is selected by answering true for that key once after the backpack
    /// press. CharacterItems.DoSwitching (CharacterItems.cs:729) then equips it through vanilla code.
    /// </summary>
    [HarmonyPatch]
    internal static class HotbarKey
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterInput), "HotbarKeyWasPressed", new[] { typeof(int) });

        private static void Postfix(CharacterInput __instance, int key, ref bool __result)
        {
            if (!__result && VRSession.Active && LocomotionInput.IsBackpackSlot(key))
                __result = LocomotionInput.ConsumeBackpack(__instance);
        }
    }
}
