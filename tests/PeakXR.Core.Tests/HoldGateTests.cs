namespace PeakXR.Core.Tests;

public class HoldGateTests
{
    static HoldInputs Calm() =>
        new()
        {
            Grounded = true,
            Upright = true,
            WaterBlend = 1f,
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
        // CharacterData.inWater is the animator's blend: WaterZone writes 0.25 deep or 0.3 shallow (WaterZone.cs:126,
        // :130), and CharacterAnimations puts 1 back every frame (CharacterAnimations.cs:283-284).
        var i = Calm();
        i.WaterBlend = 0.25f;
        Assert.False(HoldGate.Free(i));
        i.WaterBlend = 0.3f;
        Assert.False(HoldGate.Free(i));
        Assert.Equal("water", HoldGate.Why(i));
    }

    [Fact]
    public void Run34PlainLean_Holds()
    {
        // Run 34 vr.45 forward lean: grounded, upright, no input, dry (inWater 1), flat sand, no slide or bounce
        // since the session started; bend 16.8 deg. The hold must act.
        var i = new HoldInputs
        {
            Grounded = true,
            Upright = true,
            WaterBlend = 1f,
            SinceFallSlide = 500f,
            SinceBounce = 529f,
            SlopeDeg = 3f,
            MaxSlopeDeg = 50f,
        };
        Assert.True(HoldGate.Free(i));
        var h = new BodyHold();
        var start = new Vector2(22.7f, -373.1f);
        h.Update(start, Vector2.Zero, true, HoldGate.Free(i), 1f / 50f);
        var a = h.Update(
            start + new Vector2(0.01f, 0f),
            new Vector2(0.01f, 0f),
            true,
            HoldGate.Free(i),
            1f / 50f
        );
        Assert.True(h.Holding, h.Why);
        Assert.True(a.X < 0f);
    }

    [Fact]
    public void DryLand_IsNotWater()
    {
        // Run 34: every plain lean read inWater 1 (dry) as water and the hold never ran (hold 0 all through vr.45).
        var i = Calm();
        i.WaterBlend = 1f;
        Assert.True(HoldGate.Free(i));
        Assert.Null(HoldGate.Why(i));
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
