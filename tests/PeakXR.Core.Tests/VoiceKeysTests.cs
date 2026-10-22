namespace PeakXR.Core.Tests;

public class VoiceKeysTests
{
    [Theory]
    // Push To Talk: talking opens the mic, a palm over the mouth wins over everything.
    [InlineData(MicMode.PushToTalk, false, true, false, true)]
    [InlineData(MicMode.PushToTalk, true, false, false, true)]
    [InlineData(MicMode.PushToTalk, false, true, true, false)]
    [InlineData(MicMode.PushToTalk, false, false, false, false)]
    // Push To Mute: the key mutes, so only the palm (or the button) presses it; a walkie-talkie hand never mutes.
    [InlineData(MicMode.PushToMute, false, true, false, false)]
    [InlineData(MicMode.PushToMute, false, false, true, true)]
    [InlineData(MicMode.PushToMute, true, false, false, true)]
    // Voice Activation ignores the key; only the button passes through untouched.
    [InlineData(MicMode.VoiceActivation, false, true, false, false)]
    [InlineData(MicMode.VoiceActivation, true, false, false, true)]
    public void Key_PerMode(MicMode mode, bool button, bool talk, bool mute, bool expected)
    {
        Assert.Equal(expected, VoiceKeys.Key(mode, button, talk, mute));
    }

    [Theory]
    [InlineData(MicMode.VoiceActivation, true, true)]
    [InlineData(MicMode.VoiceActivation, false, false)]
    [InlineData(MicMode.PushToTalk, true, false)]
    [InlineData(MicMode.PushToMute, true, false)]
    public void Gate_OnlyWhereTheKeyCannotMute(MicMode mode, bool mute, bool gate)
    {
        Assert.Equal(gate, VoiceKeys.Gate(mode, mute));
    }

    [Theory]
    [InlineData(MicMode.PushToTalk, true, false, false, MicGlyph.Open)]
    [InlineData(MicMode.PushToTalk, false, false, true, MicGlyph.Off)]
    [InlineData(MicMode.PushToTalk, false, true, false, MicGlyph.Muted)]
    [InlineData(MicMode.PushToMute, true, false, true, MicGlyph.Muted)]
    [InlineData(MicMode.PushToMute, false, false, true, MicGlyph.Open)]
    [InlineData(MicMode.PushToMute, false, false, false, MicGlyph.Off)]
    [InlineData(MicMode.VoiceActivation, false, false, true, MicGlyph.Open)]
    [InlineData(MicMode.VoiceActivation, false, true, true, MicGlyph.Muted)]
    [InlineData(MicMode.VoiceActivation, false, false, false, MicGlyph.Off)]
    public void Glyph_ShowsAnOpenOrMutedMic(
        MicMode mode,
        bool key,
        bool mute,
        bool speaking,
        MicGlyph expected
    )
    {
        Assert.Equal(expected, VoiceKeys.Glyph(mode, key, mute, speaking));
    }
}
