namespace PeakXR.Core.Tests;

public class LungeIntentTests
{
    const float Min = 2.5f;

    [Fact]
    public void BriskReachFromChestHeight_IsNoLunge() =>
        // External review #16: a fast hand-over-hand reach lets go at chest height on its way up.
        Assert.False(LungeIntent.AtRelease(3f, Min, handBelowHead: 0.5f));

    [Fact]
    public void FlingAboveTheShoulder_IsACandidate() =>
        Assert.True(LungeIntent.AtRelease(4f, Min, handBelowHead: 0.05f));

    [Fact]
    public void SlowReleaseAboveTheShoulder_IsNoLunge() =>
        Assert.False(LungeIntent.AtRelease(1.5f, Min, handBelowHead: 0f));

    [Fact]
    public void FlingStillRisingAfterTheDelay_Lunges() =>
        Assert.True(LungeIntent.Confirm(2.5f, Min, gripClosedAgain: false));

    [Fact]
    public void ReachThatStopsToGrab_IsNoLunge()
    {
        // The hand slows as it arrives at the next hold, or closes on it.
        Assert.False(LungeIntent.Confirm(0.5f, Min, gripClosedAgain: false));
        Assert.False(LungeIntent.Confirm(3f, Min, gripClosedAgain: true));
    }
}
