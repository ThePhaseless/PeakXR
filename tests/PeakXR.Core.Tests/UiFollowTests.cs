namespace PeakXR.Core.Tests;

public class LazyYawFollowTests
{
    static LazyYawFollow Make() =>
        new LazyYawFollow
        {
            Threshold = 30f,
            Settle = 2f,
            Rate = 1000f,
        };

    [Fact]
    public void FirstUpdate_SnapsToTarget()
    {
        var f = Make();
        Assert.Equal(40f, f.Update(40f, 0.1f));
    }

    [Fact]
    public void InsideThreshold_StaysPut()
    {
        var f = Make();
        f.Update(0f, 0.1f);
        Assert.Equal(0f, f.Update(25f, 0.1f));
        Assert.False(f.Catching);
    }

    [Fact]
    public void BeyondThreshold_CatchesUpAndSettles()
    {
        var f = Make();
        f.Update(0f, 0.1f);
        TestUtil.Near(50f, f.Update(50f, 0.1f), 1e-3f);
        Assert.False(f.Catching);
    }

    [Fact]
    public void CatchUp_IsGradualAtLowRate()
    {
        var f = Make();
        f.Rate = 1f;
        f.Update(0f, 0.1f);
        float y = f.Update(60f, 0.1f);
        Assert.InRange(y, 1f, 10f);
        Assert.True(f.Catching);
    }

    [Fact]
    public void KeepsCatchingBelowThresholdUntilSettled()
    {
        var f = Make();
        f.Rate = 5f;
        f.Update(0f, 0.1f);
        f.Update(40f, 0.1f);
        float y = f.Update(40f, 0.1f);
        Assert.True(y > 10f && y < 40f);
        Assert.True(f.Catching);
    }

    [Fact]
    public void TakesShortestWayAcrossWrap()
    {
        var f = Make();
        f.Update(170f, 0.1f);
        float y = f.Update(-150f, 0.1f);
        TestUtil.Near(-150f, y, 1e-3f);
    }

    [Fact]
    public void Snap_ResetsCatching()
    {
        var f = Make();
        f.Rate = 1f;
        f.Update(0f, 0.1f);
        f.Update(90f, 0.1f);
        f.Snap(10f);
        Assert.False(f.Catching);
        Assert.Equal(10f, f.Yaw);
    }
}

public class FadeEaserTests
{
    [Fact]
    public void ReachesTargetAfterDuration()
    {
        var e = new FadeEaser { Seconds = 0.5f };
        e.Update(1f, 0.25f);
        Assert.True(e.Alpha > 0f && e.Alpha < 1f);
        e.Update(1f, 0.25f);
        TestUtil.Near(1f, e.Alpha);
    }

    [Fact]
    public void EasesInAndOut()
    {
        var e = new FadeEaser { Seconds = 1f };
        e.Update(1f, 0.1f);
        Assert.True(e.Alpha < 0.1f, $"slow start, got {e.Alpha}");
        e.Update(1f, 0.4f);
        TestUtil.Near(0.5f, e.Alpha, 1e-3f);
    }

    [Fact]
    public void FadesBackDown()
    {
        var e = new FadeEaser { Seconds = 0.2f };
        e.Update(1f, 1f);
        e.Update(0f, 1f);
        Assert.Equal(0f, e.Alpha);
    }

    [Fact]
    public void ZeroSecondsIsInstant()
    {
        var e = new FadeEaser { Seconds = 0f };
        Assert.Equal(1f, e.Update(1f, 0.001f));
    }

    [Fact]
    public void Snap_JumpsThenEasesFromThere()
    {
        var e = new FadeEaser { Seconds = 1f };
        e.Snap(1f);
        Assert.Equal(1f, e.Alpha);
        e.Update(0f, 0.5f);
        TestUtil.Near(0.5f, e.Alpha, 1e-3f);
    }

    [Fact]
    public void TargetIsClamped()
    {
        var e = new FadeEaser { Seconds = 0f };
        Assert.Equal(1f, e.Update(3f, 0.1f));
        Assert.Equal(0f, e.Update(-1f, 0.1f));
    }
}

public class PanelMathTests
{
    [Fact]
    public void YawOf_UsesUnityConvention()
    {
        TestUtil.Near(0f, PanelMath.YawOf(new Vector3(0, 0, 1), new Vector3(0, 1, 0)));
        TestUtil.Near(90f, PanelMath.YawOf(new Vector3(1, 0, 0), new Vector3(0, 1, 0)));
        TestUtil.Near(-90f, PanelMath.YawOf(new Vector3(-1, 0.5f, 0), new Vector3(0, 1, 0)));
    }

    [Fact]
    public void YawOf_LookingStraightDown_FallsBackToHeadUp()
    {
        // Looking at the floor: head up points where the face points.
        TestUtil.Near(90f, PanelMath.YawOf(new Vector3(0, -1, 0), new Vector3(1, 0, 0)));
    }

    [Fact]
    public void InFront_LevelPlacement()
    {
        TestUtil.Near(new Vector3(0, 0, 2), PanelMath.InFront(0f, 2f, 0f));
        TestUtil.Near(new Vector3(2, 0, 0), PanelMath.InFront(90f, 2f, 0f));
    }

    [Fact]
    public void InFront_TiltDropsPanelBelowEyes()
    {
        var p = PanelMath.InFront(0f, 2f, 30f);
        TestUtil.Near(new Vector3(0, -1f, MathF.Sqrt(3f)), p, 1e-4f);
    }

    [Fact]
    public void MetersPerPixel_FitsWidth()
    {
        TestUtil.Near(0.001f, PanelMath.MetersPerPixel(1.92f, 1920f), 1e-7f);
        Assert.Equal(0.001f, PanelMath.MetersPerPixel(1f, 0f));
    }
}
