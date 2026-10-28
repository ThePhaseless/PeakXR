namespace PeakXR.Core.Tests;

public class ButtonEdgeTests
{
    [Fact]
    public void PressedOnlyOnFirstDownFrame()
    {
        var b = new ButtonEdge();
        b.Update(true, 0.01f);
        Assert.True(b.Pressed);
        Assert.True(b.Held);
        b.Update(true, 0.01f);
        Assert.False(b.Pressed);
        Assert.True(b.Held);
    }

    [Fact]
    public void ReleasedOnlyOnFirstUpFrame()
    {
        var b = new ButtonEdge();
        b.Update(true, 0.01f);
        b.Update(false, 0.01f);
        Assert.True(b.Released);
        Assert.False(b.Held);
        b.Update(false, 0.01f);
        Assert.False(b.Released);
    }

    [Fact]
    public void HeldTime_AccumulatesAndReportsDurationOnRelease()
    {
        var b = new ButtonEdge();
        b.Update(true, 0.1f);
        Assert.Equal(0f, b.HeldTime);
        b.Update(true, 0.1f);
        b.Update(true, 0.1f);
        TestUtil.Near(0.2f, b.HeldTime, 1e-6f);
        b.Update(false, 0.1f);
        TestUtil.Near(0.2f, b.HeldTime, 1e-6f);
        b.Update(false, 0.1f);
        Assert.Equal(0f, b.HeldTime);
    }

    [Fact]
    public void Analog_UsesHysteresis()
    {
        var a = new AnalogButton(0.7f, 0.5f);
        a.Update(0.65f, 0.01f);
        Assert.False(a.Held);
        a.Update(0.7f, 0.01f);
        Assert.True(a.Pressed);
        a.Update(0.55f, 0.01f);
        Assert.True(a.Held);
        a.Update(0.5f, 0.01f);
        Assert.True(a.Released);
        Assert.False(a.Held);
        a.Update(0.65f, 0.01f);
        Assert.False(a.Held);
    }

    [Fact]
    public void Analog_ExposesHeldTimeAndValue()
    {
        var a = new AnalogButton();
        a.Update(1f, 0.1f);
        a.Update(0.9f, 0.1f);
        TestUtil.Near(0.1f, a.HeldTime, 1e-6f);
        Assert.Equal(0.9f, a.Value);
    }

    [Fact]
    public void Analog_HeldThroughABlink_ByFeedingItsOwnValue()
    {
        // A tracking blink keeps the last grip: no release, no second press.
        var a = new AnalogButton(0.6f, 0.4f);
        a.Update(1f, 0.01f);
        a.Update(a.Value, 0.01f);
        Assert.True(a.Held);
        Assert.False(a.Pressed);
        Assert.False(a.Released);
    }
}
