namespace PeakXR.Core.Tests;

public class FollowDistanceTests
{
    private const float Wanted = 1.75f;
    private const float Test = 0.1f;
    private const float Frame = 1f / 72f;
    private const float Open = float.PositiveInfinity;

    private static FollowDistance At(float distance)
    {
        var d = new FollowDistance();
        d.Snap(distance, onTop: false);
        return d;
    }

    /// <summary>Wall tests every 0.1 s with frames stepped between, the way a gliding menu runs them.</summary>
    private static void Glide(FollowDistance d, float wall, float seconds)
    {
        for (float t = 0f; t < seconds - 1e-4f; t += Test)
        {
            d.Test(Wanted, wall, Test, settled: false);
            for (float f = 0f; f < Test - 1e-4f; f += Frame)
                d.Step(Frame);
        }
    }

    [Fact]
    public void AWallAhead_PullsItInSmoothly_NotInOneJump()
    {
        var d = At(Wanted);
        d.Test(Wanted, 1.0f, Test, settled: false);
        Assert.Equal(1.0f - PanelMath.WallGap, d.Target, 3);
        d.Step(Frame);
        Assert.InRange(d.Distance, 1.5f, Wanted - 0.01f);
        float last = d.Distance;
        for (int i = 0; i < 200; i++)
        {
            d.Step(Frame);
            Assert.InRange(d.Distance, d.Target, last);
            last = d.Distance;
        }
        TestUtil.Near(d.Target, d.Distance, 0.002f);
        Assert.False(d.Easing);
    }

    [Fact]
    public void AtAWallEdge_TheFlippingTestDoesNotFlipTheDistance()
    {
        // The 10 Hz test at the edge of a wall saw it, then not, then saw it (trace: 0.60 m, 1.75 m, 0.60 m).
        var d = At(Wanted);
        float lowest = Wanted;
        for (int i = 0; i < 40; i++)
        {
            Glide(d, i % 2 == 0 ? 0.4f : Open, Test);
            Assert.True(
                d.Distance <= lowest + 1e-5f,
                $"moved back out to {d.Distance} at test {i}"
            );
            lowest = d.Distance;
        }
        Assert.Equal(PanelMath.NearestMenuDistance, d.Target, 3);
    }

    [Fact]
    public void TheWallClearingForAWhile_EasesItBackOut()
    {
        var d = At(Wanted);
        Glide(d, 0.4f, 2f);
        Assert.Equal(PanelMath.NearestMenuDistance, d.Distance, 2);
        Glide(d, Open, FollowDistance.OutSeconds * 0.5f);
        Assert.Equal(PanelMath.NearestMenuDistance, d.Target, 3);
        Glide(d, Open, FollowDistance.OutSeconds + Test);
        Assert.Equal(Wanted, d.Target, 3);
        Assert.True(d.Distance < Wanted - 0.1f, "jumped back out");
        Glide(d, Open, 3f);
        Assert.Equal(Wanted, d.Distance, 2);
    }

    [Fact]
    public void ASmallGainMidGlide_DoesNotMoveItOut()
    {
        var d = At(Wanted);
        Glide(d, 1.0f, 2f);
        Glide(d, 1.0f + FollowDistance.OutMargin * 0.5f, 3f);
        Assert.Equal(1.0f - PanelMath.WallGap, d.Target, 3);
    }

    [Fact]
    public void TheSettlingTest_TakesTheFitAtOnce()
    {
        // No more tests come once the glide settles: a wall left behind must not keep it pulled in.
        var d = At(Wanted);
        Glide(d, 0.4f, 1f);
        Assert.True(d.OnTop);
        d.Test(Wanted, Open, Test, settled: true);
        Assert.Equal(Wanted, d.Target, 3);
        Assert.False(d.OnTop);
        Assert.True(d.Easing);
    }

    [Fact]
    public void OnTop_WhileThePanelStandsPastTheWall_HeldAtTheEdge()
    {
        var d = At(Wanted);
        // Pulled in from 1.75 m by a wall at 1.0 m: on top until it has eased in front of the wall.
        d.Test(Wanted, 1.0f, Test, settled: false);
        Assert.True(d.OnTop);
        Glide(d, 1.0f, 3f);
        Assert.False(d.OnTop);
        // Held at the 0.6 m minimum by a wall at 0.4 m: on top, and a flickering test keeps it on.
        Glide(d, 0.4f, 1f);
        Assert.True(d.OnTop);
        for (int i = 0; i < 20; i++)
        {
            Glide(d, i % 2 == 0 ? Open : 0.4f, Test);
            Assert.True(d.OnTop);
        }
    }

    [Fact]
    public void Snap_PlacesItWithNothingCarriedOver()
    {
        var d = At(Wanted);
        Glide(d, 0.4f, 0.2f);
        d.Snap(1.2f, onTop: true);
        Assert.Equal(1.2f, d.Distance);
        Assert.Equal(1.2f, d.Target);
        Assert.True(d.OnTop);
        Assert.False(d.Easing);
    }

    [Fact]
    public void HasRoom_MatchesTheOpeningRule()
    {
        Assert.True(PanelMath.HasRoom(Open));
        Assert.True(PanelMath.HasRoom(PanelMath.NearestMenuDistance + PanelMath.WallGap));
        Assert.False(PanelMath.HasRoom(0.4f));
        Assert.Equal(1, PanelMath.PickOpenYaw(new[] { 0.4f, Open, 0.5f }));
    }
}
