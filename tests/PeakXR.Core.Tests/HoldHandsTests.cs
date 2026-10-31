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

    /// <summary>Whether any frame of an item eased <paramref name="distance"/> back onto its grip over
    /// <see cref="HoldHands.EaseSeconds"/>, at <paramref name="frame"/> seconds a frame, reads as a jump. Frame
    /// <paramref name="hitchAt"/> lasts <paramref name="hitch"/> seconds instead, and <paramref name="snap"/> metres more
    /// land at once in frame <paramref name="snapAt"/>.</summary>
    static bool AnyJump(
        float distance,
        float frame,
        int hitchAt = -1,
        float hitch = 0f,
        int snapAt = -1,
        float snap = 0f
    )
    {
        float net = distance + snap;
        float t = 0f;
        float last = 0f;
        bool jumped = false;
        for (int i = 0; t < HoldHands.EaseSeconds + 0.1f; i++)
        {
            float dt = i == hitchAt ? hitch : frame;
            t += dt;
            float now =
                distance * HoldHands.Ease(t, HoldHands.EaseSeconds)
                + (i >= snapAt && snapAt >= 0 ? snap : 0f);
            jumped |= HoldHands.ReleaseJumped(now - last, dt, net);
            last = now;
        }
        return jumped;
    }

    [Theory]
    // vr.14b's rig: 0.15 m back onto the grip at 30-40 ms frames reads 0.027-0.036 m a frame, yet it is an ease.
    [InlineData(0.15f, 0.011f)]
    [InlineData(0.15f, 0.03f)]
    [InlineData(0.15f, 0.04f)]
    [InlineData(0.15f, 0.06f)]
    [InlineData(0.3f, 0.06f)]
    public void ReleaseJumped_AnEaseAtAnyFrameTimeIsNoJump(float distance, float frame) =>
        Assert.False(AnyJump(distance, frame));

    [Theory]
    [InlineData(3)]
    [InlineData(10)]
    [InlineData(18)]
    public void ReleaseJumped_OneLongFrameIsNoJump(int hitchAt) =>
        Assert.False(AnyJump(0.15f, 0.011f, hitchAt, 0.1f));

    [Theory]
    [InlineData(0.011f, 0)]
    [InlineData(0.011f, 10)]
    [InlineData(0.04f, 0)]
    [InlineData(0.04f, 3)]
    public void ReleaseJumped_ASnapInsideTheEaseIsAJump(float frame, int snapAt) =>
        // 0.06 m of a 0.15 m release lands in one frame, the rest eases.
        Assert.True(AnyJump(0.09f, frame, snapAt: snapAt, snap: 0.06f));

    [Fact]
    public void ReleaseJumped_TheShiftSnapIsAJump()
    {
        // 5f489a9: the weld's shift snapped 0.095 m in one tick while the rest of the hold eased 0.05 m.
        Assert.True(AnyJump(0.05f, 0.03f, snapAt: 2, snap: 0.095f));
        // Out and back by the same snap: no net travel, two jumps.
        Assert.True(HoldHands.ReleaseJumped(0.095f, 0.03f, 0f));
        // A small move is never a jump, however sudden.
        Assert.False(HoldHands.ReleaseJumped(0.01f, 0.011f, 0f));
    }
}
