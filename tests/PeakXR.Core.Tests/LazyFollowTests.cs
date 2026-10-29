namespace PeakXR.Core.Tests;

public class LazyFollowTests
{
    private const float Frame = 1f / 90f;

    private static LazyFollow At(float yaw, float pitch = 0f)
    {
        var f = new LazyFollow();
        f.Snap(yaw, pitch);
        return f;
    }

    private static void Run(LazyFollow f, float yaw, float pitch, float seconds, bool hold = false)
    {
        for (float t = 0f; t < seconds; t += Frame)
            f.Update(yaw, pitch, hold, Frame);
    }

    [Fact]
    public void LookingAroundInsideTheThreshold_PanelStaysPut()
    {
        var f = At(0f);
        Run(f, 25f, 0f, 5f);
        Assert.Equal(0f, f.Yaw);
        Assert.False(f.Gliding);
    }

    [Fact]
    public void AGlancePastTheThresholdShorterThanTheDwell_PanelStaysPut()
    {
        var f = At(0f);
        Run(f, 60f, 0f, f.Dwell * 0.8f);
        Run(f, 10f, 0f, 1f);
        Run(f, 60f, 0f, f.Dwell * 0.8f);
        Assert.Equal(0f, f.Yaw);
    }

    [Fact]
    public void LookingAwayPastTheDwell_GlidesBackInFrontAndStops()
    {
        var f = At(0f);
        Run(f, 60f, 0f, f.Dwell + 3f);
        TestUtil.Near(60f, f.Yaw, f.Settle);
        Assert.False(f.Gliding);
    }

    [Fact]
    public void TheGlide_StartsGentlyAndNeverOvershoots()
    {
        var f = At(0f);
        Run(f, 60f, 0f, f.Dwell);
        float before = f.Yaw;
        f.Update(60f, 0f, false, Frame);
        Assert.InRange(f.Yaw - before, 0f, 1f);
        float last = f.Yaw;
        for (int i = 0; i < 400; i++)
        {
            f.Update(60f, 0f, false, Frame);
            Assert.InRange(f.Yaw, last, 60f);
            last = f.Yaw;
        }
    }

    [Fact]
    public void HeldByTheLaserOrAPressedControl_NeverStartsAGlide()
    {
        var f = At(0f);
        Run(f, 90f, 0f, 5f, hold: true);
        Assert.Equal(0f, f.Yaw);
        Assert.False(f.Gliding);
    }

    [Fact]
    public void HeldMidGlide_StopsWhereItIs()
    {
        var f = At(0f);
        Run(f, 90f, 0f, f.Dwell + 0.1f);
        Assert.True(f.Gliding);
        float at = f.Yaw;
        Run(f, 90f, 0f, 1f, hold: true);
        Assert.Equal(at, f.Yaw);
        Assert.False(f.Gliding);
    }

    [Fact]
    public void ReleasedAfterAHold_WaitsTheDwellAgain()
    {
        var f = At(0f);
        Run(f, 90f, 0f, 5f, hold: true);
        Run(f, 90f, 0f, f.Dwell * 0.8f);
        Assert.Equal(0f, f.Yaw);
    }

    [Fact]
    public void FollowsTheShortWayAcrossTheBackOfTheCircle()
    {
        var f = At(170f);
        Run(f, -150f, 0f, f.Dwell + 3f);
        TestUtil.Near(0f, LookMath.WrapDegrees(f.Yaw - -150f), f.Settle);
    }

    [Fact]
    public void PitchCountsTowardTheThresholdAndFollows()
    {
        var f = At(0f, 0f);
        Run(f, 0f, 45f, f.Dwell + 3f);
        TestUtil.Near(45f, f.Pitch, f.Settle);
        Assert.Equal(0f, f.Yaw, 3);
    }

    [Fact]
    public void WidePanels_WidenTheThresholdPastTheirEdge()
    {
        Assert.Equal(30f, LazyFollow.ThresholdFor(30f, 10f));
        Assert.Equal(29f + LazyFollow.EdgeMargin, LazyFollow.ThresholdFor(30f, 29f));
    }

    [Fact]
    public void OpenedTurnedTowardOpenSpace_LookingAtTheWallDoesNotPullItBack()
    {
        // The pause menu opened 45 degrees off the head because of a wall at the face: the head still facing that
        // wall is where it was placed from, not a look away.
        var f = new LazyFollow();
        f.Snap(45f, 0f, offset: 45f);
        Run(f, 0f, 0f, 5f);
        Assert.Equal(45f, f.Yaw);
        Assert.False(f.Gliding);
    }

    [Fact]
    public void WithAnOffset_GlidesToTheHeadPlusTheOffset()
    {
        var f = new LazyFollow();
        f.Snap(30f, 0f, offset: 30f);
        Run(f, 90f, 0f, f.Dwell + 3f);
        TestUtil.Near(120f, f.Yaw, f.Settle);
    }

    [Fact]
    public void Started_OnlyOnTheFrameAGlideBegins()
    {
        var f = At(0f);
        int starts = 0;
        for (float t = 0f; t < f.Dwell + 3f; t += Frame)
        {
            f.Update(90f, 0f, false, Frame);
            if (f.Started)
            {
                starts++;
                Assert.True(f.Gliding);
            }
        }
        Assert.Equal(1, starts);
    }

    [Fact]
    public void AnOffsetChosenAtTheStart_RetargetsTheGlide()
    {
        var f = At(0f);
        for (float t = 0f; t < f.Dwell + 3f; t += Frame)
        {
            f.Update(90f, 0f, false, Frame);
            if (f.Started)
                f.Offset = -30f;
        }
        TestUtil.Near(60f, f.Yaw, f.Settle);
    }

    [Fact]
    public void Turn_GlidesAtOnceTowardTheNewOffset()
    {
        var f = At(0f);
        f.Turn(40f);
        Assert.True(f.Gliding);
        Assert.True(f.Update(0f, 0f, false, Frame));
        Run(f, 0f, 0f, 3f);
        TestUtil.Near(40f, f.Yaw, f.Settle);
        Assert.False(f.Gliding);
        Assert.Equal(40f, f.Offset);
    }

    [Fact]
    public void Snap_ClearsAnOldOffset()
    {
        var f = new LazyFollow();
        f.Snap(45f, 0f, offset: 45f);
        f.Snap(10f);
        Assert.Equal(0f, f.Offset);
    }
}
