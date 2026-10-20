namespace PeakXR.Core.Tests;

public class VoiceTests
{
    [Theory]
    [InlineData("Microphone (Valve Index HMD)")]
    [InlineData("Microphone (2- Valve VR Radio & HMD Mic)")]
    [InlineData("Headset Microphone (Oculus Virtual Audio Device)")]
    [InlineData("Microphone (Meta Quest 3)")]
    [InlineData("Headset Microphone (Rift S)")]
    [InlineData("Microphone (HTC VIVE Pro Mutimedia Audio)")]
    [InlineData("Microphone (HP Reverb G2)")]
    [InlineData("Microphone (PICO 4 Audio)")]
    [InlineData("Microphone (Bigscreen Beyond Audio Strap)")]
    [InlineData("Microphone (Virtual Desktop Audio)")]
    [InlineData("Microphone (ALVR Audio)")]
    public void HeadsetNames_Match(string name) => Assert.True(VoiceMic.IsHeadsetMic(name));

    [Theory]
    [InlineData("Microphone (Realtek(R) Audio)")]
    [InlineData("Microphone (Blue Yeti)")]
    [InlineData("Survivor Mic Array")]
    [InlineData("Picolo USB Mic")]
    [InlineData("Questionable Webcam Mic")]
    [InlineData("")]
    [InlineData(null)]
    public void OtherNames_DoNotMatch(string name) => Assert.False(VoiceMic.IsHeadsetMic(name));

    private static readonly string[] devices =
    {
        "Microphone (Realtek(R) Audio)",
        "Microphone (Blue Yeti)",
        "Headset Microphone (Oculus Virtual Audio Device)",
    };

    [Fact]
    public void Preferred_SwitchesFromDefaultToHeadset() =>
        Assert.Equal(2, VoiceMic.PreferredIndex(devices, devices[0]));

    [Fact]
    public void Preferred_TreatsEmptySelectionAsDefault() =>
        Assert.Equal(2, VoiceMic.PreferredIndex(devices, ""));

    [Fact]
    public void Preferred_KeepsExplicitNonDefaultChoice() =>
        Assert.Equal(-1, VoiceMic.PreferredIndex(devices, devices[1]));

    [Fact]
    public void Preferred_NothingWhenDefaultIsAlreadyHeadset() =>
        Assert.Equal(
            -1,
            VoiceMic.PreferredIndex(new[] { "Microphone (Valve Index HMD)", "Blue Yeti" }, "")
        );

    [Fact]
    public void Preferred_NothingWithoutHeadsetOrDevices()
    {
        Assert.Equal(-1, VoiceMic.PreferredIndex(new[] { "Realtek", "Blue Yeti" }, "Realtek"));
        Assert.Equal(-1, VoiceMic.PreferredIndex(Array.Empty<string>(), ""));
        Assert.Equal(-1, VoiceMic.PreferredIndex(null, ""));
    }

    [Fact]
    public void PushToTalk_HeldForcesPressed()
    {
        bool forced = false;
        Assert.True(VoicePushToTalk.Merge(false, held: true, movementActive: true, ref forced));
        Assert.True(forced);
        Assert.True(VoicePushToTalk.Merge(false, held: true, movementActive: false, ref forced));
    }

    [Fact]
    public void PushToTalk_KeyboardStillWorksWhenNotHeld()
    {
        bool forced = false;
        Assert.True(VoicePushToTalk.Merge(true, held: false, movementActive: true, ref forced));
        Assert.False(forced);
    }

    [Fact]
    public void PushToTalk_ReleaseWhileInputBlocked_ClearsOwnStaleValue()
    {
        bool forced = false;
        VoicePushToTalk.Merge(false, held: true, movementActive: true, ref forced);
        // Sample() skips the push-to-talk read while movement is blocked, so the field keeps our true.
        Assert.False(VoicePushToTalk.Merge(true, held: false, movementActive: false, ref forced));
        Assert.False(forced);
        // Afterwards a vanilla value is left untouched.
        Assert.True(VoicePushToTalk.Merge(true, held: false, movementActive: false, ref forced));
    }

    [Fact]
    public void PushToTalk_ReleaseWhileActive_UsesVanillaValue()
    {
        bool forced = false;
        VoicePushToTalk.Merge(false, held: true, movementActive: true, ref forced);
        Assert.True(VoicePushToTalk.Merge(true, held: false, movementActive: true, ref forced));
        Assert.False(forced);
    }

    [Theory]
    [InlineData(0, 4, 0)]
    [InlineData(1, 4, 2)]
    [InlineData(2, 4, 3)]
    [InlineData(9, 4, 3)]
    [InlineData(-1, 4, 0)]
    [InlineData(1, 0, -1)]
    public void SpriteIndex_MatchesVanillaNameTag(int amplitude, int count, int expected) =>
        Assert.Equal(expected, VoiceIndicator.SpriteIndex(amplitude, count));

    [Fact]
    public void WorldSize_KeepsAngularSize()
    {
        float near = VoiceIndicator.WorldSize(1f, 2f);
        float far = VoiceIndicator.WorldSize(10f, 2f);
        TestUtil.Near(2f * MathF.Tan(MathF.PI / 180f), near, 1e-6f);
        TestUtil.Near(near * 10f, far, 1e-5f);
        Assert.Equal(0f, VoiceIndicator.WorldSize(-1f, 2f));
    }

    [Fact]
    public void Fade_RisesWhileSpeaking()
    {
        var f = new VoiceIndicatorFade { HoldSeconds = 1f, FadeSeconds = 0.2f };
        f.Update(true, 0.1f);
        TestUtil.Near(0.5f, f.Alpha, 1e-5f);
        f.Update(true, 0.2f);
        Assert.Equal(1f, f.Alpha);
    }

    [Fact]
    public void Fade_HoldsThenFadesAfterSpeech()
    {
        var f = new VoiceIndicatorFade { HoldSeconds = 1f, FadeSeconds = 0.2f };
        f.Update(true, 1f);
        f.Update(false, 0.9f);
        Assert.Equal(1f, f.Alpha);
        f.Update(false, 0.2f);
        TestUtil.Near(0.5f, f.Alpha, 1e-5f);
        f.Update(false, 0.2f);
        Assert.Equal(0f, f.Alpha);
        Assert.False(f.Visible);
    }

    [Fact]
    public void Fade_SpeechRestartsHold()
    {
        var f = new VoiceIndicatorFade { HoldSeconds = 1f, FadeSeconds = 0.2f };
        f.Update(true, 1f);
        f.Update(false, 0.9f);
        f.Update(true, 0.01f);
        f.Update(false, 0.9f);
        Assert.Equal(1f, f.Alpha);
    }

    [Fact]
    public void Fade_ResetHides()
    {
        var f = new VoiceIndicatorFade();
        f.Update(true, 1f);
        f.Reset();
        Assert.Equal(0f, f.Alpha);
        f.Update(false, 0.01f);
        Assert.False(f.Visible);
    }
}
