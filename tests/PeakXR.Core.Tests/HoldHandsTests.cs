namespace PeakXR.Core.Tests;

public class HoldHandsTests
{
    [Theory]
    [InlineData(true, true, true, true)]
    // The other hand only brushed it, or let go too: no handoff, the release drops or throws as usual.
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    public void PrimaryOpens_HandsOffOnlyToASecondHandStillGripping(
        bool twoHand,
        bool otherGripClosed,
        bool otherIsSecondHand,
        bool expected
    ) => Assert.Equal(expected, HoldHands.HandOff(twoHand, otherGripClosed, otherIsSecondHand));

    [Fact]
    public void Ease_StartsAtTheOldPoseAndSettles()
    {
        Assert.Equal(0f, HoldHands.Ease(0f, 0.25f));
        Assert.Equal(0.5f, HoldHands.Ease(0.125f, 0.25f), 4);
        Assert.Equal(1f, HoldHands.Ease(0.25f, 0.25f));
        Assert.Equal(1f, HoldHands.Ease(3f, 0.25f));
        Assert.Equal(1f, HoldHands.Ease(-1f, 0.25f));
        Assert.True(HoldHands.Ease(0.05f, 0.25f) < 0.2f);
    }
}
