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
    public void ContinueYaw_WrapsOncePastTheLimit()
    {
        // One turn back at a time: a remote peer interpolating the synced value sees one spin, rarely.
        TestUtil.Near(365f, LookMath.ContinueYaw(715f, 5f), 1e-3f);
        TestUtil.Near(-365f, LookMath.ContinueYaw(-715f, -5f), 1e-3f);
    }
}
