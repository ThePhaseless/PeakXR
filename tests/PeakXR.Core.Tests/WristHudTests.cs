namespace PeakXR.Core.Tests;

public class WristHudTests
{
    [Fact]
    public void Width_KeepsTheSameAngleAtAnyReadingDistance()
    {
        float near = WristHud.Width(0.16f, 0.3f, 0f);
        float far = WristHud.Width(0.16f, 0.6f, 0f);
        Assert.Equal(0.16f, WristHud.Width(0.16f, WristHud.ReadDistance, 0f), 4);
        Assert.Equal(near / 0.3f, far / 0.6f, 4);
    }

    [Fact]
    public void Width_StaysWristSizedWhenTheArmIsFarOrAtTheFace()
    {
        Assert.Equal(WristHud.Width(0.16f, 0.7f, 0f), WristHud.Width(0.16f, 3f, 0f), 4);
        Assert.Equal(WristHud.Width(0.16f, 0.2f, 0f), WristHud.Width(0.16f, 0.01f, 0f), 4);
    }

    [Fact]
    public void Width_GrowsOnAnAlertPulse()
    {
        Assert.True(WristHud.Width(0.16f, 0.4f, 1f) > WristHud.Width(0.16f, 0.4f, 0f));
    }

    [Fact]
    public void Facing_FullWhenLookedAt()
    {
        Assert.Equal(1f, WristHud.Facing(5f, 0f));
        Assert.Equal(1f, WristHud.Facing(WristHud.LookDegrees, 0f));
    }

    [Fact]
    public void Facing_HalfTurnedWhenNotLookedAt()
    {
        Assert.Equal(WristHud.RestFacing, WristHud.Facing(90f, 0f));
        float mid = WristHud.Facing((WristHud.LookDegrees + WristHud.AwayDegrees) / 2f, 0f);
        Assert.True(mid > WristHud.RestFacing && mid < 1f);
    }

    [Fact]
    public void Facing_TurnsToTheEyeOnAnAlert()
    {
        Assert.Equal(1f, WristHud.Facing(90f, 1f));
    }
}
