namespace PeakXR.Core.Tests;

public class PointerHandsTests
{
    [Fact]
    public void DefaultsToRightHand()
    {
        var p = new PointerHands();
        p.Update(0f, 0f, 0.01f);
        Assert.False(p.ActiveLeft);
    }

    [Fact]
    public void LastUsed_SwitchesToHandThatPressed()
    {
        var p = new PointerHands();
        p.Update(1f, 0f, 0.01f);
        Assert.True(p.ActiveLeft);
        Assert.True(p.Pressed);
        p.Update(0f, 0f, 0.01f);
        Assert.True(p.Released);
        p.Update(0f, 1f, 0.01f);
        Assert.False(p.ActiveLeft);
        Assert.True(p.Pressed);
    }

    [Fact]
    public void DoesNotSwitchWhileActiveHandHolds()
    {
        var p = new PointerHands();
        p.Update(0f, 1f, 0.01f);
        p.Update(1f, 1f, 0.01f);
        Assert.False(p.ActiveLeft);
        Assert.True(p.Held);
        Assert.False(p.Pressed);
    }

    [Fact]
    public void FixedMode_IgnoresOtherHand()
    {
        var p = new PointerHands { Mode = PointerHandMode.Left };
        p.Update(0f, 1f, 0.01f);
        Assert.True(p.ActiveLeft);
        Assert.False(p.Pressed);
        p.Update(1f, 1f, 0.01f);
        Assert.True(p.Pressed);
    }

    [Fact]
    public void UsesHysteresis()
    {
        var p = new PointerHands();
        p.Update(0f, 0.6f, 0.01f);
        Assert.True(p.Held);
        p.Update(0f, 0.4f, 0.01f);
        Assert.True(p.Held);
        p.Update(0f, 0.2f, 0.01f);
        Assert.False(p.Held);
    }
}
