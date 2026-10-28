namespace PeakXR.Core.Tests;

public class ClimbInputTests
{
    // Wall in front of a player facing +Z: the surface normal points back at the player.
    static readonly Vector3 Normal = new(0, 0, -1);

    [Fact]
    public void MovingUp_IsPositiveY()
    {
        var input = ClimbInput.FromBodyVelocity(
            new Vector3(0, 1, 0),
            Normal,
            climbSpeed: 2f,
            deadzone: 0.05f
        );
        TestUtil.Near(0.5f, input.Y);
        TestUtil.Near(0f, input.X);
    }

    [Fact]
    public void MovingRight_IsPositiveX()
    {
        var input = ClimbInput.FromBodyVelocity(new Vector3(1, 0, 0), Normal, 2f, 0.05f);
        TestUtil.Near(0.5f, input.X);
        TestUtil.Near(0f, input.Y);
    }

    [Fact]
    public void WallSpeed_IsTheSampledShareOfClimbSpeed()
    {
        // SampleWall casts from 0.4 m off the wall to 1 m behind the requested point: 0.4/1.4 of each step lands.
        TestUtil.Near(4f * 1.5f * 0.4f / 1.4f, ClimbInput.WallSpeed(4f, 1.5f));
    }

    [Fact]
    public void ArmPull_MovesTheClimberAsFarAsTheHand()
    {
        // Vanilla flat wall at z = 0 (CharacterClimbing.cs:286, :412): each fixed step requests
        // climbPos + up * stick.y * climbSpeed * dt, then climbPos becomes where the line from
        // climbPos + normal * 0.4 to requested - normal * 1 crosses the wall.
        const float climbSpeed = 4f,
            dt = 0.02f;
        var pull = new Vector3(0, 0.5f, 0);
        float y = 0f;
        for (int i = 0; i < 40; i++)
        {
            var stick = ClimbInput.FromBodyVelocity(
                pull,
                Normal,
                ClimbInput.WallSpeed(climbSpeed, 1f),
                0.1f
            );
            var from = new Vector3(0, y, 0) + Normal * 0.4f;
            var to = new Vector3(0, y + stick.Y * climbSpeed * dt, 0) - Normal;
            float t = from.Z / (from.Z - to.Z);
            y = from.Y + (to.Y - from.Y) * t;
        }
        TestUtil.Near(0.4f, y);
    }

    [Fact]
    public void MotionIntoTheWall_IsIgnored()
    {
        var input = ClimbInput.FromBodyVelocity(new Vector3(0, 0, 3), Normal, 2f, 0.05f);
        Assert.Equal(Vector2.Zero, input);
    }

    [Fact]
    public void Fast_IsClampedToUnitLength()
    {
        var input = ClimbInput.FromBodyVelocity(new Vector3(10, 10, 0), Normal, 2f, 0.05f);
        TestUtil.Near(1f, input.Length());
    }

    [Fact]
    public void Slow_IsInsideDeadzone()
    {
        Assert.Equal(
            Vector2.Zero,
            ClimbInput.FromBodyVelocity(new Vector3(0, 0.05f, 0), Normal, 2f, 0.1f)
        );
    }

    [Fact]
    public void ZeroSpeed_GivesZero()
    {
        Assert.Equal(
            Vector2.Zero,
            ClimbInput.FromBodyVelocity(new Vector3(0, 1, 0), Normal, 0f, 0.1f)
        );
    }

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
}

public class ClimbInputSettleTests
{
    [Fact]
    public void SmoothedResidue_BelowDeadzone_IsZero()
    {
        var input = ClimbInput.Settle(new Vector2(0.03f, 0.04f), Vector2.Zero);
        Assert.Equal(Vector2.Zero, input);
    }

    [Fact]
    public void Downward_IsDropped_UnlessTheArmsClearlyPullDown()
    {
        // The game climbs down at full speed for any negative stick y (CharacterClimbing.cs:303).
        var residue = ClimbInput.Settle(new Vector2(0f, -0.2f), new Vector2(0f, -0.1f));
        Assert.Equal(0f, residue.Y);
        var pull = ClimbInput.Settle(new Vector2(0f, -0.2f), new Vector2(0f, -0.5f));
        TestUtil.Near(-0.2f, pull.Y);
    }

    [Fact]
    public void UpAndSideways_PassThrough()
    {
        var input = ClimbInput.Settle(new Vector2(-0.4f, 0.6f), new Vector2(-0.5f, 0.7f));
        TestUtil.Near(-0.4f, input.X);
        TestUtil.Near(0.6f, input.Y);
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
        Assert.False(b.Update(false, false, 5f));
        Assert.False(b.Expired);
        Assert.False(b.Update(true, false, 0.01f));
        int fired = 0;
        for (int i = 0; i < 200; i++)
            if (b.Update(false, false, 0.05f))
                fired++;
        Assert.Equal(1, fired);
        Assert.True(b.Expired);
        b.Update(true, false, 0.01f);
        Assert.False(b.Expired);
    }

    [Fact]
    public void FourSecondsBehindTheBack_StillHeld()
    {
        var b = new BlinkHold();
        for (int i = 0; i < 40; i++)
            Assert.False(b.Update(false, false, 0.1f));
        Assert.False(b.Expired);
    }

    [Fact]
    public void ADisconnectedController_OpensAtOnce_UntilTrackedAgain()
    {
        var b = new BlinkHold();
        Assert.True(b.Update(false, true, 0.01f));
        Assert.True(b.Expired);
        // Back as a device but not tracked yet: still open, never fired twice.
        Assert.False(b.Update(false, false, 0.01f));
        Assert.True(b.Expired);
        b.Update(true, false, 0.01f);
        Assert.False(b.Expired);
    }
}
