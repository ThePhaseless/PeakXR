namespace PeakXR.Core.Tests;

public class AimMathTests
{
    static readonly Vector3 Forward = new(0, 0, 1);

    [Fact]
    public void TwoHand_SupportInFront_AimsFromHoldToSupport()
    {
        var dir = AimMath.TwoHandDirection(
            new Vector3(0, 0, 0),
            new Vector3(0, 0.3f, 0.3f),
            Forward,
            0.1f
        );
        TestUtil.Near(Vector3.Normalize(new Vector3(0, 1, 1)), dir);
    }

    [Fact]
    public void TwoHand_SupportBehind_StillAimsForward()
    {
        var dir = AimMath.TwoHandDirection(
            new Vector3(0, 0, 0.4f),
            new Vector3(0, 0, 0),
            Forward,
            0.1f
        );
        TestUtil.Near(Forward, dir);
    }

    [Fact]
    public void TwoHand_HandsTooClose_UsesHoldingHandForward()
    {
        var dir = AimMath.TwoHandDirection(Vector3.Zero, new Vector3(0.05f, 0, 0), Forward, 0.1f);
        TestUtil.Near(Forward, dir);
    }

    [Fact]
    public void AngularSize_IsChordOfTheAngleAtDistance()
    {
        TestUtil.Near(2f * 10f * MathF.Tan(MathF.PI / 6f), AimMath.AngularSize(10f, 60f));
    }

    [Fact]
    public void PingScale_ClampsToTheMarkerRange()
    {
        Assert.Equal(0.2f, AimMath.PingMarkerScale(0.1f, 60f, 0.2f, 3f));
        Assert.Equal(3f, AimMath.PingMarkerScale(500f, 60f, 0.2f, 3f));
        TestUtil.Near(AimMath.AngularSize(2f, 60f), AimMath.PingMarkerScale(2f, 60f, 0.2f, 3f));
    }

    [Fact]
    public void PingScale_GrowsWithTheReferenceAngle()
    {
        Assert.True(
            AimMath.PingMarkerScale(2f, 40f, 0.2f, 3f) < AimMath.PingMarkerScale(2f, 80f, 0.2f, 3f)
        );
    }
}
