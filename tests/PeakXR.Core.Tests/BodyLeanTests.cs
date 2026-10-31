namespace PeakXR.Core.Tests;

public class BodyLeanTests
{
    static readonly Vector3 Up = Vector3.UnitY;
    static readonly Vector3 Forward = Vector3.UnitZ;
    static readonly Vector3 Pivot = Vector3.Zero;
    static readonly Vector3 Eye = new(0f, 0.8f, 0.05f);

    static Quaternion Swing(Vector3 head) => BodyLean.Swing(Pivot, Eye, head, Forward, Up);

    static Vector3 Moved(Quaternion q) => Vector3.Transform(Eye - Pivot, q) + Pivot;

    [Fact]
    public void HeadOnTheEyes_NoLean()
    {
        TestUtil.SameRotation(Quaternion.Identity, Swing(Eye));
    }

    [Fact]
    public void HeadStraightBelow_NoLean()
    {
        // Ducking is the crouch's job: a head lower on the same line does not bend the spine.
        TestUtil.SameRotation(Quaternion.Identity, Swing(Eye * 0.8f));
    }

    [Fact]
    public void ForwardLean_SwingsTheEyesOntoTheHeadLine()
    {
        var head = Eye + new Vector3(0f, -0.06f, 0.3f);
        var eye = Moved(Swing(head));
        var along = Vector3.Normalize(head - Pivot);
        TestUtil.Near(along, Vector3.Normalize(eye - Pivot), 1e-3f);
        Assert.True(eye.Z > Eye.Z + 0.25f, $"eye only reached z {eye.Z:0.000}");
    }

    [Fact]
    public void SidewaysLean_RollsTowardTheHead()
    {
        var head = Eye + new Vector3(0.2f, -0.03f, 0f);
        var eye = Moved(Swing(head));
        TestUtil.Near(Vector3.Normalize(head - Pivot), Vector3.Normalize(eye - Pivot), 1e-3f);
    }

    [Fact]
    public void FarLean_StopsAtTheLimit()
    {
        var q = Swing(Eye + new Vector3(0f, -0.5f, 1.5f));
        float start = MathF.Atan2(Eye.Z, Eye.Y) * 180f / MathF.PI;
        var eye = Moved(q);
        float end = MathF.Atan2(eye.Z, eye.Y) * 180f / MathF.PI;
        TestUtil.Near(BodyLean.MaxForwardDeg, end - start, 0.05f);

        var back = Moved(Swing(Eye + new Vector3(0f, 0f, -1f)));
        float backEnd = MathF.Atan2(back.Z, back.Y) * 180f / MathF.PI;
        TestUtil.Near(-BodyLean.MaxBackDeg, backEnd - start, 0.05f);

        var side = Moved(Swing(Eye + new Vector3(-1f, 0f, 0f)));
        float roll = MathF.Atan2(side.X, side.Y) * 180f / MathF.PI;
        TestUtil.Near(-BodyLean.MaxSideDeg, roll, 0.05f);
    }

    [Fact]
    public void Swing_FollowsTheBodyHeading()
    {
        // Facing +X: a lean toward +X is forward, not sideways.
        var forward = Vector3.UnitX;
        var eye = new Vector3(0.05f, 0.8f, 0f);
        var head = eye + new Vector3(0.3f, 0f, 0f);
        var q = BodyLean.Swing(Pivot, eye, head, forward, Up);
        var moved = Vector3.Transform(eye, q);
        Assert.True(MathF.Abs(moved.Z) < 1e-3f, $"rolled sideways to z {moved.Z:0.000}");
        Assert.True(moved.X > 0.25f);
    }
}

public class BodyLeanTryTests
{
    [Fact]
    public void HeadBelowThePivot_CannotLean()
    {
        // Lying or a deep duck: no defined lean, so the driver fades the last one out instead of snapping it.
        Assert.False(
            BodyLean.TrySwing(
                Vector3.Zero,
                new Vector3(0f, 0.8f, 0f),
                new Vector3(0f, -0.1f, 0.4f),
                Vector3.UnitZ,
                Vector3.UnitY,
                out var q
            )
        );
        TestUtil.SameRotation(Quaternion.Identity, q);
        Assert.True(
            BodyLean.TrySwing(
                Vector3.Zero,
                new Vector3(0f, 0.8f, 0f),
                new Vector3(0f, 0.7f, 0.3f),
                Vector3.UnitZ,
                Vector3.UnitY,
                out _
            )
        );
    }
}

