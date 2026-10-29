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
}
