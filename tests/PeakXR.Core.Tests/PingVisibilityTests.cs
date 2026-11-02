namespace PeakXR.Core.Tests;

public class PingVisibilityTests
{
    // The prefab's values (resources.assets, #130): visibilityFullNoneNoLos (30, 75), NoLosVisibilityMul 0.5.
    const float Full = 30f;
    const float None = 75f;
    const float Mul = 0.5f;

    [Theory]
    [InlineData(30f, false, true)]
    [InlineData(30f, true, true)]
    [InlineData(52.4f, true, true)]
    [InlineData(52.5f, true, false)]
    [InlineData(52.5f, false, true)]
    [InlineData(74.9f, false, true)]
    [InlineData(75f, false, false)]
    [InlineData(75f, true, false)]
    public void Shown_MirrorsReceivePointRpc(float distance, bool blocked, bool expected) =>
        Assert.Equal(expected, PingVisibility.Shown(distance, blocked, Full, None, Mul));

    [Fact]
    public void Shown_NoFadeSpan_AlwaysShows() =>
        // Mathf.InverseLerp returns 0 when its ends meet (decomp PointPinger.cs:91), so the check never drops it.
        Assert.True(PingVisibility.Shown(1000f, true, Full, None, 0f));
}
