using System.Reflection;
using HarmonyLib;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.VR;

namespace PeakXR.Voice;

/// <summary>Merges VR push-to-talk into the game's own flag after it samples input, so the voice
/// transmit gate (CharacterVoiceHandler.cs:250) and the mouth mute (AnimatedMouth.cs:136) both follow it.
/// A/CharacterInput.cs:212 <c>internal void Sample(bool playerMovementActive)</c>; push-to-talk is read
/// at L259 only while movement is active. Called for the local character only (CharacterMovement.cs:198).</summary>
[HarmonyPatch]
internal static class PushToTalkSamplePatch
{
    private static bool forced;

    private static MethodBase TargetMethod() =>
        GameMember.Method(typeof(CharacterInput), "Sample");

    private static void Postfix(CharacterInput __instance, bool playerMovementActive)
    {
        var held = VRSession.Active && VoiceInput.Held;
        if (!held && !forced)
            return;
        __instance.pushToTalkPressed = VoicePushToTalk.Merge(
            __instance.pushToTalkPressed,
            held,
            playerMovementActive,
            ref forced
        );
    }
}
