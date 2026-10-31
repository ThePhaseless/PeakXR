namespace PeakXR.Core.Tests;

public class ReachOutTests
{
    static readonly Vector3 Neck = new(0f, 1.5f, 0f);

    [Fact]
    public void Shoulder_SitsToTheSideOfTheNeck()
    {
        var r = ReachOut.Shoulder(Neck, 0f, left: false);
        var l = ReachOut.Shoulder(Neck, 0f, left: true);
        TestUtil.Near(0.18f, r.X);
        TestUtil.Near(-0.18f, l.X);
        TestUtil.Near(1.35f, r.Y);
        // Facing +X (yaw 90): the right shoulder is toward -Z.
        var turned = ReachOut.Shoulder(Neck, 90f, left: false);
        TestUtil.Near(0f, turned.X, 1e-4f);
        TestUtil.Near(-0.18f, turned.Z, 1e-4f);
    }

    [Fact]
    public void Shoulder_FromTheNeck_StaysPutWhenTheHeadPitches()
    {
        // A real head pitches about the neck, so the tracked head moves with it.
        var pitched = UnityQuat.Euler(60f, 0f, 0f);
        var level = BodySlotLayout.Neck(Neck - BodySlotLayout.NeckFromHead, Quaternion.Identity);
        var down = BodySlotLayout.Neck(
            Neck - UnityQuat.Rotate(pitched, BodySlotLayout.NeckFromHead),
            pitched
        );
        float gap = Vector3.Distance(
            ReachOut.Shoulder(level, 0f, false),
            ReachOut.Shoulder(down, 0f, false)
        );
        Assert.True(gap < 0.02f, $"gap {gap}");
    }

    [Theory]
    [InlineData(0f, 0.65f)]
    [InlineData(float.NaN, 0.65f)]
    [InlineData(float.PositiveInfinity, 0.65f)]
    [InlineData(0.4f, 0.5f)]
    [InlineData(1.0f, 0.8f)]
    [InlineData(0.7f, 0.7f)]
    public void Arm_IsNominalUntilLearned_ThenClamped(float learned, float expect) =>
        TestUtil.Near(expect, ReachOut.Arm(learned));

    static readonly Vector3 Sh = new(0.18f, 1.35f, 0f);

    [Fact]
    public void Aims_AStraightArmAtTheFriend()
    {
        var friend = Sh + new Vector3(0f, 0f, 3f);
        var hand = Sh + new Vector3(0f, 0f, 0.65f);
        Assert.True(ReachOut.Aims(Sh, hand, friend, 0.65f, 0.75f, 15f));
    }

    [Fact]
    public void Aims_NotAHalfBentArm()
    {
        var friend = Sh + new Vector3(0f, 0f, 3f);
        var hand = Sh + new Vector3(0f, 0f, 0.33f);
        Assert.False(ReachOut.Aims(Sh, hand, friend, 0.65f, 0.75f, 15f));
    }

    [Fact]
    public void Aims_NotAFullStretch40DegreesOff()
    {
        var friend = Sh + new Vector3(0f, 0f, 3f);
        float a = 40f * MathF.PI / 180f;
        var hand = Sh + new Vector3(MathF.Sin(a), 0f, MathF.Cos(a)) * 0.65f;
        Assert.False(ReachOut.Aims(Sh, hand, friend, 0.65f, 0.75f, 15f));
    }

    [Fact]
    public void Aims_NotAHangingArmWithTheFriendLevelAhead()
    {
        var friend = Sh + new Vector3(0f, 0f, 3f);
        var hand = Sh + new Vector3(0f, -0.65f, 0f);
        Assert.False(ReachOut.Aims(Sh, hand, friend, 0.65f, 0.75f, 15f));
    }

    [Fact]
    public void Aims_NotAZeroLengthLeg()
    {
        Assert.False(ReachOut.Aims(Sh, Sh, Sh + Vector3.UnitZ, 0.65f, 0f, 15f));
        Assert.False(ReachOut.Aims(Sh, Sh + Vector3.UnitZ, Sh, 0.65f, 0f, 15f));
        TestUtil.Near(180f, ReachOut.AimDegrees(Sh, Sh, Sh + Vector3.UnitZ));
    }

    [Fact]
    public void Aims_TheIssueCase_LookingDownAtAFriendBelowTheLedge()
    {
        var head = new Vector3(0f, 1.7f, 0f);
        var rot = UnityQuat.Euler(60f, 0f, 0f);
        var shoulder = ReachOut.Shoulder(BodySlotLayout.Neck(head, rot), 0f, false);
        var friend = head + new Vector3(0f, -2.5f, 0.8f);
        float arm = ReachOut.Arm(0.75f);
        var hand = shoulder + Vector3.Normalize(friend - shoulder) * (0.75f * arm);
        Assert.True(ReachOut.Aims(shoulder, hand, friend, arm, 0.75f, ReachOut.AcquireDegrees));
    }

