namespace PeakXR.Core.Tests;

public class ArmMathTests
{
    static readonly Vector3 Right = Vector3.UnitX;
    static readonly Vector3 Up = Vector3.UnitY;
    static readonly Vector3 Forward = Vector3.UnitZ;

    /// <summary>The solver only uses the hint's offset perpendicular to the shoulder-to-hand axis.</summary>
    static Vector3 Pole(Vector3 shoulder, Vector3 hand, Vector3 hint) =>
        ArmMath.BendDirection(shoulder, hint, hand);

    [Fact]
    public void ElbowHint_HandsRaisedInFront_ElbowsPointDownAndOut()
    {
        // Scout shoulders sit 0.47 m under the camera, so hands near the face are up and forward of them.
        var shoulderL = new Vector3(-0.26f, 0f, 0f);
        var handL = new Vector3(-0.18f, 0.22f, 0.28f);
        var hintL = ArmMath.ElbowHint(shoulderL, handL, Right, Up, Forward, left: true);
        var poleL = Pole(shoulderL, handL, hintL);
        Assert.True(poleL.Y < -0.5f, $"left pole {poleL} should point down");
        Assert.True(poleL.X < 0f, $"left pole {poleL} should point left (out)");

        var shoulderR = new Vector3(0.26f, 0f, 0f);
        var handR = new Vector3(0.18f, 0.22f, 0.28f);
        var poleR = Pole(
            shoulderR,
            handR,
            ArmMath.ElbowHint(shoulderR, handR, Right, Up, Forward, left: false)
        );
        Assert.True(poleR.Y < -0.5f, $"right pole {poleR} should point down");
        Assert.True(poleR.X > 0f, $"right pole {poleR} should point right (out)");
    }

    [Fact]
    public void ElbowHint_HandOverhead_ElbowGoesOutAndBack()
    {
        var shoulder = new Vector3(0.26f, 0f, 0f);
        var hand = new Vector3(0.26f, 0.55f, 0f);
        var pole = Pole(
            shoulder,
            hand,
            ArmMath.ElbowHint(shoulder, hand, Right, Up, Forward, false)
        );
        Assert.True(pole.Length() > 0.9f, "pole must stay defined when the hand is straight up");
        Assert.True(pole.X > 0.5f, $"pole {pole} should point out");
        Assert.True(pole.Z < 0f, $"pole {pole} should point back");
    }

    [Fact]
    public void ElbowHint_FollowsBodyYaw()
    {
        // Body turned 90 degrees right: forward is +X, right is -Z.
        var right = -Vector3.UnitZ;
        var forward = Vector3.UnitX;
        var shoulder = new Vector3(0f, 0f, -0.26f);
        var hand = new Vector3(0.28f, 0.22f, -0.18f);
        var pole = Pole(
            shoulder,
            hand,
            ArmMath.ElbowHint(shoulder, hand, right, Up, forward, false)
        );
        Assert.True(pole.Y < -0.5f, $"pole {pole} should point down");
        Assert.True(pole.Z < 0f, $"pole {pole} should point to the body's right (-Z)");
    }

    [Fact]
    public void BendDirection_IsElbowOffsetOffTheShoulderHandLine()
    {
        var d = ArmMath.BendDirection(
            Vector3.Zero,
            new Vector3(0.2f, -0.1f, 0.3f),
            new Vector3(0f, 0f, 0.5f)
        );
        TestUtil.Near(Vector3.Normalize(new Vector3(0.2f, -0.1f, 0f)), d);
    }

    [Fact]
    public void BendDirection_StraightArm_IsZero()
    {
        var d = ArmMath.BendDirection(
            Vector3.Zero,
            new Vector3(0f, 0f, 0.3f),
            new Vector3(0f, 0f, 0.6f)
        );
        TestUtil.Near(Vector3.Zero, d);
    }

    /// <summary>Applies a two-bone solution: the upper arm turns about the shoulder, the forearm takes its own
    /// world rotation about the new elbow.</summary>
    static (Vector3 Elbow, Vector3 Hand) Apply(
        Vector3 s,
        Vector3 e,
        Vector3 h,
        Quaternion upper,
        Quaternion lower
    )
    {
        var elbow = s + Vector3.Transform(e - s, upper);
        return (elbow, elbow + Vector3.Transform(h - e, lower));
    }

