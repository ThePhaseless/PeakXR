namespace PeakXR.Core.Tests;

public class PhysicalCrouchTests
{
    // Calibrated standing eye height 1.6 m on a floor-level tracking space.
    const float Eye = 1.6f;

    static bool Step(PhysicalCrouch c, float headY) => c.Update(true, headY, Eye, Eye);

    [Fact]
    public void Standing_DoesNotCrouch()
    {
        var c = new PhysicalCrouch();
        Assert.False(Step(c, 1.6f));
        Assert.False(Step(c, 1.3f)); // 19 % lower: a nod or a lean
    }

    [Fact]
    public void DeepDrop_Crouches()
    {
        var c = new PhysicalCrouch();
        Assert.True(Step(c, 1.0f)); // 37.5 % lower
    }

    [Fact]
    public void Hysteresis_HoldsBetweenThresholds()
    {
        var c = new PhysicalCrouch();
        Assert.False(Step(c, 1.16f)); // 27.5 %: not in yet
        Assert.True(Step(c, 1.1f)); // 31 %: in
        Assert.True(Step(c, 1.2f)); // 25 %: still in
        Assert.False(Step(c, 1.27f)); // 20.6 %: out
        Assert.False(Step(c, 1.2f)); // 25 %: stays out
    }

    [Fact]
    public void Disabled_NeverCrouches_AndClearsState()
    {
        var c = new PhysicalCrouch();
        Assert.True(Step(c, 1.0f));
        Assert.False(c.Update(false, 1.0f, Eye, Eye));
        Assert.False(c.Crouched);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.3f)]
    [InlineData(float.NaN)]
    public void ImplausibleStandingHeight_DoesNotCrouch(float standing)
    {
        var c = new PhysicalCrouch();
        Assert.False(c.Update(true, -5f, Eye, standing));
    }

    [Fact]
    public void DeviceRelativeSpace_UsesTheReferenceHeight()
    {
        // Head-origin tracking: the calibrated eye sits at y 0, the drop is measured against 1.65 m.
        var c = new PhysicalCrouch();
        Assert.False(c.Update(true, -0.3f, 0f, 1.65f));
        Assert.True(c.Update(true, -0.6f, 0f, 1.65f));
    }
}

public class ArmSwingTests
{
    const float Dt = 1f / 90f;

    /// <summary>Hands swing forward and back along Z with the given amplitude and frequency, for seconds.</summary>
    static bool Swing(
        ArmSwing s,
        float seconds,
        float amplitude,
        float hz,
        bool alternate,
        ref float t
    )
    {
        bool on = false;
        for (float end = t + seconds; t < end; t += Dt)
        {
            float z = amplitude * MathF.Sin(2f * MathF.PI * hz * t);
            var left = new Vector3(-0.25f, -0.5f, z);
            var right = new Vector3(0.25f, -0.5f, alternate ? -z : z);
            on = s.Update(true, left, true, right, Dt);
        }
        return on;
    }

    [Fact]
    public void AlternatingBriskSwing_Sprints()
    {
        var s = new ArmSwing();
        float t = 0f;
        Assert.True(Swing(s, 1.5f, 0.25f, 1.6f, true, ref t));
    }

    [Fact]
    public void BothHandsTogether_DoesNotSprint()
    {
        // Same-direction motion: carrying, leaning, a two-hand gesture.
        var s = new ArmSwing();
        float t = 0f;
        Assert.False(Swing(s, 2f, 0.25f, 1.6f, false, ref t));
    }

    [Fact]
    public void SlowSwing_DoesNotSprint()
    {
        var s = new ArmSwing();
        float t = 0f;
        Assert.False(Swing(s, 2f, 0.1f, 0.8f, true, ref t));
    }

    [Fact]
    public void StoppingTheSwing_EndsSprintQuickly()
    {
        var s = new ArmSwing();
        float t = 0f;
        Assert.True(Swing(s, 1.5f, 0.25f, 1.6f, true, ref t));
        bool on = true;
        var still = new Vector3(0f, -0.5f, 0f);
        for (int i = 0; i < 90; i++)
            on = s.Update(true, still, true, still, Dt);
        Assert.False(on);
    }

    [Fact]
    public void LostTracking_EndsSprint()
    {
        var s = new ArmSwing();
        float t = 0f;
        Assert.True(Swing(s, 1.5f, 0.25f, 1.6f, true, ref t));
        Assert.False(s.Update(false, Vector3.Zero, true, Vector3.Zero, Dt));
    }
}
