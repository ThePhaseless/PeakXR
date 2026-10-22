namespace PeakXR.Core.Tests;

public class SocialIntentTests
{
    static SocialKind Body(SocialFacts f, out DenyReason why) => SocialIntent.Body(f, out why);

    [Fact]
    public void HelpableFriend_EmptyHand_Reaches()
    {
        Assert.Equal(
            SocialKind.Reach,
            Body(new SocialFacts { Helpable = true, OnHandOrArm = true }, out var why)
        );
        Assert.Equal(DenyReason.None, why);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KickMode_NeverReaches(bool holding)
    {
        var f = new SocialFacts
        {
            KickMode = true,
            Helpable = true,
            OnHandOrArm = true,
            HoldingItem = holding,
        };
        Assert.Equal(SocialKind.None, Body(f, out var why));
        Assert.Equal(DenyReason.None, why);
    }

    [Fact]
    public void KickMode_StillCarriesAPassedOutFriend()
    {
        var f = new SocialFacts { KickMode = true, PassedOut = true };
        Assert.Equal(SocialKind.Carry, Body(f, out _));
    }

    [Fact]
    public void HelpableFriend_HoldingItem_HandsFull()
    {
        var f = new SocialFacts { Helpable = true, HoldingItem = true };
        Assert.Equal(SocialKind.None, Body(f, out var why));
        Assert.Equal(DenyReason.HandsFull, why);
    }

    [Fact]
    public void HelpableFriend_Climbing_NotWhileClimbing()
    {
        var f = new SocialFacts { Helpable = true, Climbing = true };
        Body(f, out var why);
        Assert.Equal(DenyReason.NotWhileClimbing, why);
    }

    [Fact]
    public void Climbing_HandOnWall_LeavesTheGripToTheWall()
    {
        var f = new SocialFacts
        {
            Helpable = true,
            Climbing = true,
            HandOnWall = true,
            OnHandOrArm = true,
        };
        Assert.Equal(SocialKind.None, Body(f, out var why));
        Assert.Equal(DenyReason.None, why);
    }

    [Fact]
    public void FineFriend_HandOrArm_NotInTrouble()
    {
        Body(new SocialFacts { OnHandOrArm = true }, out var why);
        Assert.Equal(DenyReason.NotInTrouble, why);
    }

    [Fact]
    public void FineFriend_OtherPart_FallsThrough()
    {
        Assert.Equal(SocialKind.None, Body(new SocialFacts(), out var why));
        Assert.Equal(DenyReason.None, why);
    }

    [Fact]
    public void FineFriend_HandNearWall_FallsThrough()
    {
        Body(new SocialFacts { OnHandOrArm = true, HandOnWall = true }, out var why);
        Assert.Equal(DenyReason.None, why);
    }

    [Fact]
    public void PassedOut_Carries()
    {
        Assert.Equal(SocialKind.Carry, Body(new SocialFacts { PassedOut = true }, out var why));
        Assert.Equal(DenyReason.None, why);
    }

    [Fact]
    public void PassedOut_CarriedBySomeoneElse_Occupied()
    {
        var f = new SocialFacts { PassedOut = true, CarriedByOther = true };
        Assert.Equal(SocialKind.None, Body(f, out var why));
        Assert.Equal(DenyReason.Occupied, why);
    }

    [Fact]
    public void PassedOut_WhileCarryingSomeone_HandsFull()
    {
        // RPCA_StartCarry would drop the friend already carried instead (CharacterCarrying.cs:68-71).
        var f = new SocialFacts { PassedOut = true, CarryingOther = true };
        Body(f, out var why);
        Assert.Equal(DenyReason.HandsFull, why);
    }

    [Fact]
    public void CarriedByMe_Drops()
    {
        Assert.Equal(SocialKind.Interact, Body(new SocialFacts { CarriedByMe = true }, out _));
    }

    [Fact]
    public void Bot_IsNeverABodyTarget()
    {
        var f = new SocialFacts
        {
            IsBot = true,
            PassedOut = true,
            OnHandOrArm = true,
        };
        Assert.Equal(SocialKind.None, Body(f, out var why));
        Assert.Equal(DenyReason.None, why);
    }

    [Theory]
    [InlineData(true, true, false, false, DenyReason.NotWhileClimbing)]
    [InlineData(false, false, false, false, DenyReason.NeedsGround)]
    [InlineData(false, true, true, false, DenyReason.NoStamina)]
    [InlineData(false, true, false, true, DenyReason.TooSoon)]
    [InlineData(false, true, false, false, DenyReason.None)]
    public void Shove_RefusesWhatTheKickRefuses(
        bool climbing,
        bool grounded,
        bool noStamina,
        bool kicking,
        DenyReason expected
    ) => Assert.Equal(expected, SocialIntent.Shove(climbing, grounded, noStamina, kicking));
}

public class ShoveGestureTests
{
    static readonly Vector3 Forward = new(0f, 0f, 1f);

    [Fact]
    public void BothPalmsForward_FiresOnce()
    {
        var g = new ShoveGesture();
        var push = Forward * 2f;
        Assert.True(g.Update(push, push, true, Forward, out var dir));
        Assert.True(Vector3.Dot(dir, Forward) > 0.99f);
        // Still pushing: one shove per push.
        Assert.False(g.Update(push, push, true, Forward, out _));
    }

    [Fact]
    public void ReArms_AfterTheHandsSlow()
    {
        var g = new ShoveGesture();
        var push = Forward * 2f;
        g.Update(push, push, true, Forward, out _);
        g.Update(Vector3.Zero, Vector3.Zero, true, Forward, out _);
        Assert.True(g.Update(push, push, true, Forward, out _));
    }

    [Fact]
    public void OneHand_IsNoShove()
    {
        var g = new ShoveGesture();
        Assert.False(g.Update(Forward * 2f, Vector3.Zero, true, Forward, out _));
    }

    [Fact]
    public void ArmSwing_HandsOpposite_IsNoShove()
    {
        var g = new ShoveGesture();
        Assert.False(g.Update(Forward * 2f, -Forward * 2f, true, Forward, out _));
    }

    [Fact]
    public void Slow_IsNoShove()
    {
        var g = new ShoveGesture();
        Assert.False(g.Update(Forward, Forward, true, Forward, out _));
    }

    [Fact]
    public void ClosedFists_AreNoShove()
    {
        var g = new ShoveGesture();
        Assert.False(g.Update(Forward * 2f, Forward * 2f, false, Forward, out _));
    }

    [Fact]
    public void Downward_IsNoShove()
    {
        var g = new ShoveGesture();
        var down = new Vector3(0f, -2f, 0f);
        Assert.False(g.Update(down, down, true, Forward, out _));
    }
}
