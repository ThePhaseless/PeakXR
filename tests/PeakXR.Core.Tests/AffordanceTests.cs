namespace PeakXR.Core.Tests;

public class AffordanceTests
{
    [Fact]
    public void Gate_FirstDenyPasses_SameReasonRepeatIsQuiet()
    {
        var g = new DenyGate();
        Assert.True(g.Allow(DenyReason.OutOfReach, true, 10f));
        Assert.False(g.Allow(DenyReason.OutOfReach, true, 10.5f));
        Assert.False(g.Allow(DenyReason.OutOfReach, true, 10f + DenyGate.SameSeconds - 0.01f));
        Assert.True(g.Allow(DenyReason.OutOfReach, true, 10f + DenyGate.SameSeconds + 0.01f));
    }

    [Fact]
    public void Gate_AnotherReasonOrHandIsNeverDropped()
    {
        var g = new DenyGate();
        Assert.True(g.Allow(DenyReason.OutOfReach, true, 10f));
        Assert.True(g.Allow(DenyReason.NoStamina, true, 10.01f));
        Assert.True(g.Allow(DenyReason.OutOfReach, false, 10.02f));
    }

    [Fact]
    public void Gate_HandsAreSeparateForTheSameReason()
    {
        var g = new DenyGate();
        Assert.True(g.Allow(DenyReason.NeedsTwoHands, true, 10f));
        Assert.True(g.Allow(DenyReason.NeedsTwoHands, false, 10.1f));
        Assert.False(g.Allow(DenyReason.NeedsTwoHands, true, 10.5f));
    }

    [Fact]
    public void Gate_NoneNeverPasses()
    {
        var g = new DenyGate();
        Assert.False(g.Allow(DenyReason.None, true, 10f));
    }

    [Fact]
    public void Gate_ResetForgets()
    {
        var g = new DenyGate();
        Assert.True(g.Allow(DenyReason.OutOfReach, true, 10f));
        g.Reset();
        Assert.True(g.Allow(DenyReason.OutOfReach, true, 10.01f));
    }

    [Fact]
    public void EveryReasonHasAShortLabel()
    {
        for (var r = DenyReason.None + 1; r < DenyReason.Count; r++)
        {
            var text = DenyText.Label(r);
            Assert.False(string.IsNullOrEmpty(text), r.ToString());
            Assert.True(text.Length <= 20, $"{r}: '{text}' is longer than 20 characters");
        }
        Assert.Equal("", DenyText.Label(DenyReason.None));
    }

    [Theory]
    [InlineData(DenyReason.Overhang, true)]
    [InlineData(DenyReason.TooFlat, true)]
    [InlineData(DenyReason.OutOfReach, false)]
    public void SurfaceReasonsSlip(DenyReason reason, bool slip) =>
        Assert.Equal(slip, DenyHaptics.For(reason) == DenyHaptics.Slip);

    [Fact]
    public void Patterns_AreSoftMultiPulse_UnlikeSingleSuccessTicks()
    {
        foreach (var p in new[] { DenyHaptics.Deny, DenyHaptics.Slip })
        {
            Assert.True(p.Length >= 3);
            for (int i = 0; i < p.Length; i++)
            {
                Assert.InRange(p[i].Amplitude, 0.05f, 1f);
                Assert.InRange(p[i].Seconds, 0.01f, 0.05f);
                if (i > 0)
                    Assert.True(
                        p[i].At - p[i - 1].At >= p[i - 1].Seconds + 0.02f,
                        "pulses must be felt apart"
                    );
            }
            Assert.True(p[p.Length - 1].At <= 0.3f, "the whole pattern stays short");
        }
        // Slip fades out, so it reads as sliding off rather than a knock.
        Assert.True(
            DenyHaptics.Slip[0].Amplitude > DenyHaptics.Slip[DenyHaptics.Slip.Length - 1].Amplitude
        );
    }

