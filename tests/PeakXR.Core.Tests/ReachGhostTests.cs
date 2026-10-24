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
    public void JitterAtTheThresholdDoesNotFlicker()
    {
        // Shown once past 0.05 m, it stays until the gap drops under the exit (0.03 m): no on-off at 0.05.
        var f = new GhostFade();
        Settle(f, 0.1f);
        for (int i = 0; i < 50; i++)
        {
            f.Update(i % 2 == 0 ? 0.045f : 0.055f, 0.02f);
            Assert.True(f.Visible);
        }
        Settle(f, 0f);
        Assert.False(f.Visible);
    }

    [Fact]
    public void FadesRatherThanPops()
    {
        var f = new GhostFade();
        f.Update(0.5f, 0.02f);
        Assert.InRange(f.Alpha, 0.01f, 0.5f);
    }
}
