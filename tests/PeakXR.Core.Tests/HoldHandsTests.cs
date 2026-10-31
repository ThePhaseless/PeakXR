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

    /// <summary>The largest one-frame step and the whole path of an item eased back onto its grip over
    /// <see cref="HoldHands.EaseSeconds"/>, at <paramref name="frame"/> seconds a frame.</summary>
    static (float Step, float Travel) EasedRelease(float distance, float frame)
    {
        float step = 0f;
        float last = 0f;
        for (float t = frame; t < HoldHands.EaseSeconds + frame; t += frame)
        {
            float now = distance * HoldHands.Ease(t, HoldHands.EaseSeconds);
            step = MathF.Max(step, now - last);
            last = now;
        }
        return (step, last);
    }

    [Theory]
    // vr.14b's rig: 0.15 m back onto the grip at 30-40 ms frames reads 0.027-0.036 m a frame, yet it is an ease.
    [InlineData(0.15f, 0.011f)]
    [InlineData(0.15f, 0.03f)]
    [InlineData(0.15f, 0.04f)]
    [InlineData(0.3f, 0.06f)]
    public void ReleaseJumped_AnEaseAtAnyFrameTimeIsNoJump(float distance, float frame)
    {
        var (step, travel) = EasedRelease(distance, frame);
        Assert.False(HoldHands.ReleaseJumped(step, travel, 0.03f));
    }

    [Fact]
    public void ReleaseJumped_ASnapIsAJump()
    {
        // 5f489a9: the weld's shift snapped 0.095 m in one tick while the rest of the hold eased 0.05 m.
        var (_, travel) = EasedRelease(0.05f, 0.03f);
        Assert.True(HoldHands.ReleaseJumped(0.095f, travel + 0.095f, 0.03f));
        Assert.True(HoldHands.ReleaseJumped(0.1f, 0.1f, 0.03f));
        // A small move is never a jump, however sudden.
        Assert.False(HoldHands.ReleaseJumped(0.02f, 0.02f, 0.03f));
    }
}