    [Fact]
    public void Learns_OnlyAlongTheBodyYaw()
    {
        float a = 30f * MathF.PI / 180f;
        Assert.True(ReachOut.Learns(Sh, Sh + new Vector3(MathF.Sin(a), 0f, MathF.Cos(a)), 0f));
        a = 60f * MathF.PI / 180f;
        Assert.False(ReachOut.Learns(Sh, Sh + new Vector3(MathF.Sin(a), 0f, MathF.Cos(a)), 0f));
        Assert.False(ReachOut.Learns(Sh, Sh + new Vector3(0f, -0.6f, 0f), 0f));
    }

    [Fact]
    public void Verdict_FollowsTheGamesReach()
    {
        var look = Vector3.UnitZ;
        Assert.Equal(DenyReason.HandsFull, ReachOut.Verdict(true, look, new Vector3(0f, 0f, -9f)));
        Assert.Equal(
            DenyReason.OutOfReach,
            ReachOut.Verdict(false, look, new Vector3(0f, 0f, 4.5f))
        );
        float a = 70f * MathF.PI / 180f;
        Assert.Equal(
            DenyReason.WrongAngle,
            ReachOut.Verdict(false, look, new Vector3(MathF.Sin(a), 0f, MathF.Cos(a)) * 2f)
        );
        Assert.Equal(DenyReason.None, ReachOut.Verdict(false, look, new Vector3(0f, 0f, 2f)));
    }

    [Fact]
    public void Verdict_AFriendBelowNeedsALookDown()
    {
        var below = new Vector3(0f, -2.5f, 1f);
        Assert.Equal(DenyReason.WrongAngle, ReachOut.Verdict(false, Vector3.UnitZ, below));
        var down = Vector3.Normalize(new Vector3(0f, -1f, 1f));
        Assert.Equal(DenyReason.None, ReachOut.Verdict(false, down, below));
    }

    // ------------------------------------------------------------------ tracker

    const float Enter = 0.75f;

    /// <summary>One frame: <paramref name="offers"/> as (friend, angle, in range).</summary>
    static void Frame(
        ReachOutTracker t,
        float now,
        float stretch,
        bool open,
        params (int friend, float angle, bool inRange)[] offers
    )
    {
        t.Begin();
        foreach (var o in offers)
            t.Offer(o.friend, o.angle, o.inRange);
        t.Step(stretch, Enter, open, now);
    }

    [Fact]
    public void Tracker_AcquiresInside15_HoldsTo25()
    {
        var t = new ReachOutTracker();
        Frame(t, 0f, 0.8f, true, (7, 10f, true));
        Assert.Equal(7, t.Friend);
        Frame(t, 0.1f, 0.8f, true, (7, 20f, true));
        Assert.Equal(7, t.Friend);
        Frame(t, 0.2f, 0.8f, true, (7, 26f, true));
        Assert.Equal(-1, t.Friend);
    }

    [Fact]
    public void Tracker_DoesNotAcquireAt20()
    {
        var t = new ReachOutTracker();
        Frame(t, 0f, 0.8f, true, (7, 20f, true));
        Assert.Equal(-1, t.Friend);
    }

    [Fact]
    public void Tracker_StretchHysteresis()
    {
        var t = new ReachOutTracker();
        Frame(t, 0f, Enter - 0.01f, true, (7, 5f, true));
        Assert.Equal(-1, t.Friend);
        Frame(t, 0.1f, Enter, true, (7, 5f, true));
        Assert.Equal(7, t.Friend);
        Frame(t, 0.2f, Enter - 0.05f, true, (7, 5f, true));
        Assert.Equal(7, t.Friend);
        Frame(t, 0.3f, Enter - 0.11f, true, (7, 5f, true));
        Assert.Equal(-1, t.Friend);
    }

    [Fact]
    public void Tracker_AClosedHandCannotAcquire_ButASlowSqueezeHolds()
    {
        var t = new ReachOutTracker();
        Frame(t, 0f, 0.8f, false, (7, 5f, true));
        Assert.Equal(-1, t.Friend);
        Frame(t, 0.1f, 0.8f, true, (7, 5f, true));
        Assert.Equal(7, t.Friend);
        for (float now = 0.2f; now <= 2.1f; now += 0.1f)
            Frame(t, now, 0.8f, false, (7, 5f, true));
        Assert.Equal(7, t.Friend);
        Assert.Equal(7, t.ArmedFriend);
    }

    [Fact]
    public void Tracker_ArmsAfterTheDwell()
    {
        var t = new ReachOutTracker();
        Frame(t, 0f, 0.8f, true, (7, 5f, true));
        Assert.Equal(-1, t.ArmedFriend);
        Frame(t, 0.14f, 0.8f, true, (7, 5f, true));
        Assert.Equal(-1, t.ArmedFriend);
        Frame(t, 0.15f, 0.8f, true, (7, 5f, true));
        Assert.Equal(7, t.ArmedFriend);
    }

