namespace PeakXR.Core.Tests;

public class ViewMotionLimitTests
{
    const float Dt = 1f / 90f;

    /// <summary>The chase's own exponential follow (RigMath.Follow at Camera.ThirdPersonFollow 0.8/s) from the
    /// limited position toward <paramref name="target"/>: the goal the limiter is fed each frame.</summary>
    static Vector3 Follow(Vector3 pos, Vector3 target) =>
        pos + (target - pos) * (1f - MathF.Exp(-0.8f * Dt));

    [Fact]
    public void AGoalJump_IsTakenAtTheCappedAcceleration()
    {
        // The watched body drops away 3 m in one frame: the view eases after it, never jerking.
        var m = new ViewMotionLimit();
        var pos = Vector3.Zero;
        var target = new Vector3(0f, -3f, 0f);
        var lastV = Vector3.Zero;
        for (int i = 0; i < 900; i++)
        {
            var next = m.Step(pos, Follow(pos, target), target, Dt);
            var v = (next - pos) / Dt;
            Assert.True(
                (v - lastV).Length() <= m.MaxAcceleration * Dt + 1e-3f,
                $"frame {i}: dv {(v - lastV).Length() / Dt:0.0} m/s^2"
            );
            Assert.True(v.Length() <= m.MaxSpeed + 1e-3f);
            lastV = v;
            pos = next;
        }
        Assert.True(Vector3.Distance(pos, target) < 0.05f, $"ended {pos}");
    }

    [Fact]
    public void ASteadyFollow_KeepsPaceWithTheBody()
    {
        // A body walking at 1.5 m/s: once eased in, the view moves at its pace.
        var m = new ViewMotionLimit();
        var pos = Vector3.Zero;
        for (int i = 0; i < 1200; i++)
        {
            var target = new Vector3(1.5f * Dt * (i + 1), 0f, 0f);
            pos = m.Step(pos, Follow(pos, target), target, Dt);
        }
        TestUtil.Near(1.5f, m.Velocity.Length(), 0.05f);
    }

    [Theory]
    [InlineData(0.8f)]
    [InlineData(2f)]
    [InlineData(5f)]
    public void AStoppingBody_IsNeverOvershot(float rate)
    {
        // Review: at follow rate 5 a body running at 6 m/s and stopping was overshot by 1.9-4.8 m.
        var m = new ViewMotionLimit();
        var pos = Vector3.Zero;
        var body = Vector3.Zero;
        float worst = 0f;
        for (int i = 0; i < 1800; i++)
        {
            if (i < 270)
                body.X += 6f * Dt;
            var follow = pos + (body - pos) * (1f - MathF.Exp(-rate * Dt));
            pos = m.Step(pos, follow, body, Dt);
            worst = MathF.Max(worst, pos.X - body.X);
        }
        Assert.True(worst < 0.02f, $"overshot by {worst:0.00} m at rate {rate}");
        Assert.True(MathF.Abs(pos.X - body.X) < 0.05f, $"ended {body.X - pos.X:0.00} m short");
    }

    [Fact]
    public void Reset_StopsAtOnce()
    {
        var m = new ViewMotionLimit();
        m.Step(Vector3.Zero, new Vector3(5f, 0f, 0f), new Vector3(5f, 0f, 0f), Dt);
        m.Reset();
        Assert.Equal(Vector3.Zero, m.Velocity);
        Assert.Equal(Vector3.Zero, m.Step(Vector3.Zero, Vector3.Zero, Vector3.Zero, Dt));
    }

    [Fact]
    public void ZeroOrBadTime_HoldsStill()
    {
        var m = new ViewMotionLimit();
        Assert.Equal(
            Vector3.One,
            m.Step(Vector3.One, new Vector3(9f, 9f, 9f), new Vector3(9f, 9f, 9f), 0f)
        );
        Assert.Equal(
            Vector3.One,
            m.Step(Vector3.One, new Vector3(9f, 9f, 9f), new Vector3(9f, 9f, 9f), float.NaN)
        );
    }

    [Fact]
    public void ArtificialSpeed_IgnoresAReplacement()
    {
        // A move over 1.5 m in a frame is a re-place behind a fade or a snap, not motion to soften.
        Assert.Equal(0f, ViewMotionLimit.ArtificialSpeed(new Vector3(3f, 0f, 0f), Dt));
        TestUtil.Near(2f, ViewMotionLimit.ArtificialSpeed(new Vector3(2f * Dt, 0f, 0f), Dt), 1e-3f);
        Assert.Equal(0f, ViewMotionLimit.ArtificialSpeed(Vector3.One, 0f));
    }
}
