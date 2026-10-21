namespace PeakXR.Core.Tests;

public class WheelSegmentTests
{
    private const float Width = 900f;

    private static (float x, float y) Polar(float degreesFromUp, float fraction)
    {
        float a = degreesFromUp * MathF.PI / 180f;
        // Counter-clockwise from up: x = -sin, y = cos.
        return (-MathF.Sin(a) * fraction * Width, MathF.Cos(a) * fraction * Width);
    }

    [Fact]
    public void Contains_MiddleOfDrawnWedge()
    {
        var (x, y) = Polar(WheelSegment.MidDegrees, WheelSegment.MidFraction);
        Assert.True(WheelSegment.Contains(x, y, Width));
    }

    [Fact]
    public void Contains_RejectsCentreHole()
    {
        Assert.False(WheelSegment.Contains(0f, 0f, Width));
        var (x, y) = Polar(WheelSegment.MidDegrees, WheelSegment.InnerFraction * 0.9f);
        Assert.False(WheelSegment.Contains(x, y, Width));
    }

    [Fact]
    public void Contains_RejectsBeyondOuterEdgeAndRectCorner()
    {
        var (x, y) = Polar(WheelSegment.MidDegrees, WheelSegment.OuterFraction * 1.1f);
        Assert.False(WheelSegment.Contains(x, y, Width));
        Assert.False(WheelSegment.Contains(-Width * 0.5f, Width * 0.5f, Width));
    }

    [Fact]
    public void Contains_RejectsGapsAndOtherSlicesAngles()
    {
        // Just clockwise of the wedge start (gap to the next slice) and opposite side.
        var (x, y) = Polar(-2f, WheelSegment.MidFraction);
        Assert.False(WheelSegment.Contains(x, y, Width));
        (x, y) = Polar(WheelSegment.EndDegrees + 2f, WheelSegment.MidFraction);
        Assert.False(WheelSegment.Contains(x, y, Width));
        (x, y) = Polar(WheelSegment.MidDegrees + 180f, WheelSegment.MidFraction);
        Assert.False(WheelSegment.Contains(x, y, Width));
    }

    [Fact]
    public void Contains_ZeroWidth_IsFalse()
    {
        Assert.False(WheelSegment.Contains(1f, 1f, 0f));
    }
}
