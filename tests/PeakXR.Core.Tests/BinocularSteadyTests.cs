namespace PeakXR.Core.Tests;

public class BinocularSteadyTests
{
    [Fact]
    public void Magnification_LensAsWideAsView_IsOne()
    {
        Assert.Equal(1f, BinocularSteady.Magnification(40f, 40f), 4);
    }

    [Fact]
    public void Magnification_NarrowLens_Magnifies()
    {
        // tan(20) / tan(5) = 4.16
        Assert.Equal(4.16f, BinocularSteady.Magnification(10f, 40f), 2);
    }

    [Fact]
    public void SmoothTime_NoZoom_Snaps()
    {
        Assert.Equal(0f, BinocularSteady.SmoothTime(40f, 40f));
        Assert.Equal(0f, BinocularSteady.SmoothTime(60f, 40f));
    }

    [Fact]
    public void SmoothTime_GrowsWithZoom()
    {
        float low = BinocularSteady.SmoothTime(30f, 40f);
        float high = BinocularSteady.SmoothTime(10f, 40f);
        Assert.True(low > 0f);
        Assert.True(high > low);
    }

    [Fact]
    public void SmoothTime_LowZoom_StaysResponsive()
    {
        Assert.True(BinocularSteady.SmoothTime(35f, 40f) < 0.02f);
    }

    [Fact]
    public void SmoothTime_IsCapped()
    {
        Assert.Equal(BinocularSteady.MaxSmoothTime, BinocularSteady.SmoothTime(0.5f, 40f));
        Assert.Equal(BinocularSteady.MaxSmoothTime, BinocularSteady.SmoothTime(0f, 40f));
    }
}
