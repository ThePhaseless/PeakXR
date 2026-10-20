namespace PeakXR.Voice;

/// <summary>
/// Push-to-talk from VR controls. The game reads push-to-talk from its own input action, which has
/// no controller binding in VR; the binding layer (player.locomotion) writes <see cref="PushToTalkHeld"/>
/// every frame and the voice feature merges it into <c>CharacterInput.pushToTalkPressed</c> right after
/// the game samples input. The game's microphone mode (Voice Activation, Push To Talk, Push To Mute)
/// still decides what a press does.
/// </summary>
public static class VoiceInput
{
    /// <summary>Set every frame by the binding layer: true while the push-to-talk button is held.</summary>
    public static bool PushToTalkHeld;

    /// <summary>Same as writing <see cref="PushToTalkHeld"/>.</summary>
    public static void SetPushToTalk(bool held) => PushToTalkHeld = held;

    /// <summary>True while the optional hand-to-mouth gesture counts as push-to-talk.</summary>
    public static bool GestureHeld { get; internal set; }

    /// <summary>What the game sees as the push-to-talk key in VR.</summary>
    public static bool Held => PushToTalkHeld || GestureHeld;
}
