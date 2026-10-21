namespace PeakXR.Core.Tests;

public class RopeClimbTests
{
    // A 10 m vertical rope whose percent grows downward (anchor at the top), climbed at 2 m/s at full stick:
    // the game's percent rate for stick +1 is -0.2 per second (RopeClimbingAPI.GetMove is -1/length).
    static readonly Vector3 DownTheRope = new(0, -10, 0);
    const float RatePerStick = -0.2f;

    [Fact]
    public void BodyUp_IsPositiveStick()
    {
        float y = RopeClimb.StickAlong(new Vector3(0, 1, 0), DownTheRope, RatePerStick, 3f, 0f);
        TestUtil.Near(0.5f, y);
    }

    [Fact]
    public void BodyDown_UsesTheGamesFasterDescent()
    {
        // The game moves 3x faster for negative stick (CharacterRopeHandling.cs:63), so 3 m/s down needs -0.5.
        float y = RopeClimb.StickAlong(new Vector3(0, -3, 0), DownTheRope, RatePerStick, 3f, 0f);
        TestUtil.Near(-0.5f, y);
    }

    [Fact]
    public void SidewaysMotion_DoesNotClimb()
    {
        float y = RopeClimb.StickAlong(new Vector3(1, 0, 1), DownTheRope, RatePerStick, 3f, 0.05f);
        Assert.Equal(0f, y);
    }

    [Fact]
    public void FastPull_ClampsToFullStick()
    {
        float y = RopeClimb.StickAlong(new Vector3(0, 9, 0), DownTheRope, RatePerStick, 3f, 0f);
        TestUtil.Near(1f, y);
    }

    [Fact]
    public void SmallMotion_IsInsideDeadzone()
    {
        float y = RopeClimb.StickAlong(
            new Vector3(0, 0.1f, 0),
            DownTheRope,
            RatePerStick,
            3f,
            0.1f
        );
        Assert.Equal(0f, y);
    }

    [Fact]
    public void FlippedRope_FlipsTheSign()
    {
        // Same rope with percent growing upward and the game's rate sign flipped (UpMult): still up = positive.
        float y = RopeClimb.StickAlong(new Vector3(0, 1, 0), -DownTheRope, -RatePerStick, 3f, 0f);
        TestUtil.Near(0.5f, y);
    }

    [Fact]
    public void Vine_MovesAlongTheSlope()
    {
        // A 20 m horizontal vine along +X, percent growing along +X, 2 m/s at full stick: no descent multiplier.
        var along = new Vector3(20, 0, 0);
        TestUtil.Near(-0.5f, RopeClimb.StickAlong(new Vector3(-1, 0, 0), along, 0.1f, 1f, 0f));
        TestUtil.Near(0.5f, RopeClimb.StickAlong(new Vector3(1, 0, 0), along, 0.1f, 1f, 0f));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(float.NaN)]
    public void DegenerateRate_GivesZero(float rate)
    {
        Assert.Equal(0f, RopeClimb.StickAlong(new Vector3(0, 1, 0), DownTheRope, rate, 3f, 0f));
    }

    [Fact]
    public void DegenerateAxis_GivesZero()
    {
        Assert.Equal(
            0f,
            RopeClimb.StickAlong(new Vector3(0, 1, 0), Vector3.Zero, RatePerStick, 3f, 0f)
        );
    }

    [Fact]
    public void SegmentDistance_MeasuresToTheClosestPointOnTheAxis()
    {
        var a = new Vector3(0, 0, 0);
        var b = new Vector3(0, 2, 0);
        TestUtil.Near(0.01f, RopeClimb.SegmentDistanceSq(new Vector3(0.1f, 1, 0), a, b));
        TestUtil.Near(1f, RopeClimb.SegmentDistanceSq(new Vector3(0, 3, 0), a, b));
        TestUtil.Near(0.25f, RopeClimb.SegmentDistanceSq(new Vector3(0, -0.5f, 0), a, b));
        TestUtil.Near(4f, RopeClimb.SegmentDistanceSq(new Vector3(2, 0, 0), a, a));
    }
}
