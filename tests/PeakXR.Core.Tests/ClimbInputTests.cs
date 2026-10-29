namespace PeakXR.Core.Tests;

public class ClimbInputTests
{
    // Wall in front of a player facing +Z: the surface normal points back at the player.
    static readonly Vector3 Normal = new(0, 0, -1);

    [Fact]
    public void OnWall_UpIsY_RightIsX()
    {
        TestUtil.Near(1f, ClimbInput.OnWall(new Vector3(0, 1, 0), Normal).Y);
        TestUtil.Near(0f, ClimbInput.OnWall(new Vector3(0, 1, 0), Normal).X);
        TestUtil.Near(1f, ClimbInput.OnWall(new Vector3(1, 0, 0), Normal).X);
        TestUtil.Near(0f, ClimbInput.OnWall(new Vector3(1, 0, 0), Normal).Y);
    }

    [Fact]
    public void OnWall_MotionIntoTheWall_IsIgnored() =>
        Assert.Equal(Vector2.Zero, ClimbInput.OnWall(new Vector3(0, 0, 3), Normal));

    [Fact]
    public void OnWall_DegenerateNormal_IsZero()
    {
        Assert.Equal(Vector2.Zero, ClimbInput.OnWall(new Vector3(0, 1, 0), Vector3.Zero));
        Assert.Equal(Vector2.Zero, ClimbInput.OnWall(new Vector3(1, 0, 0), Vector3.UnitY));
    }

    [Fact]
    public void WallSpeed_IsTheSampledShareOfClimbSpeed()
    {
        // SampleWall casts from 0.4 m off the wall to 1 m behind the requested point: 0.4/1.4 of each step lands.
        TestUtil.Near(4f * 1.5f * 0.4f / 1.4f, ClimbInput.WallSpeed(4f, 1.5f));
    }

    [Fact]
    public void DownSpeed_IsThreeTimesTheBareWallSpeed() =>
        // CharacterClimbing.cs:305: normalized * -3, with neither staminaMod nor the surface multiplier.
        TestUtil.Near(3f * 4f * 1.5f * 0.4f / 1.4f, ClimbInput.DownSpeed(4f, 1.5f));

    [Fact]
    public void Smooth_ConvergesTowardTarget()
    {
        var v = ClimbInput.Smooth(Vector2.Zero, new Vector2(1, 0), rate: 10f, dt: 0.1f);
        Assert.InRange(v.X, 0.6f, 0.7f);
        Assert.Equal(
            new Vector2(1, 0),
            ClimbInput.Smooth(Vector2.Zero, new Vector2(1, 0), 0f, 0.1f)
        );
    }

    [Theory]
    // Two hands: any lost controller freezes the hang, anchored or not.
    [InlineData(false, false, false, false, true)]
    [InlineData(true, false, false, false, false)]
    // One-handed: the unused controller is always untracked and must not freeze it.
    [InlineData(false, false, true, false, false)]
    // One-handed: the one controller lost does.
    [InlineData(false, false, true, true, true)]
    // A hand still anchored when it blinks freezes it whatever the mode.
    [InlineData(false, true, true, false, true)]
    public void HangFreezes_OnlyForAHandInUse(
        bool tracked,
        bool anchored,
        bool oneHanded,
        bool theOneHand,
        bool freezes
    ) => Assert.Equal(freezes, ClimbInput.FreezesHang(tracked, anchored, oneHanded, theOneHand));
}

public class BlinkHoldTests
{
    [Fact]
    public void Occlusion_KeepsTheGrip_LongLossOpensItOnce()
    {
        var b = new BlinkHold();
        // A Lighthouse controller behind the body, a WMR one behind the back: seconds untracked, still held.
        Assert.False(b.Update(false, false, false, 5f));
        Assert.False(b.Expired);
        Assert.False(b.Update(true, false, false, 0.01f));
        int fired = 0;
        for (int i = 0; i < 200; i++)
            if (b.Update(false, false, false, 0.05f))
                fired++;
        Assert.Equal(1, fired);
        Assert.True(b.Expired);
        b.Update(true, false, false, 0.01f);
        Assert.False(b.Expired);
    }

    [Fact]
    public void FourSecondsBehindTheBack_StillHeld()
    {
        var b = new BlinkHold();
        for (int i = 0; i < 40; i++)
            Assert.False(b.Update(false, false, false, 0.1f));
        Assert.False(b.Expired);
    }

    [Fact]
    public void ARadioHiccup_KeepsTheGrip()
    {
        var b = new BlinkHold();
        // The runtime drops the device for a second (a radio hiccup, a profile reload) and brings it back.
        for (int i = 0; i < 10; i++)
            Assert.False(b.Update(false, true, false, 0.1f));
        Assert.False(b.Expired);
        Assert.False(b.Update(true, false, false, 0.01f));
        Assert.False(b.Expired);
    }

    [Fact]
    public void ADisconnectedController_OpensAfterTheGrace_UntilTrackedAgain()
    {
        var b = new BlinkHold();
        int fired = 0;
        for (float t = 0f; t < BlinkHold.DisconnectSeconds + 0.2f; t += 0.05f)
            if (b.Update(false, true, false, 0.05f))
                fired++;
        Assert.Equal(1, fired);
        Assert.True(b.Expired);
        // Back as a device but not tracked yet: still open, never fired twice.
        Assert.False(b.Update(false, false, false, 0.01f));
        Assert.True(b.Expired);
        b.Update(true, false, false, 0.01f);
        Assert.False(b.Expired);
    }

    [Fact]
    public void ADisconnectThatFlaps_NeedsTheGraceInOneGo()
    {
        var b = new BlinkHold();
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 15; j++)
                b.Update(false, true, false, 0.1f);
            b.Update(false, false, false, 0.1f);
        }
        // 4.5 s of drops in 1.5 s bursts: no single drop is long enough, and 4.8 s untracked is under MaxSeconds.
        Assert.False(b.Expired);
    }

    [Fact]
    public void WhileTheRuntimeHasFocus_NothingOpens_ThenTheClockResumes()
    {
        var b = new BlinkHold();
        // The SteamVR dashboard is open: controllers may read untracked or vanish, and multiplayer does not pause.
        for (int i = 0; i < 300; i++)
            Assert.False(b.Update(false, i % 2 == 0, true, 0.1f));
        Assert.False(b.Expired);
        // Focus back, the controller really gone: it opens after the grace, counted from the dashboard closing.
        Assert.False(b.Update(false, true, false, BlinkHold.DisconnectSeconds * 0.5f));
        Assert.True(b.Update(false, true, false, BlinkHold.DisconnectSeconds * 0.6f));
    }
}
