namespace PeakXR.Core.Tests;

public class ReadableTextTests
{
    [Fact]
    public void DegreesIsTheAngleTheHeightSpans()
    {
        // 2 * atan(0.5 / 1) = 53.13 degrees.
        TestUtil.Near(53.130f, ReadableText.Degrees(1f, 1f), 0.01f);
        Assert.Equal(180f, ReadableText.Degrees(1f, 0f));
    }

    [Fact]
    public void SmallTextGrowsToTheMinimumAngle()
    {
        float height = 0.005f;
        float k = ReadableText.Grow(height, 2f, 10f);
        TestUtil.Near(ReadableText.MinDegrees, ReadableText.Degrees(height * k, 2f), 0.001f);
    }

    [Fact]
    public void ReadableTextIsNeverShrunk()
    {
        Assert.Equal(1f, ReadableText.Grow(0.1f, 1f, 3f));
    }

    [Fact]
    public void GrowthIsCapped()
    {
        Assert.Equal(2f, ReadableText.Grow(0.0001f, 5f, 2f));
    }

    [Fact]
    public void EmptyOrDegenerateInputLeavesTheSize()
    {
        Assert.Equal(1f, ReadableText.Grow(0f, 1f, 3f));
        Assert.Equal(1f, ReadableText.Grow(0.01f, 0f, 3f));
    }

    [Fact]
    public void MedianIgnoresOneOutlier()
    {
        var sizes = new[] { 0.9f, 0.01f, 0.012f, 0.011f };
        TestUtil.Near(0.0115f, ReadableText.Median(sizes, 4), 1e-6f);
        Assert.Equal(0f, ReadableText.Median(sizes, 0));
        TestUtil.Near(0.01f, ReadableText.Median(new[] { 0.01f, 0.5f, 0.7f }, 1));
    }
}