public class BodyLeanLegShiftTests
{
    [Fact]
    public void NoBend_NoShift()
    {
        TestUtil.Near(Vector3.Zero, BodyLean.LegShift(Vector3.Zero, 60f, 20f));
    }

    // A ragdoll at rest: every part at hip + its animated offset (Bodypart.cs:193-207). Masses in kg, offsets in m.
    static readonly (float Mass, Vector3 Offset, bool Upper, bool Leg)[] Parts =
    {
        (15f, Vector3.Zero, false, false), // hip
        (8f, new(0f, 0.2f, 0f), true, false), // mid (the bend's pivot)
        (12f, new(0f, 0.45f, -0.02f), true, false), // torso
        (10f, new(0f, 0.85f, 0.05f), true, false), // head
        (3f, new(-0.2f, 0.6f, 0f), true, false), // upper arms
        (3f, new(0.2f, 0.6f, 0f), true, false),
        (5f, new(-0.1f, -0.25f, 0f), false, true), // thighs, shins, feet
        (5f, new(0.1f, -0.25f, 0f), false, true),
        (3f, new(-0.1f, -0.6f, 0f), false, true),
        (3f, new(0.1f, -0.6f, 0f), false, true),
        (2f, new(-0.1f, -0.9f, 0.05f), false, true),
        (2f, new(0.1f, -0.9f, 0.05f), false, true),
    };

    /// <summary>Horizontal mass centre with the feet (the last part) planted at <paramref name="feet"/>.</summary>
    static Vector3 Centre(Vector3[] offsets, Vector3 feet)
    {
        var hip = feet - offsets[^1];
        var sum = Vector3.Zero;
        float mass = 0f;
        for (int i = 0; i < Parts.Length; i++)
        {
            sum += (hip + offsets[i]) * Parts[i].Mass;
            mass += Parts[i].Mass;
        }
        var c = sum / mass;
        return new Vector3(c.X, 0f, c.Z);
    }

    [Theory]
    [InlineData(20f, 0f)]
    [InlineData(35f, 0f)]
    [InlineData(0f, 20f)]
    [InlineData(25f, -15f)]
    public void PlantedFeet_KeepTheMassCentre(float pitchDeg, float rollDeg)
    {
        var feet = new Vector3(3f, 0f, -2f);
        var rest = Array.ConvertAll(Parts, p => p.Offset);
        var before = Centre(rest, feet);

        var pivot = Parts[1].Offset;
        var bend = Quaternion.CreateFromYawPitchRoll(
            0f,
            pitchDeg * MathF.PI / 180f,
            rollDeg * MathF.PI / 180f
        );
        var bent = (Vector3[])rest.Clone();
        var moment = Vector3.Zero;
        float total = 0f;
        float legs = 0f;
        for (int i = 0; i < Parts.Length; i++)
        {
            total += Parts[i].Mass;
            if (Parts[i].Leg)
                legs += Parts[i].Mass;
            if (!Parts[i].Upper)
                continue;
            bent[i] = pivot + Vector3.Transform(rest[i] - pivot, bend);
            moment += Parts[i].Mass * (bent[i] - rest[i]);
        }
        // Without the shift the bend alone moves the planted body's mass centre: the creep run 28 saw.
        Assert.True(Vector3.Distance(before, Centre(bent, feet)) > 0.02f);

        var shift = BodyLean.LegShift(moment, total, legs);
        Assert.Equal(0f, shift.Y);
        for (int i = 0; i < Parts.Length; i++)
            if (Parts[i].Leg)
                bent[i] += shift;
        TestUtil.Near(before, Centre(bent, feet), 1e-5f);
    }

    [Fact]
    public void AllMassInTheLegs_NoShift()
    {
        TestUtil.Near(Vector3.Zero, BodyLean.LegShift(new Vector3(0f, 0f, 1f), 20f, 20f));
    }
}

public class BodyLeanHeadBiasTests
{
    [Fact]
    public void MovingBody_NoBias()
    {
        // The animated eye is sampled on a physics step, the calibrated eye on the rendered frame 20 ms later. Walking
        // at 5 m/s the body moved 0.1 m in between: carried on the frame's hip, the bias is the animated-physical gap
        // alone, not that motion.
        var gap = new Vector3(0f, 0.01f, 0.12f);
        var eyeFromHip = new Vector3(0f, 1.0f, 0.15f) + gap;
        var hipStep = new Vector3(4f, 1f, 7f);
        var hipFrame = hipStep + new Vector3(0f, 0f, 5f) * 0.02f;
        var calibrated = hipFrame + new Vector3(0f, 1.0f, 0.15f);
        TestUtil.Near(gap, BodyLean.HeadBias(eyeFromHip, hipFrame, calibrated), 1e-5f);
        // The step's world eye against the frame's calibrated eye reads the 0.1 m walk as a backward lean.
        Assert.True(Vector3.Distance(gap, hipStep + eyeFromHip - calibrated) > 0.09f);
    }
}

