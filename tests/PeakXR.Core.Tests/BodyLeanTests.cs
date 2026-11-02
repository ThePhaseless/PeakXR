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

    [Fact]
    public void ALookThatTurnedSinceTheStep_TurnsTheStepsEye()
    {
        // The next step stands the pose on the new look (CharacterRagdoll.RotateCharacter), so the eye read on the old one
        // reads as a lean by the turn's chord: vr.300's 45 degree snap wanted f-0.072 r-0.183 for one step and bent 18.5.
        var eyeAtOldLook = new Vector3(0f, 1f, 0.26f);
        var eyeAtNewLook = new Vector3(
            0.26f * MathF.Sin(MathF.PI / 4f),
            1f,
            0.26f * MathF.Cos(MathF.PI / 4f)
        );
        var hip = new Vector3(4f, 1f, 7f);
        var bias = BodyLean.HeadBias(eyeAtOldLook, hip, hip, turned: 45f);
        TestUtil.Near(eyeAtNewLook, bias, 1e-5f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(7f)]
    [InlineData(-7.5f)]
    public void AHeadSwing_AtThe30FpsRate_KeepsTheStepsEye(float turned)
    {
        // A swing's eye trails it by about a 30 fps frame: 2-3 degrees of bend at 400 deg/s, which took the load off the feet.
        // Without it the body slid 0.082-0.122 m after a glance with the hold on (vr.302, #123); with it 0.025-0.029 m.
        var eye = new Vector3(0f, 1f, 0.26f);
        var hip = new Vector3(4f, 1f, 7f);
        TestUtil.Near(
            hip + eye - hip,
            BodyLean.HeadBias(eye, hip, hip, turned, turned / BodyLean.SwingLagSeconds),
            1e-5f
        );
    }

    [Theory]
    [InlineData(25f, 100f)]
    [InlineData(30f, 100f)]
    [InlineData(40f, 100f)]
    [InlineData(60f, 100f)]
    [InlineData(90f, 100f)]
    [InlineData(144f, 100f)]
    [InlineData(25f, 200f)]
    [InlineData(30f, 200f)]
    [InlineData(40f, 200f)]
    [InlineData(60f, 200f)]
    [InlineData(90f, 200f)]
    [InlineData(144f, 200f)]
    [InlineData(25f, 300f)]
    [InlineData(90f, 300f)]
    [InlineData(25f, 400f)]
    [InlineData(40f, 400f)]
    [InlineData(90f, 400f)]
    public void ASwing_TrailsTheSame_AtEveryFrameRate(float fps, float rate)
    {
        // The fix was measured at 25-40 fps, where the turn since the last physics step happens to be about a frame: at 90 Hz
        // that step is 3-4x shorter. At 25 fps and 400 deg/s a frame turns 16 degrees, past SwingTrail, and must not flip.
        // The band is the smoothing's ripple on 50 Hz steps: 5.2-7.3 at 30 fps and 200 deg/s, where the old rule read 6.7.
        var (least, most) = TrailDegrees(fps, rate);
        float want = MathF.Min(rate * BodyLean.SwingLagSeconds, BodyLean.MaxSwingLag);
        Assert.True(
            least > want - 2f && most < want + 2f,
            $"{fps} fps, {rate} deg/s: trail {least:0.0}-{most:0.0}, want {want:0.0}"
        );
    }

    /// <summary>A look ramping to <paramref name="rate"/> over 0.25 s, written once a rendered frame, read on 50 Hz physics
    /// steps as HoldBody and ApplyLean read it: how far the eye trails the look at its steady rate, least and most.</summary>
    static (float Least, float Most) TrailDegrees(float fps, float rate)
    {
        const float Tick = 0.02f;
        float frame = 1f / fps;
        float yaw = 0f,
            stepYaw = 0f,
            swing = 0f,
            nextTick = 0f,
            least = float.MaxValue,
            most = float.MinValue;
        var eye = new Vector3(0f, 1f, 0.26f);
        for (float t = 0f; t < 2f; t += frame)
        {
            while (nextTick <= t)
            {
                swing = BodyLean.SwingRate(swing, yaw - stepYaw, Tick);
                stepYaw = yaw;
                nextTick += Tick;
            }
            yaw += rate * MathF.Min(1f, t / 0.25f) * frame;
            if (t < 1f)
                continue;
            float turned = yaw - stepYaw;
            var bias = BodyLean.HeadBias(eye, Vector3.Zero, Vector3.Zero, turned, swing);
            float trail = turned - MathF.Atan2(bias.X, bias.Z) * 180f / MathF.PI;
            least = MathF.Min(least, trail);
            most = MathF.Max(most, trail);
        }
        return (least, most);
    }

    [Theory]
    [InlineData(8f, 2)]
    [InlineData(5f, 5)]
    public void TheRate_ReadsThrough_ATickThatCarriesAFrame(float step, int nthEmpty)
    {
        // 200 deg/s at 25 and at 40 fps: one physics step carries a whole frame, read raw 2x and 1.25x the rate, and the
        // step with no frame in it reads 0.
        const float Tick = 0.02f;
        float swing = 0f;
        for (int i = 0; i < 100; i++)
        {
            swing = BodyLean.SwingRate(swing, (i + 1) % nthEmpty == 0 ? 0f : step, Tick);
            if (i > 20)
                Assert.True(swing is > 150f and < 250f, $"step {i}: read {swing:0}");
        }
    }

    [Theory]
    [InlineData(0f, 45f)]
    [InlineData(0f, 15f)]
    [InlineData(200f, 52f)]
    public void ASnap_LeavesNoRateToTrail(float rate, float step)
    {
        // A smoothed 45 degree step reads 740 deg/s, so the eye would trail by the 8 degree limit for 0.1 s after a snap
        // that has already landed: a bend under a still head, which the snap fix removed (vr.300, #123).
        Assert.Equal(0f, BodyLean.SwingRate(rate, step, 0.02f));
        Assert.True(BodyLean.IsSnap(step, rate));
    }

    [Theory]
    [InlineData(320f, 16f)]
    [InlineData(200f, 14f)]
    [InlineData(0f, 14.9f)]
    public void AFastSwingsStep_IsNoSnap(float rate, float step)
    {
        Assert.False(BodyLean.IsSnap(step, rate));
        Assert.True(BodyLean.SwingRate(rate, step, 0.02f) > 0f);
    }

    [Theory]
    [InlineData(15f)]
    [InlineData(-45f)]
    public void ATurnPastASwing_TurnsTheStepsEye(float turned)
    {
        var eye = new Vector3(0f, 1f, 0.26f);
        var hip = new Vector3(4f, 1f, 7f);
        var bias = BodyLean.HeadBias(eye, hip, hip, turned);
        float rad = turned * MathF.PI / 180f;
        TestUtil.Near(new Vector3(0.26f * MathF.Sin(rad), 1f, 0.26f * MathF.Cos(rad)), bias, 1e-5f);
    }

    [Fact]
    public void ALookThatTurnedSinceTheStep_KeepsTheGapBetweenEyes()
    {
        // Only the step's eye turns: the frame's calibrated eye already stands on the new look.
        var hip = new Vector3(4f, 1f, 7f);
        var calibrated = hip + new Vector3(0.1f, 1f, 0.1f);
        var bias = BodyLean.HeadBias(new Vector3(-0.26f, 1f, 0f), hip, calibrated, turned: -90f);
        TestUtil.Near(new Vector3(-0.1f, 0f, -0.36f), bias, 1e-5f);
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
