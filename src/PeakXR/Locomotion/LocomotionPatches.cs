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
    /// Character.AddForce(Vector3, float, float) (Character.cs:1396, internal): the game's whole-body push, used by
    /// AOE blasts, waterfalls, tornadoes, move zones, launches, knockback collisions, Bing Bong and lava
    /// (AOE.cs:172, WaterfallPusher.cs:32, Tornado.cs:301, PlayerMoveZone.cs:44, CollisionModifier.cs:79). Marks an
    /// outside force on the local body, which the stance walk never walks back. A force of no size moves nothing.
    /// </summary>
    [HarmonyPatch]
    internal static class Push
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(
                typeof(Character),
                "AddForce",
                new[] { typeof(UnityEngine.Vector3), typeof(float), typeof(float) }
            );

        private static void Prefix(Character __instance, UnityEngine.Vector3 move)
        {
            if (
                __instance == Character.localCharacter
                && UnityEngine.Vector3.SqrMagnitude(move) > 1e-6f
            )
                LocomotionInput.OutsideForceAt = UnityEngine.Time.time;
        }
    }

    /// <summary>
    /// Character.RPCA_AddForceAtPosition(Vector3, Vector3, float) (Character.cs:458, [PunRPC]): impulses from wind
    /// (WindChillZone.cs:250), mobs, bonks, arrows, axes and rollers (Beetle.cs:27, ArrowShooter.cs:128,
    /// SwingingAxe.cs:161). Marks an outside force on the local body, as <see cref="Push"/>. A storm sends this every
    /// physics step in bounds, with zero force in shelter (WindChillZone.cs:175, :240-250): that is no force (#140).
    /// </summary>
    [HarmonyPatch]
    internal static class Impulse
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(
                typeof(Character),
                nameof(Character.RPCA_AddForceAtPosition),
                new[] { typeof(UnityEngine.Vector3), typeof(UnityEngine.Vector3), typeof(float) }
            );

        private static void Prefix(Character __instance, UnityEngine.Vector3 force)
        {
            if (
                __instance == Character.localCharacter
                && UnityEngine.Vector3.SqrMagnitude(force) > 1e-6f
            )
                LocomotionInput.OutsideForceAt = UnityEngine.Time.time;
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
