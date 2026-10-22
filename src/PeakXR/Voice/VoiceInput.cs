namespace PeakXR.Voice;

/// <summary>
/// Push-to-talk from VR controls. The game reads push-to-talk from its own input action, which has
/// no controller binding in VR. The binding layer (player.locomotion) writes <see cref="PushToTalkHeld"/> for a
/// table binding (none by default, D4) and <see cref="VoiceRuntime"/> writes <see cref="GestureHeld"/> from the
/// mouth gestures and <c>Voice.PushToTalkButton</c>; the voice feature merges both into
/// <c>CharacterInput.pushToTalkPressed</c> right after the game samples input. The game's microphone mode (Voice
/// Activation, Push To Talk, Push To Mute) still decides what a press does.
/// </summary>
public static class VoiceInput
{
    /// <summary>Set every frame by the binding layer: true while a push-to-talk table binding is held.</summary>
    public static bool PushToTalkHeld;

    /// <summary>Same as writing <see cref="PushToTalkHeld"/>.</summary>
    public static void SetPushToTalk(bool held) => PushToTalkHeld = held;

    /// <summary>True while the VR voice controls press the game's key (<see cref="PeakXR.Core.VoiceKeys.Key"/>): the
    /// walkie-talkie hand in Push To Talk, the palm over the mouth in Push To Mute, the push-to-talk button.</summary>
    public static bool GestureHeld { get; internal set; }

    /// <summary>A flat palm covers the mouth (Voice.PalmMute).</summary>
    public static bool MuteHeld { get; internal set; }

    /// <summary>Hold the local mic shut although the game's mode would send (Voice Activation with the palm on).</summary>
    public static bool Gate { get; internal set; }

    /// <summary>What the game sees as the push-to-talk key in VR.</summary>
    public static bool Held => PushToTalkHeld || GestureHeld;
}
