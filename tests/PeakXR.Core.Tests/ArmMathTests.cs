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
}
