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

    [Theory]
    // Short ray: one dot per spacing, the first half a spacing out.
    [InlineData(1f, 0.1f, 40, 10, 0.1f)]
    // Long ray: capped, spread evenly over the whole length.
    [InlineData(30f, 0.1f, 40, 40, 0.75f)]
    [InlineData(0f, 0.1f, 40, 0, 0.1f)]
    [InlineData(0.04f, 0.1f, 40, 0, 0.1f)]
    public void Dots_SpaceTheRayEvenly(
        float length,
        float spacing,
        int max,
        int expectedCount,
        float expectedStep
    )
    {
        PingRules.Dots(length, spacing, max, out int count, out float step);
        Assert.Equal(expectedCount, count);
        Assert.Equal(expectedStep, step, 4);
        // The last dot never passes the end of the ray.
        Assert.True(count == 0 || step * (count - 0.5f) <= length + 1e-4f);
    }

    [Fact]
    public void ButtonPreview_ShowsOnTouchAndShortPress_HidesForTheLongPressUntilLetGo()
    {
        var p = new PingButtonPreview();
        const float longPress = 0.5f;
        Assert.False(p.Update(false, false, 0f, longPress));
        Assert.True(p.Update(true, false, 0f, longPress));
        Assert.True(p.Update(true, true, 0.2f, longPress));
        // The emote long press took over: hidden, and stays hidden while the thumb rests on the button.
        Assert.False(p.Update(true, true, 0.5f, longPress));
        Assert.False(p.Update(true, false, 0f, longPress));
        // Thumb off, then back: shows again.
        Assert.False(p.Update(false, false, 0f, longPress));
        Assert.True(p.Update(true, false, 0f, longPress));
        // A press without a touch sensor still previews.
        p.Reset();
        Assert.True(p.Update(false, true, 0.1f, longPress));
    }
}
