namespace PeakXR.Core.Tests;

public class RoundedRectTests
{
    [Fact]
    public void Distance_IsNegativeInside_ZeroOnEdge_PositiveOutside()
    {
        Assert.True(RoundedRect.Distance(0f, 0f, 10f, 5f, 2f) < 0f);
        TestUtil.Near(0f, RoundedRect.Distance(10f, 0f, 10f, 5f, 2f));
        TestUtil.Near(0f, RoundedRect.Distance(0f, -5f, 10f, 5f, 2f));
        TestUtil.Near(3f, RoundedRect.Distance(13f, 0f, 10f, 5f, 2f));
    }

    [Fact]
    public void Distance_RoundsTheCorner()
    {
        // The square corner lies outside the arc by r*(sqrt2 - 1).
        TestUtil.Near(2f * (MathF.Sqrt(2f) - 1f), RoundedRect.Distance(10f, 5f, 10f, 5f, 2f));
        // A point on the arc is on the edge.
        float c = 2f / MathF.Sqrt(2f);
        TestUtil.Near(0f, RoundedRect.Distance(8f + c, 3f + c, 10f, 5f, 2f));
    }

    [Fact]
    public void Distance_IsSymmetric()
    {
        float d = RoundedRect.Distance(7f, 4f, 10f, 5f, 2f);
        TestUtil.Near(d, RoundedRect.Distance(-7f, 4f, 10f, 5f, 2f));
        TestUtil.Near(d, RoundedRect.Distance(-7f, -4f, 10f, 5f, 2f));
    }

    [Fact]
    public void Alpha_OpaqueInside_ClearOutside_FeatheredBetween()
    {
        Assert.Equal(1f, RoundedRect.Alpha(0f, 0f, 10f, 10f, 3f, 4f));
        Assert.Equal(0f, RoundedRect.Alpha(10f, 0f, 10f, 10f, 3f, 4f));
        Assert.Equal(0f, RoundedRect.Alpha(12f, 0f, 10f, 10f, 3f, 4f));
        TestUtil.Near(0.5f, RoundedRect.Alpha(8f, 0f, 10f, 10f, 3f, 4f));
        Assert.Equal(1f, RoundedRect.Alpha(6f, 0f, 10f, 10f, 3f, 4f));
    }

    [Fact]
    public void Alpha_FallsOffMonotonicallyTowardsTheEdge()
    {
        float last = 1f;
        for (float x = 0f; x <= 10f; x += 0.25f)
        {
            float a = RoundedRect.Alpha(x, 0f, 10f, 10f, 3f, 4f);
            Assert.True(a <= last + 1e-6f, $"alpha rose at x={x}");
            last = a;
        }
    }

    [Fact]
    public void Alpha_ZeroFeather_IsHardEdge()
    {
        Assert.Equal(1f, RoundedRect.Alpha(9.9f, 0f, 10f, 10f, 3f, 0f));
        Assert.Equal(0f, RoundedRect.Alpha(10.1f, 0f, 10f, 10f, 3f, 0f));
    }
}
