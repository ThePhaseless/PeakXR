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
        Assert.False(Step(c, 1.35f)); // 16 % lower: a nod, a glance at the floor, a lean
    }

    [Fact]
    public void DeepDrop_Crouches()
    {
        var c = new PhysicalCrouch();
        Assert.True(Step(c, 1.0f)); // 37.5 % lower
    }

    [Fact]
    public void AStoopToTheGround_Crouches()
    {
        // vr.87/vr.88: a 0.47 m stoop (29 %) to a fuse or a torch stayed under the old 30 % and left the avatar
        // upright with its hands far above the controllers.
        var c = new PhysicalCrouch();
        Assert.True(Step(c, 1.6f - 0.47f));
    }

    [Fact]
    public void Hysteresis_HoldsBetweenThresholds()
    {
        var c = new PhysicalCrouch();
        Assert.False(Step(c, 1.30f)); // 18.8 %: not in yet
        Assert.True(Step(c, 1.26f)); // 21 %: in
        Assert.True(Step(c, 1.36f)); // 15 %: still in
        Assert.False(Step(c, 1.46f)); // 8.8 %: out
        Assert.False(Step(c, 1.36f)); // 15 %: stays out
    }

    [Theory]
    [InlineData(0.3f, 1.6f - 0.47f, false)] // a deeper setting keeps a stoop standing
    [InlineData(0.15f, 1.6f - 0.3f, true)]
    public void Depth_IsTheSetting(float depth, float head, bool crouched)
    {
        var c = new PhysicalCrouch();
        c.SetDepth(depth);
        Assert.Equal(crouched, Step(c, head));
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

    // Head right in tracking space: the head faces +Z.
    static readonly Vector3 Side = Vector3.UnitX;

    static bool Step(ArmSwing s, Vector3 left, Vector3 right, bool moving = true) =>
        s.Update(true, left, true, right, Side, moving, Dt);

    /// <summary>Hands swing along <paramref name="axis"/> with the given amplitude and frequency, for seconds;
    /// <paramref name="y"/> is the hands' height relative to the head.</summary>
    static bool Swing(
        ArmSwing s,
        float seconds,
        float amplitude,
        float hz,
        bool alternate,
        ref float t,
        bool moving = true,
        Vector3? axis = null,
        float y = -0.5f
    )
    {
        var a = axis ?? Vector3.UnitZ;
        bool on = false;
        for (float end = t + seconds; t < end; t += Dt)
        {
            float z = amplitude * MathF.Sin(2f * MathF.PI * hz * t);
            var left = new Vector3(-0.25f, y, 0f) + a * z;
            var right = new Vector3(0.25f, y, 0f) + a * (alternate ? -z : z);
            on = Step(s, left, right, moving);
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
            on = Step(s, still, still);
        Assert.False(on);
    }

    [Fact]
    public void LostTracking_EndsSprint()
    {
        var s = new ArmSwing();
        float t = 0f;
        Assert.True(Swing(s, 1.5f, 0.25f, 1.6f, true, ref t));
        Assert.False(s.Update(false, Vector3.Zero, true, Vector3.Zero, Side, true, Dt));
    }

    [Fact]
    public void ClappingOrWavingSideways_DoesNotSprint()
    {
        // Hands meeting and parting across the body move in opposite directions too, but sideways.
        var s = new ArmSwing();
        float t = 0f;
        Assert.False(Swing(s, 2f, 0.25f, 1.6f, true, ref t, axis: Vector3.UnitX));
    }

    [Fact]
    public void SwingingWhileStandingStill_DoesNotSprint_NorChargeTheNextWalk()
    {
        // The stick is idle: gestures (a chat, a stretch) never sprint, and they leave nothing behind, so the first
        // frames of walking afterwards do not sprint either.
        var s = new ArmSwing();
        float t = 0f;
        Assert.False(Swing(s, 2f, 0.25f, 1.6f, true, ref t, moving: false));
        Assert.False(Swing(s, 0.1f, 0.25f, 1.6f, true, ref t));
    }

    [Fact]
    public void HandsUpAtTheFace_DoNotSprint()
    {
        // Gestures at head height (talking, eating, waving hello) are not a running arm swing.
        var s = new ArmSwing();
        float t = 0f;
        Assert.False(Swing(s, 2f, 0.25f, 1.6f, true, ref t, y: 0f));
    }
}