    [Fact]
    public void Tracker_StaysArmedThroughTheGrace()
    {
        var t = new ReachOutTracker();
        Frame(t, 0f, 0.8f, true, (7, 5f, true));
        Frame(t, 0.2f, 0.8f, true, (7, 5f, true));
        Frame(t, 1f, 0.8f, true);
        Assert.Equal(-1, t.Friend);
        Assert.Equal(7, t.ArmedFriend);
        Frame(t, 1.25f, 0.8f, true);
        Assert.Equal(7, t.ArmedFriend);
        Frame(t, 1.26f, 0.8f, true);
        Assert.Equal(-1, t.ArmedFriend);
    }

    [Fact]
    public void Tracker_ADifferentFriendRestartsTheDwell()
    {
        var t = new ReachOutTracker();
        Frame(t, 0f, 0.8f, true, (7, 5f, true));
        Frame(t, 0.2f, 0.8f, true, (7, 5f, true));
        Frame(t, 0.3f, 0.8f, true, (8, 5f, true));
        Assert.Equal(8, t.Friend);
        Frame(t, 0.4f, 0.8f, true, (8, 5f, true));
        Assert.NotEqual(8, t.ArmedFriend);
        Frame(t, 0.46f, 0.8f, true, (8, 5f, true));
        Assert.Equal(8, t.ArmedFriend);
    }

    [Fact]
    public void Tracker_SameFriendWithinGraceKeepsTheDwell()
    {
        var t = new ReachOutTracker();
        Frame(t, 0f, 0.8f, true, (7, 5f, true));
        Frame(t, 0.2f, 0.8f, true, (7, 5f, true));
        Frame(t, 0.3f, 0.8f, true);
        Frame(t, 0.4f, 0.8f, true, (7, 5f, true));
        Assert.Equal(7, t.Friend);
        Assert.Equal(7, t.ArmedFriend);
    }

    [Fact]
    public void Tracker_BackToTheFirstFriendWithinGraceKeepsItsDwell()
    {
        var t = new ReachOutTracker();
        Frame(t, 0f, 0.8f, true, (7, 5f, true));
        Frame(t, 0.2f, 0.8f, true, (7, 5f, true));
        Frame(t, 0.3f, 0.8f, true, (7, 30f, true), (8, 5f, true));
        Assert.Equal(8, t.Friend);
        Frame(t, 0.35f, 0.8f, true, (7, 5f, true), (8, 30f, true));
        Assert.Equal(7, t.Friend);
        Assert.Equal(7, t.ArmedFriend);
    }

    [Fact]
    public void Tracker_InRangeFriendsWin()
    {
        var t = new ReachOutTracker();
        Frame(t, 0f, 0.8f, true, (1, 2f, false), (2, 14f, true));
        Assert.Equal(2, t.Friend);

        t = new ReachOutTracker();
        Frame(t, 0f, 0.8f, true, (1, 5f, false));
        Assert.Equal(1, t.Friend);
        Frame(t, 0.1f, 0.8f, true, (1, 5f, false), (2, 20f, true));
        Assert.Equal(1, t.Friend);
        Frame(t, 0.2f, 0.8f, true, (1, 5f, false), (2, 12f, true));
        Assert.Equal(2, t.Friend);

        t = new ReachOutTracker();
        Frame(t, 0f, 0.8f, true, (2, 10f, true));
        Frame(t, 0.1f, 0.8f, true, (2, 10f, true), (3, 1f, true), (1, 0f, false));
        Assert.Equal(2, t.Friend);
    }

    /// <summary>Holds friend <paramref name="f"/> from <paramref name="from"/> to <paramref name="to"/>, every 0.05 s;
    /// true when any frame reported Acquired.</summary>
    static bool Hold(ReachOutTracker t, int f, float from, float to)
    {
        bool any = false;
        for (float now = from; now <= to + 1e-4f; now += 0.05f)
        {
            Frame(t, now, 0.8f, true, (f, 5f, true));
            any |= t.Acquired;
        }
        return any;
    }

    [Fact]
    public void Tracker_AcquiredFiresOncePerFriend_OnArming()
    {
        var t = new ReachOutTracker();
        Frame(t, 0f, 0.8f, true, (7, 5f, true));
        // The tick waits for the ring: only once the pick is armed.
        Assert.False(t.Acquired);
        Assert.True(Hold(t, 7, 0.05f, 0.3f));
        Assert.False(Hold(t, 7, 0.35f, 0.5f));
        // Lost for 0.5 s (past Grace, so the dwell restarts), then re-picked: no second tick.
        Frame(t, 1f, 0.8f, true);
        Assert.False(Hold(t, 7, 1.5f, 1.8f));
        Assert.Equal(7, t.ArmedFriend);
        // Lost for 1.1 s: a new tick.
        Frame(t, 1.85f, 0.8f, true);
        Assert.True(Hold(t, 7, 2.95f, 3.3f));
        // A different friend ticks as soon as it arms.
        Assert.True(Hold(t, 8, 3.35f, 3.6f));
    }

    [Fact]
    public void Tracker_ResetForgetsEverything()
    {
        var t = new ReachOutTracker();
        Assert.True(Hold(t, 7, 0f, 0.3f));
        t.Reset();
        Assert.Equal(-1, t.Friend);
        Assert.Equal(-1, t.ArmedFriend);
        Assert.True(Hold(t, 7, 0.35f, 0.7f));
    }
}
