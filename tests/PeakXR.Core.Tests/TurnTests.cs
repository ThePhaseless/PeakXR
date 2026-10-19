namespace PeakXR.Core.Tests;

public class TurnTests
{
    [Fact]
    public void Snap_FiresOnceWhenCrossingActivate()
    {
        var s = new SnapTurn();
        Assert.Equal(0f, s.Update(0.5f, 0.01f));
        Assert.Equal(30f, s.Update(0.8f, 0.01f));
        Assert.Equal(0f, s.Update(0.9f, 0.01f));
        Assert.Equal(0f, s.Update(1f, 5f));
    }

    [Fact]
    public void Snap_LeftIsNegative()
    {
        var s = new SnapTurn { Angle = 45f };
        Assert.Equal(-45f, s.Update(-1f, 0.01f));
    }

    [Fact]
    public void Snap_RearmsOnlyBelowReleaseThreshold()
    {
        var s = new SnapTurn();
        s.Update(1f, 0.01f);
        Assert.Equal(0f, s.Update(0.4f, 0.01f));
        Assert.Equal(0f, s.Update(1f, 0.01f));
        s.Update(0.1f, 0.01f);
        Assert.Equal(30f, s.Update(1f, 0.01f));
    }

    [Fact]
    public void Snap_RepeatsWhileHeldWhenDelaySet()
    {
        var s = new SnapTurn { RepeatDelay = 0.5f };
        Assert.Equal(30f, s.Update(1f, 0.1f));
        float total = 0f;
        for (int i = 0; i < 4; i++)
            total += s.Update(1f, 0.1f);
        Assert.Equal(0f, total);
        Assert.Equal(30f, s.Update(1f, 0.1f));
    }

    [Fact]
    public void Snap_ResetRearms()
    {
        var s = new SnapTurn();
        s.Update(1f, 0.01f);
        s.Reset();
        Assert.Equal(30f, s.Update(1f, 0.01f));
    }

    [Fact]
    public void Smooth_IsSpeedTimesAxisAfterDeadzone()
    {
        Assert.Equal(0f, SmoothTurn.Delta(0.1f, 120f, 0.1f, 0.15f));
        TestUtil.Near(12f, SmoothTurn.Delta(1f, 120f, 0.1f, 0.15f), 1e-4f);
        TestUtil.Near(-6f, SmoothTurn.Delta(-0.575f, 120f, 0.1f, 0.15f), 1e-3f);
    }
}
