namespace PeakXR.Core.Tests;

public class EmoteGesturesTests
{
    const float Dt = 1f / 90f;

    // Lateral hand position over time: a sine of the given amplitude (m) and frequency (Hz).
    static bool Wave(WaveGesture g, float amplitude, float hz, float seconds, bool raised = true)
    {
        for (float t = 0f; t < seconds; t += Dt)
            if (g.Update(raised, amplitude * MathF.Sin(2f * MathF.PI * hz * t), Dt))
                return true;
        return false;
    }

    [Fact]
    public void Wave_RaisedOpenHandSwingingSideToSide_Fires()
    {
        Assert.True(Wave(new WaveGesture(), 0.1f, 1.5f, 2f));
    }

    [Fact]
    public void Wave_HandDown_NeverFires()
    {
        Assert.False(Wave(new WaveGesture(), 0.1f, 1.5f, 3f, raised: false));
    }

    [Fact]
    public void Wave_SmallTremor_NeverFires()
    {
        Assert.False(Wave(new WaveGesture(), 0.02f, 3f, 3f));
    }

    [Fact]
    public void Wave_SlowDrift_NeverFires()
    {
        // One swing every 2 s: three swings never fit in the window.
        Assert.False(Wave(new WaveGesture(), 0.15f, 0.25f, 6f));
    }

    [Fact]
    public void Wave_OneSwingOnly_DoesNotFire()
    {
        var g = new WaveGesture();
        for (float t = 0f; t < 1f; t += Dt)
            Assert.False(g.Update(true, t * 0.3f, Dt));
    }

    [Fact]
    public void Wave_LoweringTheHand_StartsOver()
    {
        var g = new WaveGesture();
        // Two swings, then the hand drops: the count is lost.
        Assert.False(Wave(g, 0.1f, 1.5f, 0.55f));
        g.Update(false, 0f, Dt);
        Assert.False(Wave(g, 0.1f, 1.5f, 0.55f));
    }

