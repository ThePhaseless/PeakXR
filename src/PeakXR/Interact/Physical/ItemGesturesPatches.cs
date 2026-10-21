using System;
using System.Reflection;
using HarmonyLib;
using PeakXR.Framework;

namespace PeakXR.Interact.Physical;

/// <summary>Harmony patches of <see cref="ItemGesturesFeature"/>. All act on the local VR player only.</summary>
internal static class ItemGesturesPatches
{
    /// <summary>CharacterInput.Sample(bool) (CharacterInput.cs:212, internal): the game's once-per-frame input
    /// sampling for the local character. Runs before interact.physical's postfix (Priority.Low), so the gesture
    /// requests it makes are injected by that postfix in the same frame.</summary>
    [HarmonyPatch]
    internal static class Sample
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterInput), "Sample", new[] { typeof(bool) });

        [HarmonyPriority(Priority.High)]
        private static void Postfix(CharacterInput __instance, bool playerMovementActive)
        {
            var c = Character.localCharacter;
            if (c != null && c.input == __instance)
                ItemGestures.Tick(c, __instance, playerMovementActive);
        }
    }

    /// <summary>BugleSFX.UpdateTooting (BugleSFX.cs:43, private): sends RPC_StartToot with a pitch from the holder's
    /// lookDirection (BugleSFX.cs:59-61). The look is swapped to the bell for the call and restored in a finalizer.</summary>
    [HarmonyPatch]
    internal static class Tooting
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(BugleSFX), "UpdateTooting");

        private static void Prefix(BugleSFX __instance) => HeldGestures.BeforeTooting(__instance);

        private static Exception Finalizer(Exception __exception)
        {
            HeldGestures.AfterTooting();
            return __exception;
        }
    }

    /// <summary>Interaction.DoInteraction (Interaction.cs:99, private): adds the frame time to
    /// currentInteractableHeldTime and finishes the cast at currentConstantInteractableTime (Interaction.cs:136-139).
    /// The prefix caps a lifted lid's held time first.</summary>
    [HarmonyPatch]
    internal static class DoInteraction
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(Interaction), "DoInteraction");

        private static void Prefix(Interaction __instance) =>
            LidGesture.BeforeInteraction(__instance);
    }
}
