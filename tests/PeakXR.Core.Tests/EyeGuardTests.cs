namespace PeakXR.Core.Tests;

public class EyeGuardTests
{
    static EyeGuard Make() =>
        new()
        {
            WatchSeconds = 3f,
            ClearSeconds = 0.2f,
            MaxHoldSeconds = 5f,
        };

    /// <summary>Runs <paramref name="seconds"/> of frames; true when any frame asked for black.</summary>
    static bool Run(EyeGuard g, bool inside, float seconds, float dt = 0.01f)
    {
        bool black = false;
        for (float s = 0f; s < seconds - 1e-6f; s += dt)
            black |= g.Update(dt, inside);
        return black;
    }

    [Fact]
    public void Unarmed_InsideStaysClear()
    {
        var g = Make();
        Assert.False(Run(g, true, 1f));
        Assert.False(g.Watching);
    }

    [Fact]
    public void Armed_InsideGoesBlack()
    {
        var g = Make();
        g.Arm();
        Assert.True(g.Watching);
        Assert.True(g.Update(0.01f, true));
    }

    [Fact]
    public void Armed_ClearEye_NeverBlack_WatchEnds()
    {
        var g = Make();
        g.Arm();
        Assert.False(Run(g, false, 3.1f));
        Assert.False(g.Watching);
    }

    [Fact]
    public void Black_HoldsUntilClearForClearSeconds()
    {
        var g = Make();
        g.Arm();
        Run(g, true, 0.5f);
        Assert.True(g.Update(0.1f, false));
        Assert.True(g.Update(0.05f, false));
        Assert.False(g.Update(0.1f, false));
    }

    [Fact]
    public void Black_OutlastsTheWatchWindow()
    {
        var g = Make();
        g.Arm();
        Run(g, false, 2.9f);
        Run(g, true, 0.5f);
        Assert.True(g.Watching);
        Assert.True(g.Update(0.01f, true));
    }

    [Fact]
    public void StuckInside_GivesUpAfterMaxHold()
    {
        var g = Make();
        g.Arm();
        Run(g, true, 5.05f);
        Assert.False(g.Update(0.01f, true));
        Assert.False(g.Watching);
    }

    [Fact]
    public void Rearm_RestartsTheWindow()
    {
        var g = Make();
        g.Arm();
        Run(g, false, 2.5f);
        g.Arm();
        Run(g, false, 2.5f);
        Assert.True(g.Watching);
    }
}
