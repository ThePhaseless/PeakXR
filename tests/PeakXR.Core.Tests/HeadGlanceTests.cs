namespace PeakXR.Core.Tests;

public class HeadGlanceTests
{
    static readonly Vector3 Forward = new(0f, 0f, 1f);

    static float Deg(Vector3 a, Vector3 b) =>
        MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)), -1f, 1f))
        * 180f
        / MathF.PI;

    static float Pitch(Vector3 d) => MathF.Asin(Vector3.Normalize(d).Y) * 180f / MathF.PI;

    [Fact]
    public void TargetAhead_KeepsTheView()
    {
        var d = HeadGlance.Aim(Forward, new Vector3(0f, 0f, 2f));
        Assert.True(Deg(d, Forward) < 0.01f);
    }

    [Fact]
    public void TargetToTheSide_TurnsMostOfTheWayButNotExactly()
    {
        var to = new Vector3(0.6f, -0.3f, 0.4f);
        var d = HeadGlance.Aim(Forward, to);
        float left = Deg(d, to);
        Assert.InRange(left, 0.5f, HeadGlance.MaxLeftToEyes + 0.01f);
        Assert.True(d.X > 0f && d.Y < 0f);
        Assert.True(HeadGlance.Aim(Forward, new Vector3(-0.6f, 0f, 0.4f)).X < 0f);
    }

    [Fact]
    public void OverheadTarget_PitchesUpToTheNeckLimitAndStaysInView()
    {
        var to = new Vector3(0f, 1f, 0.05f);
        var d = HeadGlance.Aim(Forward, to);
        Assert.InRange(Pitch(d), HeadGlance.MaxUp - 0.5f, HeadGlance.MaxUp + 0.01f);
        Assert.True(Deg(d, to) < 40f);
    }

    [Fact]
    public void LowTarget_NeverPitchesPastTheNeck()
    {
        var d = HeadGlance.Aim(Forward, new Vector3(0f, -1.2f, 0.1f));
        Assert.True(Pitch(d) >= -HeadGlance.MaxDown - 0.01f);
    }

    [Fact]
    public void TargetBehind_YawIsCapped()
    {
        var d = HeadGlance.Aim(Forward, new Vector3(-0.5f, 0f, -0.5f));
        var flat = new Vector3(d.X, 0f, d.Z);
        Assert.True(Deg(flat, Forward) <= HeadGlance.MaxYaw + 0.01f);
    }

    static void Unit(Vector3 d)
    {
        Assert.True(float.IsFinite(d.X) && float.IsFinite(d.Y) && float.IsFinite(d.Z), $"{d}");
        TestUtil.Near(1f, d.Length());
    }

    [Fact]
    public void TargetAtTheHead_KeepsTheLevelHeading()
    {
        // A heading that is not unit length or not level still gives the level heading.
        var heading = new Vector3(0f, 0.4f, 3f);
        TestUtil.Near(Forward, HeadGlance.Aim(heading, Vector3.Zero));
        TestUtil.Near(Forward, HeadGlance.Aim(heading, new Vector3(0f, 1e-7f, 0f)));
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(-1f)]
    public void StraightUpOrDown_PitchesToTheNeckLimitOnTheHeading(float y)
    {
        var d = HeadGlance.Aim(Forward, new Vector3(0f, y, 0f));
        Unit(d);
        Assert.Equal(y > 0f ? HeadGlance.MaxUp : -HeadGlance.MaxDown, Pitch(d), 2);
        Assert.True(MathF.Abs(d.X) < 1e-4f && d.Z > 0f);
    }

    [Fact]
    public void StraightBehind_StaysFiniteAndWithinTheNeck()
    {
        var d = HeadGlance.Aim(Forward, new Vector3(0f, 0f, -2f));
        Unit(d);
        var flat = new Vector3(d.X, 0f, d.Z);
        Assert.True(Deg(flat, Forward) <= HeadGlance.MaxYaw + 0.01f);
    }
}
