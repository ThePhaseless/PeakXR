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

    [Fact]
    public void PlantedFeet_KeepTheMassCentre()
    {
        // Upper parts (torso 20 kg, head 10 kg) move 0.1 and 0.2 m forward relative to the hip. The ragdoll pulls every
        // part to hip + offset (Bodypart.cs:193-207) and the planted feet fix the hip at feet - leg offset, so the
        // legs' offset grows by the shift and the hip (with all it carries) moves back by it.
        const float total = 60f;
        const float legs = 20f;
        var moment = new Vector3(0f, -0.4f, 20f * 0.1f + 10f * 0.2f);
        var shift = BodyLean.LegShift(moment, total, legs);
        Assert.Equal(0f, shift.Y);
        // Mass centre change: everything off the feet moves back by the shift, the upper parts forward by their bend.
        var centre = (-shift * (total - legs) + new Vector3(moment.X, 0f, moment.Z)) / total;
        TestUtil.Near(Vector3.Zero, centre, 1e-5f);
        Assert.True(shift.Z > 0f, "the legs' targets move forward of the hip");
    }

    [Fact]
    public void AllMassInTheLegs_NoShift()
    {
        TestUtil.Near(Vector3.Zero, BodyLean.LegShift(new Vector3(0f, 0f, 1f), 20f, 20f));
    }
}
