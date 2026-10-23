namespace PeakXR.Core.Tests;

public class SeatedReachTests
{
    // Seated head 1.2 m over the real floor, avatar eyes 1.65 m: the virtual floor is 0.45 m under the real one.
    const float Eye = 1.2f;
    const float Floor = Eye - HeightCalibration.DefaultStandingEyeHeight;
    const float Pivot = Eye - SeatedReach.HipBelowEye;

    static float Y(float hand, float gain = 2f, bool seated = true) =>
        SeatedReach.Y(hand, Eye, Floor, gain, seated);

    [Theory]
    [InlineData(0.6f)]
    [InlineData(1.0f)]
    [InlineData(2.0f)]
    public void AbovePivot_Identity(float hand) => TestUtil.Near(hand, Y(hand), 1e-6f);

    [Fact]
    public void NotSeatedOrGainOne_Identity()
    {
        TestUtil.Near(0.1f, Y(0.1f, seated: false), 1e-6f);
        TestUtil.Near(0.1f, Y(0.1f, gain: 1f), 1e-6f);
        TestUtil.Near(0.1f, Y(0.1f, gain: float.NaN), 1e-6f);
    }

    [Fact]
    public void ContinuousAtPivot() => TestUtil.Near(Pivot, Y(Pivot - 1e-4f), 1e-3f);

    [Fact]
    public void BelowPivot_ScalesDistanceByGain() =>
        TestUtil.Near(Pivot - 0.2f * 2f, Y(Pivot - 0.2f), 1e-5f);

    [Fact]
    public void RealFloorReachesVirtualFloor_NeverBelow()
    {
        Assert.True(Y(0.1f) - Floor < 0.2f);
        TestUtil.Near(Floor, Y(0f, gain: 3f), 1e-6f);
    }

    [Fact]
    public void Monotonic()
    {
        float last = float.NegativeInfinity;
        for (float h = -1f; h <= 2f; h += 0.01f)
        {
            float y = Y(h, gain: 3f);
            Assert.True(y >= last - 1e-6f, $"not monotonic at {h}");
            Assert.True(y >= MathF.Min(h, Floor) - 1e-6f, $"below the floor at {h}");
            last = y;
        }
    }
}
