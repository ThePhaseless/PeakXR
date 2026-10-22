namespace PeakXR.Core.Tests;

public class StaminaOutCueTests
{
    [Fact]
    public void RunningOutWhileSprinting_CuesOnce()
    {
        var c = new StaminaOutCue();
        Assert.False(c.Update(false, exerting: true, sprintWanted: true, now: 0f));
        Assert.True(c.Update(true, exerting: true, sprintWanted: true, now: 0.1f));
        Assert.False(c.Update(true, exerting: false, sprintWanted: true, now: 0.2f));
        Assert.False(c.Update(true, exerting: false, sprintWanted: true, now: 3f));
    }

    [Fact]
    public void RunningOutAtRest_IsNotABodyCue()
    {
        // Stamina emptied by something else (a status, a climb handled elsewhere): no sprint or jump to refuse.
        var c = new StaminaOutCue();
        Assert.False(c.Update(false, false, false, 0f));
        Assert.False(c.Update(true, false, false, 0.1f));
    }

    [Fact]
    public void AskingToSprintWhileEmpty_CuesOnEachNewAsk()
    {
        var c = new StaminaOutCue();
        Assert.False(c.Update(true, false, false, 0f));
        Assert.True(c.Update(true, false, true, 0.1f));
        Assert.False(c.Update(true, false, true, 0.2f)); // still holding: no repeat
        Assert.False(c.Update(true, false, false, 2f));
        Assert.True(c.Update(true, false, true, 2.1f)); // asked again
    }

    [Fact]
    public void RepeatsAreSpacedOut()
    {
        var c = new StaminaOutCue();
        Assert.True(c.Update(true, false, true, 0f));
        Assert.False(c.Update(true, false, false, 0.1f));
        Assert.False(c.Update(true, false, true, 0.2f)); // a second click too soon
    }

    [Fact]
    public void StaminaBack_ArmsTheRunOutCueAgain()
    {
        var c = new StaminaOutCue();
        Assert.True(c.Update(true, true, true, 0f));
        Assert.False(c.Update(false, false, false, 1f));
        Assert.True(c.Update(true, true, true, 3f));
    }
}

public class BodyCuesTests
{
    [Fact]
    public void Landing_GrowsWithImpactSpeed_WithinLimits()
    {
        Assert.Equal(BodyCues.LandingMin, BodyCues.LandingAmplitude(0f));
        Assert.True(BodyCues.LandingAmplitude(5f) > BodyCues.LandingAmplitude(3f));
        Assert.True(BodyCues.LandingAmplitude(12f) >= 0.99f);
        Assert.Equal(1f, BodyCues.LandingAmplitude(40f));
        Assert.Equal(BodyCues.LandingMin, BodyCues.LandingAmplitude(float.NaN));
    }

    [Fact]
    public void Landing_AfterAnEightMetreFall_IsStrong()
    {
        // v = sqrt(2 g h) for 8 m at 9.81 m/s^2: about 12.5 m/s.
        Assert.True(BodyCues.LandingAmplitude(MathF.Sqrt(2f * 9.81f * 8f)) >= 0.6f);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.9f, 0f)] // damage at 1.5 s: quiet until half a second before
    [InlineData(1.0f, 0f)]
    [InlineData(1.5f, 0.333f)]
    [InlineData(2.5f, 1f)]
    [InlineData(9f, 1f)]
    public void FallDanger_BuildsBeforeDamage(float fallTime, float urgency) =>
        TestUtil.Near(urgency, BodyCues.FallUrgency(fallTime, 1.5f), 0.01f);

    [Fact]
    public void Side_HitFromTheRight_FeltMostOnTheRight()
    {
        BodyCues.SideGains(new Vector2(1f, 0f), new Vector2(1f, 0f), out float l, out float r);
        Assert.Equal(1f, r);
        TestUtil.Near(BodyCues.FarSide, l);
    }

    [Fact]
    public void Side_FromTheLeft_FeltMostOnTheLeft()
    {
        BodyCues.SideGains(new Vector2(-3f, 0.5f), new Vector2(1f, 0f), out float l, out float r);
        Assert.Equal(1f, l);
        Assert.True(r < 0.5f);
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(0f, -1f)]
    [InlineData(0f, 0f)]
    public void Side_FromAheadBehindOrNowhere_BothHandsFull(float x, float z)
    {
        BodyCues.SideGains(new Vector2(x, z), new Vector2(1f, 0f), out float l, out float r);
        Assert.Equal(1f, l);
        Assert.Equal(1f, r);
    }

    [Fact]
    public void Footing_TicksOnceWhenControlReturnsAfterAFall()
    {
        var f = new FootingCue();
        Assert.False(f.Update(1f));
        Assert.False(f.Update(0.95f)); // never fell
        Assert.False(f.Update(0.2f));
        Assert.False(f.Update(0.7f));
        Assert.True(f.Update(0.92f));
        Assert.False(f.Update(1f));
        Assert.False(f.Update(0.85f)); // a wobble, not a fall
        Assert.False(f.Update(0.95f));
    }

    [Fact]
    public void Bounce_SpringGrowsWithTheGamesBounce()
    {
        Assert.Equal(BodyCues.BounceMin, BodyCues.BounceAmplitude(1f, 7.5f));
        Assert.Equal(1f, BodyCues.BounceAmplitude(7.5f, 7.5f));
        Assert.True(BodyCues.BounceAmplitude(4f, 7.5f) > BodyCues.BounceAmplitude(2f, 7.5f));
    }

    [Fact]
    public void Brace_CrouchingCutsThePullToAQuarter()
    {
        TestUtil.Near(0.25f * BodyCues.PullAmplitude(1f), BodyCues.PullAmplitude(1f, crouched: true));
        Assert.True(BodyCues.PullAmplitude(1f) <= BodyCues.PressureMax);
        Assert.Equal(0f, BodyCues.PullAmplitude(0f));
    }

    [Theory]
    [InlineData(5f, 0f)]
    [InlineData(1.5f, 0f)]
    [InlineData(0.1f, 0.93f)]
    [InlineData(0f, 1f)]
    public void Countdown_BuildsOverItsLastSeconds(float left, float urgency) =>
        TestUtil.Near(urgency, BodyCues.CountdownUrgency(left, 1.5f), 0.07f);
}

