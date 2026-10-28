namespace PeakXR.Core.Tests;

public class ReachGhostTests
{
    private static float Settle(GhostFade f, float gap, float seconds = 2f)
    {
        for (float t = 0f; t < seconds; t += 0.02f)
            f.Update(gap, 0.02f);
        return f.Alpha;
    }

    [Fact]
    public void AHandOnItsControllerShowsNoGhost() =>
        Assert.Equal(0f, Settle(new GhostFade(), 0.03f));

    [Fact]
    public void FadesInFromAboutFiveCentimetresAndIsFullByFifteen()
    {
        Assert.InRange(Settle(new GhostFade(), 0.08f), 0.05f, 0.9f);
        Assert.Equal(1f, Settle(new GhostFade(), 0.2f), 3);
    }

    [Fact]
    public void JitterAtTheThresholdOnlyShimmersFaintly()
    {
        // Around 0.05 m the ghost is all but transparent and eases: no pop, however the gap jitters.
        var f = new GhostFade();
        Settle(f, 0.1f);
        float last = f.Alpha;
        for (int i = 0; i < 50; i++)
        {
            f.Update(i % 2 == 0 ? 0.045f : 0.055f, 0.02f);
            // Down from the 0.1 m level first, at its eased rate.
            if (i >= 10)
                Assert.True(f.Alpha <= 0.1f);
            Assert.True(System.MathF.Abs(f.Alpha - last) <= GhostFade.Rate * 0.02f + 1e-4f);
            last = f.Alpha;
        }
        Settle(f, 0f);
        Assert.False(f.Visible);
    }

    [Fact]
    public void AStraightArmAtRestHidesTheGhost()
    {
        // vr70 vr.173: a hand hanging at rest on a straight arm reads about 4 cm short; the ghost must fade out
        // there, not linger faintly.
        var f = new GhostFade();
        Settle(f, 0.5f);
        Assert.Equal(0f, Settle(f, 0.04f, 0.5f));
    }

    [Fact]
    public void AShortfallRightAfterHidingWaitsItsPersistAgain()
    {
        // Shown, hidden for one frame, then short again: the new shortfall must last Persist before it shows,
        // or the ghost flickers back at once.
        var f = new GhostFade();
        Settle(f, 0.2f);
        f.Update(0f, 0.02f);
        float hidden = f.Alpha;
        for (int i = 0; i < 5; i++)
            f.Update(0.2f, 0.02f);
        Assert.True(f.Alpha < hidden, $"{f.Alpha} >= {hidden}");
    }

    [Fact]
    public void FadesRatherThanPops()
    {
        var f = new GhostFade();
        for (int i = 0; i < 10; i++)
            f.Update(0.5f, 0.02f);
        Assert.InRange(f.Alpha, 0.01f, 0.5f);
    }

    [Fact]
    public void ABriefShortfallShowsNothing()
    {
        // A shortfall must last about 0.15 s before the ghost fades in (a reach that only touches the limit).
        var f = new GhostFade();
        for (int i = 0; i < 6; i++)
            f.Update(0.3f, 0.02f);
        Assert.False(f.Visible);
        for (int i = 0; i < 6; i++)
            f.Update(0f, 0.02f);
        Assert.False(f.Visible);
    }
}

public class ReachShortfallTests
{
    private static readonly System.Numerics.Vector3 Shoulder = new(0.2f, 1.4f, 0f);

    [Fact]
    public void ABodyLaggingTheHeadDoesNotCountAsReach()
    {
        // vr72 vr.173: after a warp and recenter the body sat behind the tracked head; a resting hand read 11 cm short.
        var lag = new System.Numerics.Vector3(0f, 0f, -0.15f);
        var hand = Shoulder + new System.Numerics.Vector3(0f, 0f, 0.6f);
        Assert.True(WeldGate.Shortfall(Shoulder + lag, hand, 0.6f) > 0.05f);
        Assert.Equal(0f, GhostFade.Shortfall(Shoulder + lag, -lag, hand, 0.6f), 3);
    }

    [Fact]
    public void AHandPastTheArmUnderTheHeadStillCounts()
    {
        var hand = Shoulder + new System.Numerics.Vector3(0f, -0.9f, 0f);
        Assert.Equal(
            0.3f,
            GhostFade.Shortfall(Shoulder, System.Numerics.Vector3.Zero, hand, 0.6f),
            3
        );
    }
}
