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
    public void ClearDistance_NoWall_KeepsWantedDistance()
    {
        Assert.Equal(1.8f, PanelMath.ClearDistance(1.8f, float.PositiveInfinity));
        Assert.Equal(1.8f, PanelMath.ClearDistance(1.8f, 2.5f));
    }

    [Fact]
    public void ClearDistance_WallCloser_StopsInFrontOfIt()
    {
        TestUtil.Near(1.0f - PanelMath.WallGap, PanelMath.ClearDistance(1.8f, 1.0f));
    }

    [Fact]
    public void EdgeDepth_ProjectsAnEdgeRayHitOntoTheViewLine()
    {
        // Edge ray to (0.8, 0, 1.8): a hit 0.985 m along it lies 0.9 m ahead.
        var length = MathF.Sqrt(1.8f * 1.8f + 0.8f * 0.8f);
        TestUtil.Near(0.9f, PanelMath.EdgeDepth(length / 2f, 1.8f, 0.8f));
        Assert.Equal(1.8f, PanelMath.EdgeDepth(1.8f, 1.8f, 0f));
        Assert.True(
            float.IsPositiveInfinity(PanelMath.EdgeDepth(float.PositiveInfinity, 1.8f, 0.8f))
        );
    }

    [Fact]
    public void PickOpenYaw_KeepsStraightAheadWhenItFits()
    {
        Assert.Equal(0, PanelMath.PickOpenYaw(new[] { 1.0f, 3f, 3f }));
        Assert.Equal(0, PanelMath.PickOpenYaw(new[] { float.PositiveInfinity, 0.2f }));
    }

    [Fact]
    public void PickOpenYaw_WallAtTheFace_TakesTheFirstOpenTurn()
    {
        // Ahead and the first turn are blocked within 0.7 m; the second turn has room.
        Assert.Equal(2, PanelMath.PickOpenYaw(new[] { 0.3f, 0.5f, 0.9f, 3f }));
    }

    [Fact]
    public void PickOpenYaw_BoxedIn_TakesTheMostOpen()
    {
        Assert.Equal(1, PanelMath.PickOpenYaw(new[] { 0.3f, 0.65f, 0.4f }));
    }

    [Fact]
    public void ClearDistance_WallAtTheFace_KeepsNearestReadableDistance()
    {
        Assert.Equal(PanelMath.NearestMenuDistance, PanelMath.ClearDistance(1.8f, 0.2f));
        // A wanted distance already nearer than the floor is never pushed out.
        Assert.Equal(0.4f, PanelMath.ClearDistance(0.4f, 0.1f));
    }

    [Fact]
    public void MetersPerPixel_FitsWidth()
    {
        TestUtil.Near(0.001f, PanelMath.MetersPerPixel(1.92f, 1920f), 1e-7f);
        Assert.Equal(0.001f, PanelMath.MetersPerPixel(1f, 0f));
    }

    [Fact]
    public void MenuScale_FullScreenContentKeepsMenuWidth()
    {
        // Content spanning the whole 1920x1080 canvas sizes exactly like the plain width fit.
        TestUtil.Near(
            PanelMath.MetersPerPixel(1.6f, 1920f),
            PanelMath.MenuMetersPerPixel(1920f, 1080f, 1.6f, 1.8f),
            1e-7f
        );
    }

    [Fact]
    public void MenuScale_SmallContentGrowsUpToTextCap()
    {
        // The pause menu (logo and five buttons, about 400x540 px) stops at the angular cap.
        float s = PanelMath.MenuMetersPerPixel(400f, 540f, 1.6f, 1.8f);
        TestUtil.Near(1.8f * MathF.Tan(PanelMath.MaxMenuDegreesPerPixel / 57.29578f), s, 1e-6f);
        Assert.True(s > 2.5f * PanelMath.MetersPerPixel(1.6f, 1920f));
        // 20 px body text then spans 1.4 degrees.
        TestUtil.Near(1.4f, 2f * MathF.Atan(10f * s / 1.8f) * 57.29578f, 0.01f);
    }

    [Fact]
    public void MenuScale_TallContentFitsHeight()
    {
        float s = PanelMath.MenuMetersPerPixel(600f, 1000f, 1.6f, 3f);
        TestUtil.Near(1.6f * PanelMath.MenuHeightRatio / 1000f, s, 1e-7f);
    }

    [Fact]
    public void MenuScale_EmptyContentFallsBackToWidthFit()
    {
        Assert.True(PanelMath.MenuMetersPerPixel(0f, 0f, 1.6f, 1.8f) > 0f);
    }
}