public class HeartbeatTests
{
    const float Dt = 1f / 90f;

    /// <summary>Runs the heartbeat for seconds; returns the beats (first pulse of each lub-dub), the mean gap and
    /// the last beat's amplitude.</summary>
    static (int beats, float period, float amplitude) Run(
        Heartbeat h,
        float seconds,
        bool exerting,
        float stamina,
        float weight
    )
    {
        int beats = 0;
        float first = -1f,
            last = -1f,
            amp = 0f;
        for (float t = 0f; t < seconds; t += Dt)
        {
            if (h.Update(exerting, stamina, weight, Dt, out var pulse) && pulse.Beat)
            {
                beats++;
                if (first < 0f)
                    first = t;
                last = t;
                amp = pulse.Amplitude;
            }
        }
        return (beats, beats > 1 ? (last - first) / (beats - 1) : 0f, amp);
    }

    [Fact]
    public void Resting_IsQuiet()
    {
        Assert.Equal(0, Run(new Heartbeat(), 5f, false, 0.2f, 0f).beats);
    }

    [Fact]
    public void ExertingWithAFullBar_IsQuiet()
    {
        Assert.Equal(0, Run(new Heartbeat(), 5f, true, 0.95f, 0f).beats);
    }

    [Fact]
    public void ExertingLow_BeatsFasterAsStaminaFalls()
    {
        var half = Run(new Heartbeat(), 6f, true, 0.5f, 0f);
        var low = Run(new Heartbeat(), 6f, true, 0.1f, 0f);
        Assert.True(half.beats >= 3);
        Assert.True(low.period < half.period, $"{low.period} vs {half.period}");
    }

    [Fact]
    public void Weight_SlowsAndDeepensTheBeat()
    {
        var light = Run(new Heartbeat(), 6f, true, 0.4f, 0f);
        var heavy = Run(new Heartbeat(), 6f, true, 0.4f, 0.5f);
        Assert.True(heavy.period > light.period, $"{heavy.period} vs {light.period}");
        Assert.True(heavy.amplitude > light.amplitude);
    }

    [Fact]
    public void Weight_IsFeltEvenWithAFullBar()
    {
        Assert.True(Run(new Heartbeat(), 5f, true, 1f, 0.5f).beats >= 2);
    }

    [Fact]
    public void EachBeat_IsALubDub()
    {
        var h = new Heartbeat();
        int lub = 0,
            dub = 0;
        for (float t = 0f; t < 3f; t += Dt)
        {
            if (h.Update(true, 0.2f, 0f, Dt, out var p))
            {
                if (p.Beat)
                    lub++;
                else
                    dub++;
            }
        }
        Assert.True(lub > 0);
        Assert.InRange(dub, lub - 1, lub);
    }
}

public class ShiverTests
{
    const float Dt = 1f / 90f;

    static int Run(Shiver s, float seconds, float from, float ratePerSecond)
    {
        int pulses = 0;
        float cold = from;
        for (float t = 0f; t < seconds; t += Dt)
        {
            cold += ratePerSecond * Dt;
            if (s.Update(cold, Dt) > 0f)
                pulses++;
        }
        return pulses;
    }

    [Fact]
    public void ColdRising_Shivers()
    {
        Assert.True(Run(new Shiver(), 2f, 0.1f, 0.02f) > 10);
    }

    [Fact]
    public void ColdSteadyOrFalling_IsQuiet()
    {
        Assert.Equal(0, Run(new Shiver(), 2f, 0.3f, 0f));
        Assert.Equal(0, Run(new Shiver(), 2f, 0.3f, -0.05f));
    }

    [Fact]
    public void ATinyFirstChill_IsQuiet()
    {
        // Barely cold: the first status step is not a shiver yet.
        Assert.Equal(0, Run(new Shiver(), 1f, 0f, 0.02f));
    }

    [Fact]
    public void AStepUpInCold_ShiversForAWhile()
    {
        // The game adds cold in steps (wind, night): one step keeps the shiver going briefly, then it stops.
        var s = new Shiver();
        int during = 0,
            after = 0;
        s.Update(0.2f, Dt);
        for (float t = 0f; t < 3f; t += Dt)
        {
            bool p = s.Update(0.25f, Dt) > 0f;
            if (t < 0.5f && p)
                during++;
            if (t > 2.5f && p)
                after++;
        }
        Assert.True(during > 3);
        Assert.Equal(0, after);
    }
}