    [Fact]
    public void Dwell_BothHandsUpForTheDwell_FiresOnce()
    {
        var g = new DwellGesture();
        int fired = 0;
        for (float t = 0f; t < 2f; t += Dt)
            if (g.Update(true, Dt))
                fired++;
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Dwell_ShorterThanDwell_DoesNotFire()
    {
        var g = new DwellGesture();
        for (float t = 0f; t < g.Dwell * 0.8f; t += Dt)
            Assert.False(g.Update(true, Dt));
        Assert.False(g.Update(false, Dt));
    }

    [Fact]
    public void Dwell_RearmsAfterTheHandsDrop()
    {
        var g = new DwellGesture();
        bool first = false;
        for (float t = 0f; t < 1f; t += Dt)
            first |= g.Update(true, Dt);
        g.Update(false, Dt);
        bool second = false;
        for (float t = 0f; t < 1f; t += Dt)
            second |= g.Update(true, Dt);
        Assert.True(first && second);
    }

    [Fact]
    public void Raised_AboveTheLineUnderTheHead()
    {
        Assert.True(EmoteGestures.Raised(handY: 1.6f, headY: 1.7f, margin: -0.15f));
        Assert.False(EmoteGestures.Raised(handY: 1.4f, headY: 1.7f, margin: -0.15f));
    }

    [Theory]
    [InlineData(true, false, false, false, false, DenyReason.NotNow)]
    [InlineData(false, true, false, false, false, DenyReason.NotNow)]
    [InlineData(false, false, true, false, false, DenyReason.NotWhileClimbing)]
    [InlineData(false, false, false, true, false, DenyReason.NotNow)]
    [InlineData(false, false, false, false, true, DenyReason.NotNow)]
    [InlineData(false, false, false, false, false, DenyReason.None)]
    public void WheelRefusal_FollowsTheGameGate(
        bool dead,
        bool won,
        bool climbing,
        bool otherWheel,
        bool window,
        DenyReason expected
    )
    {
        Assert.Equal(expected, EmoteGestures.WheelRefusal(dead, won, climbing, otherWheel, window));
    }

    // Fist on the controller (grip closed, finger resting on the trigger), thumb lifted off every button.
    static readonly FingerCurls Fist = new(0f, FingerCurlMapper.TriggerTouchCurl, 1f, 1f, 1f);

    [Fact]
    public void ThumbsUp_FistWithTheThumbLiftedAndUp()
    {
        Assert.True(EmoteGestures.ThumbsUp(Fist, thumbUp: 0.9f));
    }

    [Fact]
    public void ThumbLifted_AsTheFistStartsClosing()
    {
        // Issue #82: at the grip's press the other fingers are only just closing; the thumb already says thumbs-up.
        var closing = new FingerCurls(0f, 0f, 0.6f, 0.6f, 0.5f);
        Assert.False(EmoteGestures.ThumbsUp(closing, 0.9f));
        Assert.True(EmoteGestures.ThumbLifted(closing, 0.9f));
        closing.Thumb = FingerCurlMapper.ThumbTouchCurl;
        Assert.False(EmoteGestures.ThumbLifted(closing, 0.9f));
        Assert.False(EmoteGestures.ThumbLifted(Fist, 0.2f));
    }

    [Fact]
    public void ThumbsUp_ThumbOnAButton_IsNot()
    {
        var c = Fist;
        c.Thumb = FingerCurlMapper.ThumbTouchCurl;
        Assert.False(EmoteGestures.ThumbsUp(c, thumbUp: 0.9f));
    }

    [Fact]
    public void ThumbsUp_OpenHand_IsNot()
    {
        Assert.False(EmoteGestures.ThumbsUp(new FingerCurls(0f, 0f, 0.1f, 0.1f, 0.1f), 0.9f));
    }

    [Fact]
    public void ThumbsUp_PointingIndex_IsNot()
    {
        var c = Fist;
        c.Index = 0f;
        Assert.False(EmoteGestures.ThumbsUp(c, thumbUp: 0.9f));
    }

    [Fact]
    public void ThumbsUp_ThumbSidewaysOrDown_IsNot()
    {
        Assert.False(EmoteGestures.ThumbsUp(Fist, thumbUp: 0.3f));
        Assert.False(EmoteGestures.ThumbsUp(Fist, thumbUp: -0.9f));
    }

    // A deliberate thumbs-up: held out 0.4 m in front, 0.2 m under the eyes, still, thumb sensed and lifted.
    static bool Thumb(
        bool pose = true,
        bool sensed = true,
        bool holding = false,
        bool busy = false,
        float forward = 0.4f,
        float below = 0.2f,
        float speed = 0.05f,
        float stretch = 0.6f
    ) => EmoteGestures.ThumbIntent(pose, sensed, holding, busy, forward, below, speed, stretch);

    // Frames of one intent fed to the dwell; how many emotes fire.
    static int Fires(DwellGesture g, bool intent, float seconds)
    {
        int fired = 0;
        for (float t = 0f; t < seconds; t += Dt)
            if (g.Update(intent, Dt))
                fired++;
        return fired;
    }

    [Fact]
    public void ThumbIntent_DeliberateHeldPose_FiresOnce()
    {
        var g = new DwellGesture { Dwell = EmoteGestures.ThumbDwell };
        Assert.Equal(1, Fires(g, Thumb(), 2f));
    }

    [Fact]
    public void ThumbIntent_OrdinaryGrips_NeverFire()
    {
        // vr audit: a closed grip with the thumb off the buttons fired the thumbs-up for every peer to see.
        Assert.False(Thumb(forward: 0.05f, below: 0.6f)); // fist at the side, swinging while walking
        Assert.False(Thumb(below: 0.7f)); // held low, at the hip
        Assert.False(Thumb(holding: true)); // gripping a held item
        Assert.False(Thumb(busy: true)); // climbing, on a rope or vine, carrying, mid-grab
        Assert.False(Thumb(speed: 0.8f)); // reaching or swinging
        Assert.False(Thumb(sensed: false)); // no thumb touch sensor: a lifted thumb cannot be told apart
        Assert.False(Thumb(pose: false));
        var g = new DwellGesture { Dwell = EmoteGestures.ThumbDwell };
        Assert.Equal(0, Fires(g, Thumb(forward: 0.05f, below: 0.6f), 5f));
    }

    [Fact]
    public void ThumbIntent_APlainReachIsNoThumbsUp()
    {
        // #146: a neutral forward reach has the thumb side up, so a still fist at full stretch met every other rule.
        Assert.False(Thumb(stretch: 0.95f));
        Assert.False(Thumb(stretch: 0.91f));
        Assert.True(Thumb(stretch: 0.9f));
    }

    [Theory]
    [InlineData(0.3f, true)]
    [InlineData(0.4f, true)]
    [InlineData(0.45f, true)]
    [InlineData(0.6f, false)]
    public void ThumbIntent_NaturalHoldsCountOnTheNominalArm(float ahead, bool counts)
    {
        // A fist from the eye, a little to its own side and 0.2 m down, for a player who has not stretched yet.
        var head = new System.Numerics.Vector3(0f, 1.6f, 0f);
        var neck = BodySlotLayout.Neck(head, System.Numerics.Quaternion.Identity);
        var shoulder = ReachOut.Shoulder(neck, 0f, false);
        var hand = head + new System.Numerics.Vector3(0.1f, -0.2f, ahead);
        float stretch = ReachOut.Stretch(shoulder, hand, ReachOut.NominalArm);
        Assert.Equal(counts, EmoteGestures.ThumbOutFront(ahead, 0.2f, stretch));
    }

    [Theory]
    [InlineData(0.6f, EmoteGestures.ThumbDwell)]
    [InlineData(0.75f, EmoteGestures.ThumbDwell)]
    [InlineData(0.76f, EmoteGestures.ThumbFarDwell)]
    [InlineData(0.9f, EmoteGestures.ThumbFarDwell)]
    public void ThumbDwell_IsLongerPastABentArm(float stretch, float dwell) =>
        Assert.Equal(dwell, EmoteGestures.ThumbDwellAt(stretch));

    [Fact]
    public void ThumbDwell_AStalledMidStretchFistWaitsLonger()
    {
        var g = new DwellGesture { Dwell = EmoteGestures.ThumbDwellAt(0.85f) };
        Assert.Equal(0, Fires(g, Thumb(stretch: 0.85f), 0.8f));
        Assert.Equal(1, Fires(g, Thumb(stretch: 0.85f), 0.4f));
    }

    [Fact]
    public void ThumbDwell_FiresBeforeTheFistsInteractComesDue()
    {
        // #82: a posed fist holds the head-aimed interact back at most GestureMaxWait from the grip.
        Assert.True(EmoteGestures.ThumbFarDwell < GripIntent.GestureMaxWait);
    }

    [Fact]
    public void ThumbIntent_AThumbThatNeverMovedIsNoThumbsUp()
    {
        // #146: reaching out and pausing with a closed fist, the thumb long off the stick. The fist closed in place,
        // open when it got there, and this far out it is a thumbs-up's own stretch: only the thumb tells them apart.
        var g = new DwellGesture { Dwell = EmoteGestures.ThumbDwell };
        bool lifted = EmoteGestures.ThumbJustLifted(EmoteGestures.ThumbLiftWindow + 0.1f);
        Assert.Equal(0, Fires(g, Thumb(sensed: lifted, stretch: 0.5f), 5f));
    }

    [Fact]
    public void ThumbIntent_ALiftedThumbFitsARaiseAndTheDwell()
    {
        // Lifted, the fist raised for a second, then held for the longer of the two dwells.
        const float Raise = 1f;
        var g = new DwellGesture { Dwell = EmoteGestures.ThumbFarDwell };
        bool lifted = EmoteGestures.ThumbJustLifted(Raise + EmoteGestures.ThumbFarDwell);
        Assert.Equal(1, Fires(g, Thumb(sensed: lifted, stretch: 0.85f), 1.2f));
        Assert.True(EmoteGestures.ThumbJustLifted(0f));
    }

    [Fact]
    public void ThumbIntent_BrokenByMovement_StartsOver()
    {
        var g = new DwellGesture { Dwell = EmoteGestures.ThumbDwell };
        Assert.Equal(0, Fires(g, Thumb(), EmoteGestures.ThumbDwell * 0.8f));
        Assert.Equal(0, Fires(g, Thumb(speed: 0.8f), 0.05f));
        Assert.Equal(0, Fires(g, Thumb(), EmoteGestures.ThumbDwell * 0.8f));
    }

    [Fact]
    public void Busy_AnyBodyActivityBlocksGestures()
    {
        Assert.False(EmoteGestures.Busy(false, false, false));
        Assert.True(EmoteGestures.Busy(climbingAnything: true, false, false));
        Assert.True(EmoteGestures.Busy(false, carrying: true, false));
        Assert.True(EmoteGestures.Busy(false, false, midGrab: true));
        // Issue #84: shaking the hands free of a web or fly trap is no wave.
        Assert.True(EmoteGestures.Busy(false, false, false, struggling: true));
    }

    [Fact]
    public void HandBusy_AnyCueOnTheHandBlocksIt()
    {
        // Issue #84: reaching up for a rope, vine, handle or ledge with open hands is no cheer or wave.
        Assert.False(EmoteGestures.HandBusy(false, false, false, false));
        Assert.True(EmoteGestures.HandBusy(midGrab: true, false, false, false));
        Assert.True(EmoteGestures.HandBusy(false, atRock: true, false, false));
        Assert.True(EmoteGestures.HandBusy(false, false, climbRing: true, false));
        Assert.True(EmoteGestures.HandBusy(false, false, false, grabGlow: true));
    }
}