    // A bent left arm hanging forward, elbow down and out, as the IK leaves it before the spine leans.
    static readonly Vector3 S = new(-0.2f, 1.2f, 0f);
    static readonly Vector3 E = new(-0.3f, 0.95f, 0.25f);
    static readonly Vector3 H = new(-0.2f, 1.1f, 0.55f);
    static readonly Vector3 Hint = new(-0.6f, 0.6f, 0.1f);

    [Fact]
    public void SolveTwoBone_HandOnTarget_ChangesNothing()
    {
        ArmMath.SolveTwoBone(S, E, H, H, Hint, out var upper, out var lower);
        TestUtil.SameRotation(Quaternion.Identity, upper, 0.05f);
        TestUtil.SameRotation(Quaternion.Identity, lower, 0.05f);
    }

    [Fact]
    public void SolveTwoBone_ReachableTarget_PutsTheHandThereAndKeepsTheElbowSide()
    {
        // The spine leaned, so the shoulder came 0.15 m forward and down with the arm; the hand goes back on target.
        var shift = new Vector3(0f, -0.05f, 0.15f);
        ArmMath.SolveTwoBone(
            S + shift,
            E + shift,
            H + shift,
            H,
            Hint,
            out var upper,
            out var lower
        );
        var (elbow, hand) = Apply(S + shift, E + shift, H + shift, upper, lower);
        TestUtil.Near(H, hand, 1e-3f);
        TestUtil.Near(Vector3.Distance(S, E), Vector3.Distance(S + shift, elbow), 1e-4f);
        var before = ArmMath.BendDirection(S, E, H);
        var after = ArmMath.BendDirection(S + shift, elbow, hand);
        Assert.True(Vector3.Dot(before, after) > 0.8f, $"elbow flipped: {before} to {after}");
    }

    [Fact]
    public void SolveTwoBone_OutOfReach_PointsTheStraightArmAtTheTarget()
    {
        var target = S + new Vector3(0f, 0f, 2f);
        ArmMath.SolveTwoBone(S, E, H, target, Hint, out var upper, out var lower);
        var (elbow, hand) = Apply(S, E, H, upper, lower);
        var dir = Vector3.Normalize(target - S);
        TestUtil.Near(dir, Vector3.Normalize(hand - S), 0.02f);
        TestUtil.Near(dir, Vector3.Normalize(elbow - S), 0.02f);
    }

    [Fact]
    public void SolveTwoBone_StraightArm_BendsTowardTheHint()
    {
        var s = Vector3.Zero;
        var e = new Vector3(0f, 0f, 0.3f);
        var h = new Vector3(0f, 0f, 0.6f);
        var hint = new Vector3(0f, -0.5f, 0.3f);
        ArmMath.SolveTwoBone(
            s,
            e,
            h,
            new Vector3(0f, 0f, 0.4f),
            hint,
            out var upper,
            out var lower
        );
        var (elbow, hand) = Apply(s, e, h, upper, lower);
        TestUtil.Near(new Vector3(0f, 0f, 0.4f), hand, 1e-3f);
        Assert.True(elbow.Y < -0.1f, $"elbow went to y {elbow.Y:0.000}, away from the hint");
    }

    [Fact]
    public void SolveTwoBone_NearlyStraightArm_BendsTowardTheHintNotItsNoise()
    {
        // The IK left an out-of-reach arm straight but for a 1 cm upward kink; the lean then brought the shoulder
        // in. That kink's plane is noise: the elbow must still go down toward the hint (vr.08 "elbow points up").
        var s = Vector3.Zero;
        var e = new Vector3(0f, 0.01f, 0.3f);
        var h = new Vector3(0f, 0f, 0.6f);
        var hint = new Vector3(0f, -0.5f, 0.3f);
        var target = new Vector3(0f, 0f, 0.4f);
        ArmMath.SolveTwoBone(s, e, h, target, hint, out var upper, out var lower);
        var (elbow, hand) = Apply(s, e, h, upper, lower);
        TestUtil.Near(target, hand, 1e-3f);
        TestUtil.Near(Vector3.Distance(s, e), Vector3.Distance(s, elbow), 1e-4f);
        Assert.True(elbow.Y < -0.1f, $"elbow went to y {elbow.Y:0.000}, the kink's side");
    }

    [Fact]
    public void BendOffset_IsTheElbowsDistanceOffTheLine()
    {
        var off = ArmMath.BendOffset(
            Vector3.Zero,
            new Vector3(0f, -0.02f, 0.3f),
            new Vector3(0f, 0f, 0.6f)
        );
        TestUtil.Near(new Vector3(0f, -0.02f, 0f), off, 1e-5f);
    }
}
