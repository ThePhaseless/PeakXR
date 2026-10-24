namespace PeakXR.Core.Tests;

public class ArmTwistTests
{
    static readonly Vector3 Axis = Vector3.UnitY;
    const float Deg = MathF.PI / 180f;

    static Quaternion About(Vector3 axis, float degrees) =>
        Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), degrees * Deg);

    static float Degrees(float radians) => radians / Deg;

    [Fact]
    public void TwistAngle_PureTwist_IsTheAngle()
    {
        TestUtil.Near(60f, Degrees(ArmTwist.TwistAngle(About(Axis, 60f), Axis)), 0.01f);
        TestUtil.Near(-135f, Degrees(ArmTwist.TwistAngle(About(Axis, -135f), Axis)), 0.01f);
    }

    [Fact]
    public void TwistAngle_NegatedQuaternion_TakesTheShortWay()
    {
        var q = About(Axis, 100f);
        var negated = Quaternion.Negate(q);
        TestUtil.Near(100f, Degrees(ArmTwist.TwistAngle(negated, Axis)), 0.01f);
        // 250 degrees one way is 110 the other.
        TestUtil.Near(-110f, Degrees(ArmTwist.TwistAngle(About(Axis, 250f), Axis)), 0.01f);
    }

    [Fact]
    public void Decompose_SwingThenTwist_RecomposesWithASwingOffTheAxis()
    {
        var q = Quaternion.Multiply(About(Vector3.UnitX, 30f), About(Axis, 70f));
        ArmTwist.Decompose(q, Axis, out var swing, out var twist);
        TestUtil.SameRotation(q, Quaternion.Multiply(swing, twist), 0.05f);
        TestUtil.Near(70f, Degrees(ArmTwist.TwistAngle(twist, Axis)), 0.05f);
        TestUtil.Near(0f, Vector3.Dot(new Vector3(swing.X, swing.Y, swing.Z), Axis), 1e-4f);
    }

    [Fact]
    public void Decompose_HalfTurnSwing_GivesNoTwist()
    {
        ArmTwist.Decompose(About(Vector3.UnitX, 180f), Axis, out var swing, out var twist);
        TestUtil.SameRotation(Quaternion.Identity, twist);
        TestUtil.SameRotation(About(Vector3.UnitX, 180f), swing);
    }

    [Theory]
    [InlineData(0f, 50f, 50f)]
    [InlineData(170f, -175f, 185f)]
    [InlineData(-170f, 175f, -185f)]
    [InlineData(220f, -130f, -130f)]
    [InlineData(-220f, 130f, 130f)]
    public void Unwrap_StaysContinuousUntilPastTheHysteresis(float previous, float raw, float want)
    {
        TestUtil.Near(want, Degrees(ArmTwist.Unwrap(raw * Deg, previous * Deg)), 0.01f);
    }

    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(120f, 60f, 60f)]
    [InlineData(-80f, -40f, -40f)]
    [InlineData(180f, 90f, 90f)]
    [InlineData(210f, 90f, 90f)]
    [InlineData(-225f, -90f, -90f)]
    public void Split_SharesTheTwistAndClampsTheWrist(float total, float forearm, float wrist)
    {
        ArmTwist.Split(total * Deg, out var f, out var w);
        TestUtil.Near(forearm, Degrees(f), 0.01f);
        TestUtil.Near(wrist, Degrees(w), 0.01f);
    }

    [Fact]
    public void Spread_WristRolled120_KeepsTheHandAndHalvesTheWristTwist()
    {
        var swing = About(new Vector3(1f, 0f, 0.4f), 35f);
        var relative = Quaternion.Multiply(swing, About(Axis, 120f));
        float twist = 0f;
        var wrist = ArmTwist.Spread(relative, Axis, ref twist, out var roll);
        TestUtil.SameRotation(relative, Quaternion.Multiply(roll, wrist), 0.05f);
        TestUtil.Near(60f, Degrees(ArmTwist.TwistAngle(roll, Axis)), 0.05f);
        TestUtil.Near(60f, Degrees(ArmTwist.TwistAngle(wrist, Axis)), 0.05f);
        TestUtil.Near(120f, Degrees(twist), 0.05f);
    }

    [Fact]
    public void Spread_PastTheRange_ClampsTheRollButKeepsThePointingDirection()
    {
        var swing = About(Vector3.UnitZ, 40f);
        var relative = Quaternion.Multiply(swing, About(Axis, 200f));
        float twist = 170f * Deg;
        var wrist = ArmTwist.Spread(relative, Axis, ref twist, out var roll);
        var hand = Quaternion.Multiply(roll, wrist);
        TestUtil.Near(Vector3.Transform(Axis, relative), Vector3.Transform(Axis, hand), 1e-3f);
        TestUtil.Near(90f, Degrees(ArmTwist.TwistAngle(roll, Axis)), 0.05f);
        TestUtil.Near(90f, MathF.Abs(Degrees(ArmTwist.TwistAngle(wrist, Axis))), 0.05f);
    }

    [Fact]
    public void Spread_RollSweptAcrossTheHalfTurn_NeverFlipsTheForearm()
    {
        var swing = About(Vector3.UnitX, 20f);
        float twist = 0f;
        float lastRoll = 0f;
        for (float a = 0f; a <= 220f; a += 2f)
        {
            var relative = Quaternion.Multiply(swing, About(Axis, a));
            ArmTwist.Spread(relative, Axis, ref twist, out var roll);
            float r = Degrees(ArmTwist.TwistAngle(roll, Axis));
            Assert.True(
                MathF.Abs(r - lastRoll) <= 1.5f,
                $"forearm roll jumped {lastRoll} -> {r} at {a}"
            );
            lastRoll = r;
        }
        // And back again without jumping the other way.
        for (float a = 220f; a >= -40f; a -= 2f)
        {
            var relative = Quaternion.Multiply(swing, About(Axis, a));
            ArmTwist.Spread(relative, Axis, ref twist, out var roll);
            float r = Degrees(ArmTwist.TwistAngle(roll, Axis));
            Assert.True(
                MathF.Abs(r - lastRoll) <= 1.5f,
                $"forearm roll jumped {lastRoll} -> {r} at {a}"
            );
            lastRoll = r;
        }
    }

    [Fact]
    public void Spread_HeldStillAtTheHalfTurn_DoesNotAlternate()
    {
        // Jitter of a degree around 180: the old path flipped between +180 and -180 every tick.
        float twist = 0f;
        var relative = About(Axis, 170f);
        ArmTwist.Spread(relative, Axis, ref twist, out var first);
        float start = ArmTwist.TwistAngle(first, Axis);
        for (int i = 0; i < 40; i++)
        {
            relative = About(Axis, i % 2 == 0 ? 179.5f : 180.5f);
            ArmTwist.Spread(relative, Axis, ref twist, out var roll);
            Assert.True(
                MathF.Abs(ArmTwist.TwistAngle(roll, Axis) - start) < 10f * Deg,
                $"forearm roll flipped at tick {i}"
            );
        }
    }

    [Fact]
    public void Spread_NoTwist_IsIdentityRoll()
    {
        float twist = 0f;
        var swing = About(Vector3.UnitX, 45f);
        var wrist = ArmTwist.Spread(swing, Axis, ref twist, out var roll);
        TestUtil.SameRotation(Quaternion.Identity, roll);
        TestUtil.SameRotation(swing, wrist);
    }
}
