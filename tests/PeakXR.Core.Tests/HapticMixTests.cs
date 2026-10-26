namespace PeakXR.Core.Tests;

public class HapticMixTests
{
    [Theory]
    [InlineData(0.32f)]
    [InlineData(0.6f)]
    [InlineData(1f)]
    [InlineData(0.08f)]
    public void Retune_NewDefaultFeelsLikeTheOldDefault(float amplitude)
    {
        // Before: amplitude x strength, default strength 0.5.
        float old = amplitude * 0.5f;
        Assert.Equal(1f, HapticScale.DefaultStrength);
        Assert.Equal(
            old,
            HapticScale.Felt(amplitude, HapticScale.DefaultStrength, HapticScale.Cue),
            5
        );
    }

    [Fact]
    public void Retune_UiTicksKeepTheirFeelAtTheDefault_AndFollowTheSlider()
    {
        // UI ticks went out unscaled before; at the new default they feel the same, and the slider now applies.
        Assert.Equal(0.2f, HapticScale.Felt(0.2f, HapticScale.DefaultStrength, HapticScale.Ui), 5);
        Assert.Equal(0.1f, HapticScale.Felt(0.2f, 0.5f, HapticScale.Ui), 5);
        Assert.Equal(0f, HapticScale.Felt(0.2f, 0f, HapticScale.Ui));
    }

    [Fact]
    public void Retune_DoubledSliderReachesTheOldMaximum()
    {
        // Old maximum: strength 1 -> amplitude x 1. New slider range runs to 2, so nobody loses strength.
        Assert.Equal(0.6f, HapticScale.Felt(0.6f, HapticScale.MaxStrength, HapticScale.Cue), 5);
        Assert.Equal(1f, HapticScale.Felt(1f, HapticScale.MaxStrength, HapticScale.Ui));
    }

    [Fact]
    public void Channel_AFreeHandTakesAnything()
    {
        var ch = new HapticChannel();
        Assert.True(ch.Allow(HapticPriority.Ambient, 10f, 0.05f));
    }

    [Fact]
    public void Channel_LowerNeverCutsAHigherOneStillPlaying()
    {
        var ch = new HapticChannel();
        ch.Hold(HapticPriority.Alert, 10f, 0.16f);
        Assert.False(ch.Allow(HapticPriority.Cue, 10.05f, 0.03f));
        Assert.False(ch.Allow(HapticPriority.Ambient, 10.1f, 0.05f));
        // Equal or higher takes over: the refusal's own next pulse.
        Assert.True(ch.Allow(HapticPriority.Alert, 10.07f, 0.02f));
        // The hold keeps its end: the pattern's later pulses still block lower ones.
        Assert.False(ch.Allow(HapticPriority.Cue, 10.12f, 0.03f));
        Assert.True(ch.Allow(HapticPriority.Cue, 10.17f, 0.03f));
    }

    [Fact]
    public void Channel_AmbientDucksUnderCues_ForAMomentAfterToo()
    {
        var ch = new HapticChannel();
        Assert.True(ch.Allow(HapticPriority.Cue, 10f, 0.04f));
        Assert.False(ch.Allow(HapticPriority.Ambient, 10.02f, 0.05f));
        // Just after the cue ended: still ducked, so the cue reads alone.
        Assert.False(
            ch.Allow(HapticPriority.Ambient, 10.04f + HapticChannel.DuckTail * 0.5f, 0.05f)
        );
        Assert.True(
            ch.Allow(HapticPriority.Ambient, 10.04f + HapticChannel.DuckTail + 0.01f, 0.05f)
        );
        // Ambient after ambient is never held back.
        Assert.True(ch.Allow(HapticPriority.Ambient, 10.2f, 0.05f));
        Assert.True(ch.Allow(HapticPriority.Ambient, 10.21f, 0.05f));
    }

    [Fact]
    public void Channel_ARefusalCutsAmbientAndCues()
    {
        var ch = new HapticChannel();
        Assert.True(ch.Allow(HapticPriority.Ambient, 10f, 0.3f));
        Assert.True(ch.Allow(HapticPriority.Alert, 10.01f, 0.02f));
        Assert.True(ch.Allow(HapticPriority.Cue, 10.2f, 0.02f));
        Assert.True(ch.Allow(HapticPriority.Alert, 10.205f, 0.02f));
    }

    [Fact]
    public void Channel_ARefusalPatternSurvivesAHeartbeatEveryFrame()
    {
        // The refusal triple held for its whole length while an ambient beat asks every frame at 90 Hz.
        var ch = new HapticChannel();
        var pattern = DenyHaptics.Deny;
        float start = 10f;
        float length = pattern[^1].At + pattern[^1].Seconds;
        ch.Hold(HapticPriority.Alert, start, length);
        int next = 0;
        int sent = 0;
        for (float now = start; now < start + 0.5f; now += 1f / 90f)
        {
            if (next < pattern.Length && now - start >= pattern[next].At)
            {
                Assert.True(ch.Allow(HapticPriority.Alert, now, pattern[next].Seconds));
                next++;
                sent++;
            }
            bool ambient = ch.Allow(HapticPriority.Ambient, now, 0.05f);
            if (now < start + length + HapticChannel.DuckTail)
                Assert.False(ambient, $"ambient cut in at {now - start:0.000} s");
        }
        Assert.Equal(pattern.Length, sent);
    }

