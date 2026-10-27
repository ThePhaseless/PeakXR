namespace PeakXR.Core.Tests;

public class ExpSmoothTests
{
    [Fact]
    public void Factor_IsOneMinusExpOfRateTimesDt()
    {
        TestUtil.Near(1f - MathF.Exp(-1f), ExpSmooth.Factor(10f, 0.1f), 1e-6f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-5f)]
    [InlineData(float.PositiveInfinity)]
    public void Factor_ZeroNegativeOrInfiniteRate_Snaps(float rate)
    {
        Assert.Equal(1f, ExpSmooth.Factor(rate, 0.02f));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-0.1f)]
    public void Factor_NoTimePassed_Holds(float dt)
    {
        Assert.Equal(0f, ExpSmooth.Factor(10f, dt));
    }
}
