namespace PeakXR.Core.Tests;

public class HoldGateTests
{
    static HoldInputs Calm() =>
        new()
        {
            Grounded = true,
            Upright = true,
            SinceFallSlide = 10f,
            SinceBounce = 10f,
            SlopeDeg = 5f,
            MaxSlopeDeg = 50f,
        };

    [Fact]
    public void StandingOnFlatGround_Free() => Assert.True(HoldGate.Free(Calm()));

    [Fact]
    public void Airborne_NotFree()
    {
        var i = Calm();
        i.Grounded = false;
        Assert.False(HoldGate.Free(i));
    }

    [Fact]
    public void RagdolledOrClimbing_NotFree()
    {
        // Upright is rig.camera's: ragdoll control, climbing, rope, vine, climb handle, gliding.
        var i = Calm();
        i.Upright = false;
        Assert.False(HoldGate.Free(i));
    }

    [Fact]
    public void MoveInput_NotFree()
    {
        var i = Calm();
        i.MoveInput = true;
        Assert.False(HoldGate.Free(i));
    }

    [Fact]
    public void Carried_NotFree()
    {
        var i = Calm();
        i.Carried = true;
        Assert.False(HoldGate.Free(i));
    }

    [Fact]
    public void InWater_NotFree()
    {
        var i = Calm();
        i.InWater = true;
        Assert.False(HoldGate.Free(i));
    }

    [Fact]
    public void Warping_NotFree()
    {
        var i = Calm();
        i.Warping = true;
        Assert.False(HoldGate.Free(i));
    }

    [Fact]
    public void SlidingOrJustSlid_NotFree()
    {
        var i = Calm();
        i.SinceFallSlide = 0f;
        Assert.False(HoldGate.Free(i));
        i.SinceFallSlide = HoldGate.SlideCooldown * 0.9f;
        Assert.False(HoldGate.Free(i));
        i.SinceFallSlide = HoldGate.SlideCooldown;
        Assert.True(HoldGate.Free(i));
    }

    [Fact]
    public void JustBounced_NotFree()
    {
        var i = Calm();
        i.SinceBounce = HoldGate.BounceCooldown * 0.5f;
        Assert.False(HoldGate.Free(i));
    }

    [Fact]
    public void SlopeAtTheGamesMaxAngle_NotFree()
    {
        var i = Calm();
        i.SlopeDeg = 50f;
        Assert.False(HoldGate.Free(i));
        i.SlopeDeg = 49f;
        Assert.True(HoldGate.Free(i));
    }

    [Fact]
    public void NonFinite_NotFree()
    {
        var i = Calm();
        i.SlopeDeg = float.NaN;
        Assert.False(HoldGate.Free(i));
        i = Calm();
        i.SinceFallSlide = float.NaN;
        Assert.False(HoldGate.Free(i));
    }
}
