namespace PeakXR.Core.Tests;

public class PutAwayWindowTests
{
    [Fact]
    public void Idle_HoldsNothingBack()
    {
        Assert.False(new PutAwayWindow().Pending(0f));
    }

    [Fact]
    public void APutAway_HoldsEquipsBack_UntilItsOwnEquipSlotRpcLands()
    {
        var w = new PutAwayWindow();
        w.Begin(10f, PutAwayWindow.NoSlot);
        Assert.True(w.Pending(10.1f));
        // Another slot's RPC (someone else's equip) is not the put-away's.
        w.Landed(2);
        Assert.True(w.Pending(10.12f));
        w.Landed(PutAwayWindow.NoSlot);
        Assert.False(w.Pending(10.13f));
    }

    [Fact]
    public void ALostRpc_StopsHoldingBackAfterTheDeadline()
    {
        var w = new PutAwayWindow();
        w.Begin(10f, 1);
        Assert.True(w.Pending(10f + PutAwayWindow.Delay));
        Assert.False(w.Pending(10f + PutAwayWindow.Delay + PutAwayWindow.Slack + 0.01f));
    }

    [Fact]
    public void ASecondPutAway_RestartsTheWindowForItsSlot()
    {
        var w = new PutAwayWindow();
        w.Begin(10f, PutAwayWindow.NoSlot);
        w.Begin(10.1f, 3);
        w.Landed(PutAwayWindow.NoSlot);
        Assert.True(w.Pending(10.2f));
        w.Landed(3);
        Assert.False(w.Pending(10.2f));
    }
}
