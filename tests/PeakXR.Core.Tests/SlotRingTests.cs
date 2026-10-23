namespace PeakXR.Core.Tests;

public class SlotRingTests
{
    [Fact]
    public void Ring_IsOpaqueWhite()
    {
        SlotRing.Texel(SlotRing.RingRadius, out float value, out float alpha);
        Assert.Equal(1f, value, 3);
        Assert.Equal(1f, alpha, 3);
    }

    [Fact]
    public void JustOutsideTheRing_ADarkBandKeepsItReadableOnSnow()
    {
        SlotRing.Texel(SlotRing.OutlineRadius, out float value, out float alpha);
        Assert.True(value < 0.2f, $"value {value}");
        Assert.True(alpha > 0.6f, $"alpha {alpha}");
    }

    [Fact]
    public void Middle_IsAFaintWhiteFill_AndTheCornersClear()
    {
        SlotRing.Texel(0.4f, out float value, out float alpha);
        Assert.Equal(1f, value, 3);
        Assert.InRange(alpha, 0.1f, 0.3f);
        SlotRing.Texel(1.05f, out _, out alpha);
        Assert.Equal(0f, alpha, 3);
    }

    [Fact]
    public void Outline_StaysOutsideTheRing()
    {
        Assert.True(SlotRing.OutlineRadius > SlotRing.RingRadius);
        Assert.True(SlotRing.OutlineRadius < 1f);
    }
}
