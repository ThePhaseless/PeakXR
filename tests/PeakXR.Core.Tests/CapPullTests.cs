namespace PeakXR.Core.Tests;

public class CapPullTests
{
    const float Dt = 0.02f;

    static CapPull Make() =>
        new()
        {
            Distance = 0.1f,
            Seconds = 1f,
            ShortShare = 0.3f,
        };

    [Fact]
    public void GripAtTheEnd_ThenPullAway_Lights()
    {
        var p = Make();
        Assert.Equal(CapPullEvent.None, p.Update(false, true, 0.2f, Dt));
        Assert.Equal(CapPullEvent.Gripped, p.Update(true, true, 0.2f, Dt));
        Assert.Equal(CapPullEvent.None, p.Update(true, false, 0.25f, Dt));
        Assert.Equal(CapPullEvent.Lit, p.Update(true, false, 0.31f, Dt));
        Assert.False(p.Armed);
    }

    [Fact]
    public void GripAwayFromTheEnds_DoesNothing()
    {
        var p = Make();
        p.Update(false, false, 0.2f, Dt);
        Assert.Equal(CapPullEvent.None, p.Update(true, false, 0.2f, Dt));
        Assert.Equal(CapPullEvent.None, p.Update(true, false, 0.5f, Dt));
    }

    [Fact]
    public void GripAlreadyClosedWhenReachingTheEnd_DoesNotArm()
    {
        var p = Make();
        p.Update(true, false, 0.2f, Dt);
        Assert.Equal(CapPullEvent.None, p.Update(true, true, 0.2f, Dt));
        Assert.Equal(CapPullEvent.None, p.Update(true, true, 0.5f, Dt));
    }

    [Fact]
    public void PushingTowardTheFlare_DoesNotLight()
    {
        var p = Make();
        p.Update(false, true, 0.2f, Dt);
        p.Update(true, true, 0.2f, Dt);
        Assert.Equal(CapPullEvent.None, p.Update(true, true, 0.05f, Dt));
    }

    [Fact]
    public void ReleaseAfterAShortPull_IsTooShort()
    {
        var p = Make();
        p.Update(false, true, 0.2f, Dt);
        p.Update(true, true, 0.2f, Dt);
        p.Update(true, false, 0.25f, Dt);
        Assert.Equal(CapPullEvent.TooShort, p.Update(false, false, 0.25f, Dt));
    }

    [Fact]
    public void ReleaseWithoutPulling_IsSilent()
    {
        var p = Make();
        p.Update(false, true, 0.2f, Dt);
        p.Update(true, true, 0.2f, Dt);
        Assert.Equal(CapPullEvent.None, p.Update(false, true, 0.21f, Dt));
    }

    [Fact]
    public void SlowPull_RunsOutOfTime()
    {
        var p = Make();
        p.Update(false, true, 0.2f, Dt);
        p.Update(true, true, 0.2f, Dt);
        var last = CapPullEvent.None;
        float apart = 0.2f;
        for (int i = 0; i < 60 && last == CapPullEvent.None; i++)
        {
            apart += 0.0008f;
            last = p.Update(true, false, apart, Dt);
        }
        Assert.Equal(CapPullEvent.TooShort, last);
        Assert.False(p.Armed);
    }

    [Fact]
    public void Progress_FollowsThePull()
    {
        var p = Make();
        p.Update(false, true, 0.2f, Dt);
        p.Update(true, true, 0.2f, Dt);
        p.Update(true, false, 0.25f, Dt);
        Assert.Equal(0.5f, p.Progress, 3);
    }

    [Fact]
    public void Reset_Disarms()
    {
        var p = Make();
        p.Update(false, true, 0.2f, Dt);
        p.Update(true, true, 0.2f, Dt);
        p.Reset();
        Assert.Equal(CapPullEvent.None, p.Update(true, false, 0.5f, Dt));
    }
}
