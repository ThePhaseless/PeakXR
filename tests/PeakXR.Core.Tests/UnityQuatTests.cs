namespace PeakXR.Core.Tests;

public class UnityQuatTests
{
    static readonly Vector3 Fwd = new(0, 0, 1);
    static readonly Vector3 Up = new(0, 1, 0);
    static readonly Vector3 Right = new(1, 0, 0);

    [Fact]
    public void Euler_YawRight_TurnsForwardToRight()
    {
        TestUtil.Near(Right, UnityQuat.Rotate(UnityQuat.Euler(0, 90, 0), Fwd));
    }

    [Fact]
    public void Euler_PositiveX_PitchesForwardDown()
    {
        TestUtil.Near(new Vector3(0, -1, 0), UnityQuat.Rotate(UnityQuat.Euler(90, 0, 0), Fwd));
    }

    [Fact]
    public void Euler_PositiveZ_RollsUpTowardMinusX()
    {
        // Unity: Quaternion.Euler(0,0,90) * Vector3.up == Vector3.left
        TestUtil.Near(new Vector3(-1, 0, 0), UnityQuat.Rotate(UnityQuat.Euler(0, 0, 90), Up));
    }

    [Fact]
    public void Euler_AppliesZThenXThenY()
    {
        var expected = UnityQuat.Mul(
            UnityQuat.Mul(
                Quaternion.CreateFromAxisAngle(Up, Rad(40)),
                Quaternion.CreateFromAxisAngle(Right, Rad(25))
            ),
            Quaternion.CreateFromAxisAngle(Fwd, Rad(-70))
        );
        TestUtil.SameRotation(expected, UnityQuat.Euler(25, 40, -70));
    }

    [Fact]
    public void Mul_MatchesUnityOrder_RightOperandAppliedFirst()
    {
        var yaw90 = Quaternion.CreateFromAxisAngle(Up, Rad(90));
        var pitch90 = Quaternion.CreateFromAxisAngle(Right, Rad(90));
        // Unity: (yaw90 * pitch90) * right: pitch first leaves right unchanged, then yaw gives back.
        TestUtil.Near(
            new Vector3(0, 0, -1),
            UnityQuat.Rotate(UnityQuat.Mul(yaw90, pitch90), Right)
        );
        TestUtil.Near(new Vector3(0, 1, 0), UnityQuat.Rotate(UnityQuat.Mul(pitch90, yaw90), Right));
    }

    [Fact]
    public void Inverse_UndoesRotation()
    {
        var q = UnityQuat.Euler(10, 200, -35);
        var v = new Vector3(0.3f, -2f, 5f);
        TestUtil.Near(v, UnityQuat.Rotate(UnityQuat.Inverse(q), UnityQuat.Rotate(q, v)));
    }

    [Theory]
    [InlineData(30, 45, 60)]
    [InlineData(0, 0, 0)]
    [InlineData(10, 350, 1)]
    [InlineData(80, 120, 270)]
    [InlineData(300, 10, 20)]
    public void ToEuler_RoundTripsInUnityRanges(float x, float y, float z)
    {
        var e = UnityQuat.ToEuler(UnityQuat.Euler(x, y, z));
        TestUtil.Near(new Vector3(x, y, z), e, 1e-2f);
    }

    [Fact]
    public void ToEuler_NegativeAnglesWrapTo0To360()
    {
        TestUtil.Near(new Vector3(350, 0, 0), UnityQuat.ToEuler(UnityQuat.Euler(-10, 0, 0)), 1e-2f);
    }

    [Fact]
    public void ToEuler_RandomRotations_RebuildSameRotation()
    {
        var rng = new Random(1);
        for (int i = 0; i < 500; i++)
        {
            var q = UnityQuat.Euler(
                rng.NextSingle() * 720 - 360,
                rng.NextSingle() * 720 - 360,
                rng.NextSingle() * 720 - 360
            );
            var e = UnityQuat.ToEuler(q);
            Assert.InRange(e.X, 0f, 360f);
            Assert.InRange(e.Y, 0f, 360f);
            Assert.InRange(e.Z, 0f, 360f);
            TestUtil.SameRotation(q, UnityQuat.Euler(e.X, e.Y, e.Z), 0.1f);
        }
    }

    [Fact]
    public void ToEuler_GimbalLock_StillRebuildsRotation()
    {
        var q = UnityQuat.Euler(90, 30, 20);
        var e = UnityQuat.ToEuler(q);
        TestUtil.SameRotation(q, UnityQuat.Euler(e.X, e.Y, e.Z), 0.1f);
    }

    [Fact]
    public void YawOnly_KeepsHeadingDropsPitchAndRoll()
    {
        var q = UnityQuat.Euler(40, 70, 25);
        TestUtil.SameRotation(UnityQuat.Euler(0, 70, 0), UnityQuat.YawOnly(q), 0.01f);
    }

    [Fact]
    public void YawOnly_LookingStraightDown_UsesHeadUpForHeading()
    {
        TestUtil.SameRotation(
            UnityQuat.Euler(0, 120, 0),
            UnityQuat.YawOnly(UnityQuat.Euler(90, 120, 0)),
            0.05f
        );
        TestUtil.SameRotation(
            UnityQuat.Euler(0, 120, 0),
            UnityQuat.YawOnly(UnityQuat.Euler(-90, 120, 0)),
            0.05f
        );
    }

    [Fact]
    public void LookRotation_MatchesEulerForHeadingAndPitch()
    {
        TestUtil.SameRotation(UnityQuat.Euler(0, 90, 0), UnityQuat.LookRotation(Right, Up));
        var f = UnityQuat.Rotate(UnityQuat.Euler(-30, 45, 0), Fwd);
        TestUtil.SameRotation(UnityQuat.Euler(-30, 45, 0), UnityQuat.LookRotation(f, Up));
    }

    [Fact]
    public void LookRotation_RespectsUpForRoll()
    {
        var q = UnityQuat.Euler(0, 0, 90);
        var r = UnityQuat.LookRotation(UnityQuat.Rotate(q, Fwd), UnityQuat.Rotate(q, Up));
        TestUtil.SameRotation(q, r);
    }

    [Fact]
    public void LookRotation_Degenerate_DoesNotProduceNaN()
    {
        var a = UnityQuat.LookRotation(Vector3.Zero, Up);
        Assert.Equal(Quaternion.Identity, a);
        var b = UnityQuat.LookRotation(Up, Up);
        Assert.False(float.IsNaN(b.X) || float.IsNaN(b.W));
        TestUtil.Near(Up, UnityQuat.Rotate(b, Fwd));
    }

    static float Rad(float d) => d * MathF.PI / 180f;
}
