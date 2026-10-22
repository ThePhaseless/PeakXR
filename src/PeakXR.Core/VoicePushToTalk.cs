namespace PeakXR.Core;

/// <summary>
/// Merges the VR push-to-talk button into the game's <c>pushToTalkPressed</c> field after the game
/// sampled its own input. Kept pure because of one trap: the game only re-reads push-to-talk while
/// movement input is allowed, so a VR press released during a menu would otherwise stay "held".
/// </summary>
public static class VoicePushToTalk
{
    /// <param name="vanilla">Field value after the game's own sampling.</param>
    /// <param name="held">VR push-to-talk held this frame (button or gesture).</param>
    /// <param name="movementActive">Whether the game re-read push-to-talk this frame.</param>
    /// <param name="forced">State between frames: true while the field holds a value we wrote.</param>
    /// <returns>The value to store back into the field.</returns>
    public static bool Merge(bool vanilla, bool held, bool movementActive, ref bool forced)
    {
        if (held)
        {
            forced = true;
            return true;
        }
        if (!forced)
            return vanilla;
        forced = false;
        return movementActive && vanilla;
    }
}

/// <summary>The game's microphone modes, in its order (PushToTalkSetting.PushToTalkType).</summary>
public enum MicMode : byte
{
    VoiceActivation,
    PushToTalk,
    PushToMute,
}

/// <summary>The local mic glyph: hidden, open (sending your voice) or muted.</summary>
public enum MicGlyph : byte
{
    Off,
    Open,
    Muted,
}

/// <summary>
/// The VR voice controls per microphone mode. A walkie-talkie hand at the mouth talks and a flat palm over the mouth
/// mutes, whatever the mode: the game's key means "talk" in Push To Talk and "mute" in Push To Mute
/// (CharacterVoiceHandler.cs:250), so the gestures map onto it by meaning. In Voice Activation the key does nothing,
/// so mute is a local gate there.
/// </summary>
public static class VoiceKeys
{
    /// <summary>The game's <c>pushToTalkPressed</c> from the VR controls. <paramref name="button"/> keeps the raw key
    /// meaning (Voice.PushToTalkButton).</summary>
    public static bool Key(MicMode mode, bool button, bool talk, bool mute) =>
        mode switch
        {
            MicMode.PushToTalk => button || (talk && !mute),
            MicMode.PushToMute => button || mute,
            _ => button,
        };

    /// <summary>Hold the mic shut locally: only in Voice Activation, where no key can mute.</summary>
    public static bool Gate(MicMode mode, bool mute) => mute && mode == MicMode.VoiceActivation;

    /// <summary>What the mic glyph shows. <paramref name="speaking"/> is the local mouth picking up a voice: in the
    /// modes that send without a key the glyph shows only while you speak.</summary>
    public static MicGlyph Glyph(MicMode mode, bool key, bool mute, bool speaking)
    {
        if (mute || (mode == MicMode.PushToMute && key))
            return MicGlyph.Muted;
        bool open = mode == MicMode.PushToTalk ? key : speaking;
        return open ? MicGlyph.Open : MicGlyph.Off;
    }
}
