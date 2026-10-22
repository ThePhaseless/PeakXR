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
}
