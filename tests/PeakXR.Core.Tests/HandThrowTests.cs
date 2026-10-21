namespace PeakXR.Core.Tests;

public class HandThrowTests
{
    // Vanilla-shaped constants: the master adds 0.5 * multiplier * (min + (max - min) * charge) along the look.
    const float Min = 10f;
    const float Max = 30f;

    [Fact]
    public void Charge_InvertsTheMastersLookForce()
    {
        // 0.5 * (10 + 20 * 0.5) = 10 m/s for charge 0.5.
        Assert.Equal(0.5f, HandThrow.Charge(10f, Min, Max, 1f, 0f), 4);
        Assert.Equal(1f, HandThrow.Charge(100f, Min, Max, 1f, 0f), 4);
        Assert.Equal(10f, HandThrow.LookSpeed(0.5f, Min, Max, 1f), 4);
    }

    [Fact]
    public void Charge_BelowTheMinimumForceIsZero()
    {
        Assert.Equal(0f, HandThrow.Charge(4f, Min, Max, 1f, 0f));
        Assert.Equal(0f, HandThrow.LookSpeed(0f, Min, Max, 1f));
    }

    [Fact]
    public void Charge_NeverBelowTheStickyRequirement()
    {
        Assert.Equal(0.6f, HandThrow.Charge(4f, Min, Max, 1f, 0.6f), 4);
        Assert.Equal(0.5f, HandThrow.Charge(10f, Min, Max, 1f, 0.3f), 4);
    }

    [Fact]
    public void Charge_ScalesWithTheItemMultiplier()
    {
        // Twice the multiplier: the same hand speed needs half the force.
        Assert.Equal(0f, HandThrow.Charge(10f, Min, Max, 2f, 0f), 4);
        Assert.Equal(0.5f, HandThrow.Charge(20f, Min, Max, 2f, 0f), 4);
    }

    [Fact]
    public void Charge_IgnoresDegenerateForces() =>
        Assert.Equal(0f, HandThrow.Charge(10f, Max, Max, 1f, 0f));
}
