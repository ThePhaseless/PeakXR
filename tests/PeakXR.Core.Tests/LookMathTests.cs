namespace PeakXR.Core.Tests;

public class LookMathTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(180, 180)]
    [InlineData(-180, 180)]
    [InlineData(190, -170)]
    [InlineData(-190, 170)]
    [InlineData(540, 180)]
    [InlineData(725, 5)]
    [InlineData(-359, 1)]
    public void WrapDegrees_MapsToHalfOpenRange(float input, float expected)
    {
        TestUtil.Near(expected, LookMath.WrapDegrees(input), 1e-3f);
    }

    [Fact]
    public void ToLookValues_YawIsX()
    {
        var v = LookMath.ToLookValues(UnityQuat.Euler(0, 270, 0));
        TestUtil.Near(-90f, v.X, 1e-3f);
        TestUtil.Near(0f, v.Y, 1e-3f);
    }

    [Fact]
    public void ToLookValues_LookingUpIsPositiveY()
    {
        // Unity Euler negative X pitches the head up.
        var v = LookMath.ToLookValues(UnityQuat.Euler(-30, 45, 0));
        TestUtil.Near(45f, v.X, 1e-3f);
        TestUtil.Near(30f, v.Y, 1e-3f);
    }

    [Fact]
    public void ToLookValues_PitchClampedTo85()
    {
        TestUtil.Near(-85f, LookMath.ToLookValues(UnityQuat.Euler(89, 10, 0)).Y, 1e-3f);
        TestUtil.Near(85f, LookMath.ToLookValues(UnityQuat.Euler(-89, 10, 0)).Y, 1e-3f);
    }

    [Fact]
    public void ToLookValues_IgnoresRoll()
    {
        var v = LookMath.ToLookValues(UnityQuat.Euler(-20, 100, 40));
        TestUtil.Near(100f, v.X, 1e-3f);
        TestUtil.Near(20f, v.Y, 1e-3f);
    }

    [Fact]
    public void ToLookValues_StraightDown_KeepsHeading()
    {
        var v = LookMath.ToLookValues(UnityQuat.Euler(90, 60, 0));
        TestUtil.Near(60f, v.X, 0.05f);
        TestUtil.Near(-85f, v.Y, 1e-3f);
    }

    [Theory]
    [InlineData(170f, -170f, 190f)]
    [InlineData(-170f, 170f, -190f)]
    [InlineData(400f, 50f, 410f)]
    [InlineData(10f, 20f, 20f)]
    public void ContinueYaw_StaysContinuousAcrossTheSeam(float previous, float next, float expected)
    {
        TestUtil.Near(expected, LookMath.ContinueYaw(previous, next), 1e-3f);
    }

    [Fact]
    public void ContinueYaw_KeepsAccumulatingPastTwoTurns()
    {
        // Remote peers lerp the synced yaw, so every pull-back is a visible spin: none for normal play.
        TestUtil.Near(725f, LookMath.ContinueYaw(715f, 5f), 1e-3f);
        TestUtil.Near(-725f, LookMath.ContinueYaw(-715f, -5f), 1e-3f);
    }

    [Fact]
    public void ContinueYaw_PullsBackFiveTurnsOnlyWhereHalfPrecisionDrops()
    {
        // Half floats step 1 degree up to 2048, 2 degrees above: wrap there, by five turns at once.
        TestUtil.Near(2050f - 1800f, LookMath.ContinueYaw(2045f, 2050f - 1800f), 1e-3f);
        TestUtil.Near(-250f, LookMath.ContinueYaw(-2045f, -250f), 1e-3f);
        TestUtil.Near(2040f, LookMath.ContinueYaw(2035f, 2040f - 1800f), 1e-3f);
    }
}
