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
    public void NegativeRate_NeverTurnsAwayFromTheHead()
    {
        var f = Make();
        f.Rate = -4f;
        f.Update(0f, 0.1f);
        Assert.Equal(90f, f.Update(90f, 0.1f));
    }

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
    public void FitBeforeWall_NoWall_KeepsWantedDistanceWithDepth()
    {
        Assert.Equal((1.8f, false), PanelMath.FitBeforeWall(1.8f, float.PositiveInfinity));
        Assert.Equal((1.8f, false), PanelMath.FitBeforeWall(1.8f, 2.5f));
    }

    [Fact]
    public void FitBeforeWall_WallCloser_StopsInFrontOfItWithDepth()
    {
        var (distance, onTop) = PanelMath.FitBeforeWall(1.8f, 1.0f);
        TestUtil.Near(1.0f - PanelMath.WallGap, distance);
        Assert.False(onTop);
        // Just outside the floor: still pulled in, still depth tested.
        (distance, onTop) = PanelMath.FitBeforeWall(1.8f, PanelMath.ComfortFloor + 0.12f);
        TestUtil.Near(PanelMath.ComfortFloor + 0.02f, distance);
        Assert.False(onTop);
    }

    [Fact]
    public void FitBeforeWall_WallInsideTheFloor_HoldsTheFloorAndDrawsOnTop()
    {
        Assert.Equal((PanelMath.ComfortFloor, true), PanelMath.FitBeforeWall(1.8f, 0.2f));
        // Menus pass their own, higher floor.
        Assert.Equal(
            (PanelMath.NearestMenuDistance, true),
            PanelMath.FitBeforeWall(1.8f, 0.2f, PanelMath.NearestMenuDistance)
        );
    }

    [Fact]
    public void FitBeforeWall_WantedNearerThanTheFloor_IsNeverPushedOut()
    {
        Assert.Equal((0.4f, true), PanelMath.FitBeforeWall(0.4f, 0.1f));
        Assert.Equal((0.4f, false), PanelMath.FitBeforeWall(0.4f, 1f));
    }

    [Fact]
    public void FitBeforeWall_CustomMargin_KeepsThatGap()
    {
        var (distance, onTop) = PanelMath.FitBeforeWall(30f, 10f, margin: 1f);
        TestUtil.Near(9f, distance);
        Assert.False(onTop);
    }

    [Fact]
    public void DepthAlong_ProjectsASampleRayHitOntoTheCentreLine()
    {
        // Edge ray to (0.8, 0, 1.8): a hit halfway along it lies 0.9 m ahead.
        var length = MathF.Sqrt(1.8f * 1.8f + 0.8f * 0.8f);
        TestUtil.Near(0.9f, PanelMath.DepthAlong(length / 2f, 1.8f, length));
        Assert.Equal(1.8f, PanelMath.DepthAlong(1.8f, 1.8f, 1.8f));
        Assert.True(
            float.IsPositiveInfinity(PanelMath.DepthAlong(float.PositiveInfinity, 1.8f, length))
        );
    }

    [Fact]
    public void PanelSample_CoversCornersEdgesAndCentre()
    {
        var seen = new System.Collections.Generic.List<Vector2>();
        for (int i = 0; i < PanelMath.PanelSamples; i++)
        {
            var o = PanelMath.PanelSample(i, 0.8f, 0.5f);
            Assert.InRange(MathF.Abs(o.X), 0f, 0.8f + 1e-5f);
            Assert.InRange(MathF.Abs(o.Y), 0f, 0.5f + 1e-5f);
            seen.Add(o);
        }
        // All four corners (a corner cut by a curved wall), both side edges and the centre.
        foreach (
            var want in new[]
            {
                new Vector2(-0.8f, -0.5f),
                new Vector2(0.8f, -0.5f),
                new Vector2(-0.8f, 0.5f),
                new Vector2(0.8f, 0.5f),
                new Vector2(-0.8f, 0f),
                new Vector2(0.8f, 0f),
                Vector2.Zero,
            }
        )
            Assert.Contains(seen, o => Vector2.Distance(o, want) < 1e-4f);
    }

    [Fact]
    public void PanelSample_IsDenseEnoughForAPillarBetweenSamples()
    {
        // Columns no further apart than a quarter of the width, rows a half of the height.
        Assert.True(PanelMath.PanelSamples >= 15);
        var a = PanelMath.PanelSample(0, 1f, 1f);
        var b = PanelMath.PanelSample(1, 1f, 1f);
        Assert.True(Vector2.Distance(a, b) <= 0.5f + 1e-5f);
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

public class WallOnTopHoldTests
{
    const float Wanted = 1.2f;
    const float Dt = 0.1f;

    /// <summary>One wall test the way HudLayout runs it: the wall depth in, the held flag out.</summary>
    static bool Test(WallOnTopHold h, float wall, float seconds)
    {
        bool onTop = false;
        for (float t = 0f; t < seconds - 1e-4f; t += Dt)
            onTop = h.Update(Wanted, wall, Dt);
        return onTop;
    }

    [Fact]
    public void OnAtOnce_OffOnlyAfterAClearDwell()
    {
        var h = new WallOnTopHold();
        // Rock inside the comfort floor: drawn on top at once.
        Assert.True(Test(h, 0.4f, Dt));
        // Right at the floor's margin: held on (no flicker at the edge).
        Assert.True(Test(h, PanelMath.ComfortFloor + PanelMath.WallGap, 1f));
        // Clearly clear but only for 0.2 s, then back in: still on.
        Assert.True(Test(h, 0.9f, 0.2f));
        Assert.True(Test(h, 0.4f, Dt));
        // A wall at 0.9 m pulls the HUD in to 0.8 m, depth tested: it turns off after the dwell.
        Assert.True(Test(h, 0.9f, 0.2f));
        Assert.False(Test(h, 0.9f, 0.2f));
    }

    [Fact]
    public void AClimbWallBetweenTheFloorAndTheWantedDistance_DoesNotKeepItOnTop()
    {
        var h = new WallOnTopHold();
        Assert.True(Test(h, 0.55f, Dt));
        Assert.True(Test(h, 0.6f, 0.5f));
        Assert.False(Test(h, 0.9f, 0.5f));
        Assert.False(Test(h, 1.0f, 2f));
    }
}

public class SignDwellTests
{
    [Fact]
    public void Glances_DoNotReplace_ALongLookAwayDoes()
    {
        var d = new SignDwell();
        Assert.False(d.Update(40f, false, 0.5f));
        Assert.False(d.Update(40f, false, 5f));
        Assert.False(d.Update(80f, false, 0.6f));
        Assert.False(d.Update(30f, false, 0.1f));
        Assert.False(d.Update(80f, false, 0.6f));
        Assert.True(d.Update(80f, false, 0.5f));
        d.Reset();
        Assert.True(d.Update(0f, true, 0.01f));
    }

    [Fact]
    public void HalfAHeadsetFovAway_ForTheDwell_Replaces()
    {
        // 50 degrees off is outside half of a 90-100 degree view: the sign is out of sight.
        var d = new SignDwell();
        Assert.False(d.Update(50f, false, 0.6f));
        Assert.True(d.Update(50f, false, 0.5f));
    }
}
