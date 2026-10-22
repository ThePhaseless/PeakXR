namespace PeakXR.Core.Tests;

public class PingRulesTests
{
    [Theory]
    [InlineData(true, 10f, 0f, 2f, PingVerdict.Fire)]
    [InlineData(true, 10f, 9f, 2f, PingVerdict.TooSoon)]
    [InlineData(true, 10f, 8f, 2f, PingVerdict.Fire)]
    [InlineData(false, 10f, 0f, 2f, PingVerdict.NotNow)]
    [InlineData(false, 10f, 9f, 2f, PingVerdict.NotNow)]
    public void Press_MirrorsPointPingerCanPing(
        bool fullyConscious,
        float now,
        float lastPinged,
        float coolDown,
        PingVerdict expected
    )
    {
        Assert.Equal(expected, PingRules.Press(fullyConscious, now, lastPinged, coolDown));
    }

    [Theory]
    // A fixed hand setting always wins.
    [InlineData(PingAimHand.Left, false, true, false, true)]
    [InlineData(PingAimHand.Right, true, false, true, false)]
    // Pointing: the one pointing hand, else the off hand of the layout (the table's ping button is there).
    [InlineData(PingAimHand.Pointing, true, false, false, true)]
    [InlineData(PingAimHand.Pointing, false, true, true, false)]
    [InlineData(PingAimHand.Pointing, false, false, false, true)]
    [InlineData(PingAimHand.Pointing, false, false, true, false)]
    [InlineData(PingAimHand.Pointing, true, true, false, true)]
    public void ButtonHand_PicksTheAimingHand(
        PingAimHand setting,
        bool leftPointing,
        bool rightPointing,
        bool dominantLeft,
        bool expectedLeft
    )
    {
        Assert.Equal(
            expectedLeft,
            PingRules.ButtonHandLeft(setting, leftPointing, rightPointing, dominantLeft)
        );
    }

    [Theory]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, false, true, false)]
    public void HoldingHand_GetsTheFreeHandNoticeOnlyWhenItsSqueezeDidNothing(
        bool gesture,
        bool itemHasUse,
        bool otherHandFree,
        bool notice
    )
    {
        Assert.Equal(notice, PingRules.UseFreeHand(gesture, itemHasUse, otherHandFree));
    }
}