    [Fact]
    public void Player_FiresEachPulseOnceInOrder()
    {
        var player = new HapticPatternPlayer();
        player.Start(DenyHaptics.Deny, 5f);
        var fired = 0;
        for (float t = 5f; t < 6f; t += 0.011f)
        {
            while (player.TryNext(t, out var pulse))
            {
                Assert.Equal(DenyHaptics.Deny[fired].Amplitude, pulse.Amplitude);
                Assert.True(t - 5f >= DenyHaptics.Deny[fired].At);
                fired++;
            }
        }
        Assert.Equal(DenyHaptics.Deny.Length, fired);
        Assert.False(player.Playing);
    }

    [Fact]
    public void Player_RestartReplacesPattern()
    {
        var player = new HapticPatternPlayer();
        player.Start(DenyHaptics.Deny, 0f);
        Assert.True(player.TryNext(0f, out _));
        player.Start(DenyHaptics.Slip, 0.01f);
        Assert.True(player.TryNext(0.01f, out var p));
        Assert.Equal(DenyHaptics.Slip[0].Amplitude, p.Amplitude);
    }

    [Fact]
    public void Fade_RisesHoldsFallsWithoutFlashing()
    {
        Assert.Equal(0f, DenyFade.Alpha(-0.1f));
        Assert.Equal(0f, DenyFade.Alpha(DenyFade.Duration + 0.01f));
        Assert.Equal(1f, DenyFade.Alpha(DenyFade.In + 0.01f), 3);
        float prev = 0f;
        bool falling = false;
        for (float t = 0f; t <= DenyFade.Duration; t += 0.01f)
        {
            float a = DenyFade.Alpha(t);
            Assert.InRange(a, 0f, 1f);
            if (a < prev - 1e-5f)
                falling = true;
            else if (falling)
                Assert.True(a <= prev + 1e-5f, "alpha rose again after it started to fall");
            // No step bigger than a 60 Hz frame of the fade-in ramp: no flash.
            Assert.True(Math.Abs(a - prev) <= 0.01f / DenyFade.In + 1e-4f);
            prev = a;
        }
    }

    [Fact]
    public void Fade_IsNotOverInTheFrameItStarts()
    {
        // Shown and posed in the same frame: alpha is still 0, but the refusal has only begun.
        Assert.False(DenyFade.Over(0f, 0f));
        Assert.False(DenyFade.Over(DenyFade.In, DenyFade.In));
        Assert.False(DenyFade.Over(5f, 5f));
        Assert.True(DenyFade.Over(DenyFade.Duration + 0.01f, 0f));
        Assert.True(DenyFade.Over(5f + DenyFade.Out + 0.01f, 5f));
    }

    [Fact]
    public void Fade_ARefusalRepeatedEveryFrame_KeepsOneLiveRing()
    {
        // Reported again each 1/90 s from its first frame on: it must ramp in once and stay up, never restart at 0.
        const float dt = 1f / 90f;
        float at = 10f;
        float held = at;
        float seenFull = 0f;
        for (float now = 10f; now < 11f; now += dt)
        {
            at = DenyFade.Refresh(at, held, now, same: true);
            held = now;
            float a = DenyFade.Alpha(now - at, held - at);
            if (now - 10f > DenyFade.In + dt)
                Assert.True(a > 0.99f, $"alpha {a} at {now - 10f:0.000} s");
            seenFull = MathF.Max(seenFull, a);
        }
        Assert.True(seenFull > 0.99f);
    }

    [Fact]
    public void Fade_RefreshKeepsTheRamp_StartsOverForANewOrEndedRefusal()
    {
        // Still ramping in: the start stays, so the ramp is not cut short (no pop).
        Assert.Equal(10f, DenyFade.Refresh(10f, 10f, 10.05f, same: true));
        // Up at full: back to the start of the hold, still full.
        Assert.Equal(11f - 2f * DenyFade.In, DenyFade.Refresh(10f, 10.5f, 11f, same: true));
        // Another reason, or one that already faded out: a fresh start.
        Assert.Equal(11f, DenyFade.Refresh(10f, 10.5f, 11f, same: false));
        Assert.Equal(20f, DenyFade.Refresh(10f, 10f, 20f, same: true));
    }

