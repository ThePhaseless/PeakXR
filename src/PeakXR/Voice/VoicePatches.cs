using System.Reflection;
using HarmonyLib;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.VR;
using Photon.Voice.Unity;

namespace PeakXR.Voice;

/// <summary>Merges VR push-to-talk into the game's own flag after it samples input, so the voice
/// transmit gate (CharacterVoiceHandler.cs:250) and the mouth mute (AnimatedMouth.cs:136) both follow it.
/// A/CharacterInput.cs:212 <c>internal void Sample(bool playerMovementActive)</c>; push-to-talk is read
/// at L259 only while movement is active. Called for the local character only (CharacterMovement.cs:198).
/// Low priority: runs after player.locomotion's postfix, which writes the binding-table press for this frame.</summary>
[HarmonyPatch]
internal static class PushToTalkSamplePatch
{
    private static bool forced;

    private static MethodBase TargetMethod() =>
        GameMember.Method(typeof(CharacterInput), "Sample", new[] { typeof(bool) });

    [HarmonyPriority(Priority.Low)]
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

/// <summary>
/// The palm-over-mouth mute where the game's key cannot mute (Voice Activation): after
/// <c>CharacterVoiceHandler.PushToTalk</c> (CharacterVoiceHandler.cs:248, private; called from Update :222) decided to
/// send, the local character's recorder is held shut. It clears <c>m_currentlyTransmitting</c> (:56) too, so the
/// first frame after the palm leaves sees a change and vanilla opens the recorder again (:251-255). Local only: no
/// setting is written and nothing new goes over the network.
/// </summary>
[HarmonyPatch]
internal static class MicGatePatch
{
    internal static AccessTools.FieldRef<CharacterVoiceHandler, Character> Owner;
    internal static AccessTools.FieldRef<CharacterVoiceHandler, Recorder> Recorder;
    internal static AccessTools.FieldRef<CharacterVoiceHandler, bool> Transmitting;

    private static MethodBase TargetMethod() =>
        GameMember.Method(typeof(CharacterVoiceHandler), "PushToTalk");

    internal static void Resolve()
    {
        Owner = GameMember.Field<CharacterVoiceHandler, Character>("m_character");
        Recorder = GameMember.Field<CharacterVoiceHandler, Recorder>("m_Recorder");
        Transmitting = GameMember.Field<CharacterVoiceHandler, bool>("m_currentlyTransmitting");
    }

    private static void Postfix(CharacterVoiceHandler __instance)
    {
        if (!VRSession.Active || !VoiceInput.Gate)
            return;
        var owner = Owner(__instance);
        if (owner == null || !owner.IsLocal)
            return;
        var recorder = Recorder(__instance);
        if (recorder != null)
            recorder.TransmitEnabled = false;
        Transmitting(__instance) = false;
        __instance.transmitting = false;
    }
}
