namespace PeakXR.Core.Tests;

public class SegmentSwapGateTests
{
    [Fact]
    public void FadesWhenTheFogCatchesUpAndNotAgainAtTheSwap()
    {
        var g = new SegmentSwapGate();
        g.Arm(0f);
        Assert.True(g.FogCaughtUp());
        Assert.False(g.FogCaughtUp());
        Assert.False(g.Swap());
        Assert.False(g.Armed);
    }

    [Fact]
    public void SwapWithoutTheFogSignalSnapsToBlack()
    {
        var g = new SegmentSwapGate();
        g.Arm(0f);
        Assert.True(g.Swap());
        Assert.False(g.Armed);
    }

    [Fact]
    public void LongFogWaitNeverDisarmsBeforeTheTimeout()
    {
        var g = new SegmentSwapGate { Timeout = 600f };
        g.Arm(0f);
        Assert.False(g.Expired(300f));
        Assert.True(g.FogCaughtUp());
    }

    [Fact]
    public void NoSwapEverComingDisarmsOnce()
    {
        var g = new SegmentSwapGate { Timeout = 600f };
        g.Arm(0f);
        Assert.True(g.Expired(601f));
        Assert.False(g.Expired(700f));
        Assert.False(g.Swap());
    }

    [Fact]
    public void UnarmedIgnoresEverything()
    {
        var g = new SegmentSwapGate();
        Assert.False(g.FogCaughtUp());
        Assert.False(g.Swap());
        Assert.False(g.Expired(1000f));
    }

    [Fact]
    public void ResetDisarms()
    {
        var g = new SegmentSwapGate();
        g.Arm(0f);
        g.Reset();
        Assert.False(g.Swap());
    }
}