    [Fact]
    public void NotLit_PointsAtTheTrigger()
    {
        Assert.Equal("Not lit", DenyText.Label(DenyReason.NotLit));
        Assert.Contains("trigger", DenyText.Hint(DenyReason.NotLit));
        Assert.Equal(CueGlyph.Empty, DenyGlyph.For(DenyReason.NotLit));
    }

    [Fact]
    public void Blocked_NamesTheWindowInFront()
    {
        Assert.Equal("Blocked", DenyText.Label(DenyReason.Blocked));
        Assert.Contains("window", DenyText.Hint(DenyReason.Blocked));
        Assert.Equal(CueGlyph.Padlock, DenyGlyph.For(DenyReason.Blocked));
    }

    [Fact]
    public void Hints_AreShort()
    {
        for (var r = DenyReason.None + 1; r < DenyReason.Count; r++)
            Assert.True(DenyText.Hint(r).Length <= 24, r.ToString());
    }

    [Fact]
    public void Compose_AddsNextStep_AndDistanceToClose()
    {
        Assert.Equal("Used up", DenyText.Compose(DenyReason.UsedUp, 0f));
        var reach = DenyText.Compose(DenyReason.OutOfReach, 0.43f);
        Assert.StartsWith("Too far\n", reach);
        Assert.Contains("Move 0.4 m closer", reach);
        Assert.Contains(
            "Grip with the other hand",
            DenyText.Compose(DenyReason.NeedsTwoHands, 0.5f)
        );
        Assert.Contains("Move closer", DenyText.Compose(DenyReason.OutOfReach, 0f));
    }

    [Fact]
    public void ClampElevation_TiltsIntoTheWindowKeepingHeading()
    {
        var steep = Vector3.Normalize(new Vector3(1f, 3f, 0f));
        var c = DenyRange.ClampElevation(steep, -0.65f, 0.45f);
        Assert.Equal(0.45f, c.Y, 4);
        Assert.Equal(1f, c.Length(), 4);
        Assert.True(c.X > 0f && Math.Abs(c.Z) < 1e-5f);
        var level = Vector3.Normalize(new Vector3(0f, 0.1f, 1f));
        Assert.Equal(level, DenyRange.ClampElevation(level, -0.65f, 0.45f));
        var down = DenyRange.ClampElevation(-Vector3.UnitY, -0.65f, 0.45f);
        Assert.Equal(-0.65f, down.Y, 4);
        Assert.Equal(1f, down.Length(), 4);
    }

    [Fact]
    public void Boundary_IsWhereTheRangeEnds()
    {
        var o = new Vector3(0f, 1f, 0f);
        var b = DenyRange.Boundary(o, new Vector3(0f, 1f, 5f), 2f);
        Assert.Equal(2f, b.Z, 4);
        Assert.Equal(1f, b.Y, 4);
        var near = new Vector3(0f, 1f, 1f);
        Assert.Equal(near, DenyRange.Boundary(o, near, 2f));
    }

    [Theory]
    // Never "can't" inside the limit: out of range clears as soon as the limit is reached.
    [InlineData(false, 1.99f, true)]
    [InlineData(false, 2.01f, false)]
    // In range turns out only past the margin, so the cue does not flicker at the edge.
    [InlineData(true, 2.05f, true)]
    [InlineData(true, 2.15f, false)]
    public void Range_HysteresisOnlyOnTheCanSide(bool was, float distance, bool expected) =>
        Assert.Equal(expected, DenyRange.Within(distance, 2f, was, 0.1f));
}