    /// <summary>The laser's hover tick (UI scale): the loudest routine UI feel a refusal must beat.</summary>
    const float HoverTick = 0.2f;

    [Theory]
    [InlineData(0.25f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void Alert_IsFeltAboveAHoverTick_AtAnySlider(float strength)
    {
        float hover = HapticScale.Felt(HoverTick, strength, HapticScale.Ui);
        foreach (var p in DenyHaptics.Deny)
            Assert.True(HapticScale.Felt(p.Amplitude, strength, HapticScale.Cue) > hover, "deny");
        foreach (var p in DenyHaptics.Slip)
            Assert.True(HapticScale.Felt(p.Amplitude, strength, HapticScale.Cue) > hover, "slip");
        Assert.True(
            HapticScale.Felt(DangerPulse.Amplitude(0f), strength, HapticScale.Cue) > hover,
            "danger"
        );
    }

    [Fact]
    public void Deny_DeliversAboutHalf_At30Ms_KeepingItsRhythm()
    {
        foreach (var p in DenyHaptics.Deny)
        {
            Assert.Equal(0.5f, HapticScale.Felt(p.Amplitude, 1f, HapticScale.Cue), 2);
            Assert.Equal(0.03f, p.Seconds, 3);
        }
        Assert.Equal(new[] { 0f, 0.07f, 0.14f }, Array.ConvertAll(DenyHaptics.Deny, p => p.At));
    }

    [Fact]
    public void Alert_IsTheStrongestCueFeel()
    {
        float deny = DenyHaptics.Deny[0].Amplitude;
        Assert.True(deny >= CueHaptics.EndStop.Amplitude);
        Assert.True(deny >= CueHaptics.Contact.Amplitude);
        Assert.True(deny >= ProgressRamp.Amplitude(1f));
        Assert.True(DangerPulse.Amplitude(1f) >= ProgressRamp.Amplitude(1f));
    }

    [Theory]
    [InlineData(HapticDetail.Full, HapticPriority.Ambient, true)]
    [InlineData(HapticDetail.Full, HapticPriority.Cue, true)]
    [InlineData(HapticDetail.Full, HapticPriority.Alert, true)]
    [InlineData(HapticDetail.Essential, HapticPriority.Ambient, false)]
    [InlineData(HapticDetail.Essential, HapticPriority.Cue, true)]
    [InlineData(HapticDetail.Essential, HapticPriority.Alert, true)]
    public void Essential_DropsOnlyAmbientFeel(
        HapticDetail detail,
        HapticPriority priority,
        bool plays
    ) => Assert.Equal(plays, HapticScale.Plays(detail, priority));
}

public class SteadyRumbleTests
{
    [Fact]
    public void EachImpulseOutlastsTheNextRefresh_SoTheRumbleHasNoGaps()
    {
        // A refresh can land a frame late (90 Hz: 11 ms, a hitch: more); the impulse must still be playing then.
        Assert.True(SteadyRumble.Seconds >= SteadyRumble.Refresh + 0.05f);
    }

    [Fact]
    public void RefreshesKeepTheChannel_AtAmbientPriority()
    {
        var channel = new HapticChannel();
        for (int i = 0; i < 20; i++)
            Assert.True(
                channel.Allow(
                    HapticPriority.Ambient,
                    i * SteadyRumble.Refresh,
                    SteadyRumble.Seconds
                )
            );
    }

    [Fact]
    public void EnvironmentRumble_YieldsToAmbientBeats_AndResumesRightAfter()
    {
        var ch = new HapticChannel();
        Assert.True(ch.Allow(HapticPriority.Environment, 10f, SteadyRumble.Seconds));
        // A heartbeat takes the hand over a playing rumble; the rumble waits it out, then carries on.
        Assert.True(ch.Allow(HapticPriority.Ambient, 10.01f, 0.1f));
        Assert.False(ch.Allow(HapticPriority.Environment, 10.05f, SteadyRumble.Seconds));
        Assert.True(ch.Allow(HapticPriority.Environment, 10.12f, SteadyRumble.Seconds));
    }

    [Fact]
    public void EnvironmentRumble_DucksUnderCues_ForTheTail()
    {
        var ch = new HapticChannel();
        Assert.True(ch.Allow(HapticPriority.Cue, 10f, 0.05f));
        Assert.False(
            ch.Allow(HapticPriority.Environment, 10.05f + HapticChannel.DuckTail * 0.5f, 0.15f)
        );
        Assert.True(ch.Allow(HapticPriority.Environment, 10.06f + HapticChannel.DuckTail, 0.15f));
    }

    [Theory]
    [InlineData(HapticDetail.Full, true)]
    [InlineData(HapticDetail.Essential, false)]
    public void Essential_DropsTheEnvironmentRumble(HapticDetail detail, bool plays) =>
        Assert.Equal(plays, HapticScale.Plays(detail, HapticPriority.Environment));
}