public class BodyLeanHeldHeadTests
{
    static readonly Vector3 Up = Vector3.UnitY;
    static readonly Vector3 Forward = Vector3.UnitZ;

    // The scout's head joint sits at the neck and its eyes high in a big head. The head keeps its world rotation
    // through the bend (AvatarPoseDriver.ApplyLean), so the eyes move only as far as the joint does.
    static readonly Vector3 Joint = new(0f, 0.55f, 0.08f);
    static readonly Vector3 Eye = new(0f, 0.8f, 0.29f);

    static Vector3 EyeAfter(Quaternion q) => Vector3.Transform(Joint, q) + (Eye - Joint);

    static Vector3 Level(Vector3 v) => new(v.X, 0f, v.Z);

    static Quaternion Swing(Vector3 head)
    {
        Assert.True(BodyLean.TrySwingHeld(Vector3.Zero, Joint, Eye, head, Forward, Up, out var q));
        return q;
    }

    [Theory]
    [InlineData(0f, 0.15f)]
    [InlineData(0f, 0.3f)]
    [InlineData(0f, -0.1f)]
    [InlineData(0.15f, 0f)]
    [InlineData(-0.12f, 0.08f)]
    public void Lean_LandsTheEyesOnTheHead(float right, float forward)
    {
        // Run 14: the swing aimed at the eye itself moved it 0.47-0.52 of the want (vr.45: 0.161 of 0.311 m).
        var head = Eye + new Vector3(right, -0.04f, forward);
        TestUtil.Near(Level(head), Level(EyeAfter(Swing(head))), 2e-3f);
    }

    [Fact]
    public void FarLean_StopsAtTheLimit()
    {
        var joint = Vector3.Transform(Joint, Swing(Eye + new Vector3(0f, 0f, 0.6f)));
        float turned = MathF.Atan2(joint.Z, joint.Y) - MathF.Atan2(Joint.Z, Joint.Y);
        TestUtil.Near(BodyLean.MaxForwardDeg, turned * 180f / MathF.PI, 0.05f);
    }

    [Fact]
    public void HeadOnTheEyes_NoLean() => TestUtil.SameRotation(Quaternion.Identity, Swing(Eye));

    [Theory]
    [InlineData(0.6f, -0.12f)]
    [InlineData(-0.6f, -0.12f)]
    [InlineData(0.6f, 0f)]
    [InlineData(0.5f, 0.3f)]
    public void SidewaysPart_BendsNoFurtherThanTheSideLimit(float right, float forward)
    {
        // vr.45 (run 15): the sideways lean, with the body's eyes 0.1 m ahead of the head, bent 26.7 degrees: 25
        // sideways and 9 back at once. Each direction's limit bounds the whole bend toward it (an elliptic cone).
        var q = Swing(Eye + new Vector3(right, -0.03f, forward));
        float angle = 2f * MathF.Acos(MathF.Min(1f, MathF.Abs(q.W))) * 180f / MathF.PI;
        var joint = Vector3.Transform(Joint, q);
        float side = MathF.Atan2(joint.X, joint.Y) * 180f / MathF.PI;
        float fwd =
            (MathF.Atan2(joint.Z, joint.Y) - MathF.Atan2(Joint.Z, Joint.Y)) * 180f / MathF.PI;
        float along = fwd >= 0f ? BodyLean.MaxForwardDeg : BodyLean.MaxBackDeg;
        float e =
            fwd / along * (fwd / along) + side / BodyLean.MaxSideDeg * (side / BodyLean.MaxSideDeg);
        Assert.True(e <= 1.02f, $"side {side:0.0}, forward {fwd:0.0}: {e:0.00} of the cone");
        Assert.True(
            MathF.Abs(side) <= BodyLean.MaxSideDeg + 0.5f,
            $"bent {angle:0.0}, side {side:0.0}"
        );
        if (forward <= 0f)
            Assert.True(angle <= BodyLean.MaxSideDeg + 0.5f, $"bent {angle:0.0}");
    }
}
