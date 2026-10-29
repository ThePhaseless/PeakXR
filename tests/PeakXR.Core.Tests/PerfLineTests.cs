using System.Text;

namespace PeakXR.Core.Tests;

public class PerfLineTests
{
    [Theory]
    [InlineData(12.345f, 1, "12.3")]
    [InlineData(9.96f, 1, "10.0")]
    [InlineData(0.04f, 1, "0.0")]
    [InlineData(0.05f, 2, "0.05")]
    [InlineData(1234f, 0, "1234")]
    [InlineData(0f, 0, "0")]
    [InlineData(-1f, 1, "n/a")] // not reported
    [InlineData(float.NaN, 1, "n/a")]
    [InlineData(float.PositiveInfinity, 1, "n/a")]
    public void AppendsFixedPointWithoutFormatting(float value, int decimals, string expected)
    {
        var sb = new StringBuilder("x ");
        PerfLine.Append(sb, value, decimals);
        Assert.Equal("x " + expected, sb.ToString());
    }

    [Theory]
    [InlineData(11.0f, 9f, 8f, 11.11f, FrameBound.OnRate)]
    [InlineData(18.2f, 16.8f, 9f, 11.11f, FrameBound.MainThread)] // the 55 fps report if the main thread is the cost
    [InlineData(18.2f, 6f, 17f, 11.11f, FrameBound.Gpu)]
    [InlineData(18.2f, 12f, 15f, 11.11f, FrameBound.Gpu)] // both over: the larger names it
    [InlineData(18.2f, 15f, 12f, 11.11f, FrameBound.MainThread)]
    [InlineData(18.2f, 6f, 7f, 11.11f, FrameBound.Waiting)] // nothing saturated: render thread, compositor or present
    [InlineData(18.2f, 6f, -1f, 11.11f, FrameBound.Waiting)] // no GPU time: still not the main thread
    [InlineData(18.2f, 6f, 7f, 0f, FrameBound.Unknown)] // no headset rate
    public void NamesWhereTheFrameGoes(
        float frame,
        float main,
        float gpu,
        float budget,
        FrameBound bound
    ) => Assert.Equal(bound, PerfLine.Judge(frame, main, gpu, budget));
}
