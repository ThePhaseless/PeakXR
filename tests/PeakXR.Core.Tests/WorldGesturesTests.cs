namespace PeakXR.Core.Tests;

public class TwoHandGateTests
{
    [Fact]
    public void OneHandRefusesOnceAfterDelay()
    {
        var g = new TwoHandGate { Delay = 0.6f };
        Assert.False(g.Update(1, 0.5f));
        Assert.True(g.Update(1, 0.2f));
        Assert.False(g.Update(1, 1f));
    }

    [Fact]
    public void BothHandsNeverRefuse()
    {
        var g = new TwoHandGate { Delay = 0.6f };
        for (int i = 0; i < 10; i++)
            Assert.False(g.Update(2, 0.2f));
    }

    [Fact]
    public void LettingGoRearms()
    {
        var g = new TwoHandGate { Delay = 0.6f };
        Assert.True(g.Update(1, 0.7f));
        Assert.False(g.Update(0, 0.1f));
        Assert.False(g.Update(1, 0.3f));
        Assert.True(g.Update(1, 0.4f));
    }

    [Fact]
    public void SecondHandArrivingResetsTheTimer()
    {
        var g = new TwoHandGate { Delay = 0.6f };
        Assert.False(g.Update(1, 0.5f));
        Assert.False(g.Update(2, 0.1f));
        Assert.False(g.Update(1, 0.5f));
    }
}

public class FriendBearingTests
{
    [Fact]
    public void SpotLiesFromTheHandTowardTheFriendAtHandHeight()
    {
        var spot = FriendBearing.Spot(new Vector3(0, 1, 0), new Vector3(10, 5, 0), 0.25f);
        Assert.Equal(0.25f, spot.X, 3);
        Assert.Equal(1f, spot.Y, 3);
        Assert.Equal(0f, spot.Z, 3);
    }

    [Fact]
    public void FriendStraightAboveGoesUp()
    {
        var spot = FriendBearing.Spot(new Vector3(0, 1, 0), new Vector3(0, 9, 0), 0.25f);
        Assert.Equal(1.25f, spot.Y, 3);
    }

    [Fact]
    public void FriendAtTheHandGivesTheHand()
    {
        var hand = new Vector3(2, 1, 3);
        Assert.Equal(hand, FriendBearing.Spot(hand, hand, 0.25f));
    }
}

public class CookCheckTests
{
    [Theory]
    [InlineData(true, true, false, DenyReason.None)]
    [InlineData(true, false, false, DenyReason.NotLit)]
    [InlineData(true, false, true, DenyReason.UsedUp)]
    [InlineData(false, true, false, DenyReason.CantUse)]
    [InlineData(false, false, false, DenyReason.NotLit)]
    public void ReasonFollowsFireThenFood(
        bool cookable,
        bool lit,
        bool spent,
        DenyReason expected
    ) => Assert.Equal(expected, CookCheck.Reason(cookable, lit, spent));
}

public class CreditsSkipTests
{
    [Fact]
    public void ReadyOnlyOnTheFrameItCrossesTheDelay()
    {
        Assert.False(CreditsSkip.BecameReady(19f, 19.9f));
        Assert.True(CreditsSkip.BecameReady(19.9f, 20.1f));
        Assert.False(CreditsSkip.BecameReady(20.1f, 20.2f));
    }
}

public class HeatGradientTests
{
    [Fact]
    public void WarmerTowardTheColumn()
    {
        Assert.Equal(0f, HeatGradient.Closeness(0.6f, 0.5f));
        Assert.Equal(0.5f, HeatGradient.Closeness(0.25f, 0.5f), 3);
        Assert.Equal(1f, HeatGradient.Closeness(0f, 0.5f));
    }
}

public class PalmContactTests
{
    static readonly Vector3 Wall = new(0, 1, 1);

    [Fact]
    public void OpenPalmFacingTheSurfaceTouches() =>
        Assert.True(PalmContact.On(Vector3.UnitZ, new Vector3(0, 1, 0.97f), Wall, 0.05f));

    [Fact]
    public void BackOfTheHandDoesNot() =>
        Assert.False(PalmContact.On(-Vector3.UnitZ, new Vector3(0, 1, 0.97f), Wall, 0.05f));

    [Fact]
    public void TooFarDoesNot() =>
        Assert.False(PalmContact.On(Vector3.UnitZ, new Vector3(0, 1, 0.8f), Wall, 0.05f));

    [Fact]
    public void HandInsideTheSurfaceTouches() =>
        Assert.True(PalmContact.On(Vector3.UnitZ, Wall, Wall, 0.05f));
}

public class AltarSocketTests
{
    [Fact]
    public void OwnSocketWhenFree() =>
        Assert.Equal(2, AltarSocket.For(2, new[] { 0, -1, -1, -1 }, 4));

    [Fact]
    public void FirstFreeTypeWhenOwnIsTaken() =>
        Assert.Equal(1, AltarSocket.For(0, new[] { 0, -1, -1, -1 }, 4));

    [Fact]
    public void NoneWhenFull() => Assert.Equal(-1, AltarSocket.For(1, new[] { 0, 1, 2, 3 }, 4));

    [Fact]
    public void BadTypeIsNone() =>
        Assert.Equal(-1, AltarSocket.For(99, new[] { -1, -1, -1, -1 }, 4));
}

public class AltarSocketReachTests
{
    const float Head = 1.6f;

    [Fact]
    public void ReachableSocketIsPlainDistance() =>
        Assert.Equal(
            0.2f,
            AltarSocket.Reach(new Vector3(0, 1.2f, 0.2f), new Vector3(0, 1.2f, 0), Head),
            3
        );

    [Fact]
    public void HighSocketCountsHeldUpUnderIt() =>
        Assert.Equal(
            0f,
            AltarSocket.Reach(new Vector3(0.1f, 1.9f, 0), new Vector3(0, 2.7f, 0), Head)
        );

    [Fact]
    public void HighSocketWithTheHandAtTheChestIsShort() =>
        Assert.True(
            AltarSocket.Reach(new Vector3(0, 1.3f, 0), new Vector3(0, 2.7f, 0), Head) > 0.3f
        );

    [Fact]
    public void HighSocketRaisedButOffToTheSideIsShort() =>
        Assert.True(
            AltarSocket.Reach(new Vector3(0.8f, 1.9f, 0), new Vector3(0, 2.7f, 0), Head) > 0.3f
        );
}

public class LidCapTests
{
    [Fact]
    public void TrackedLidUsesItsLift()
    {
        Assert.True(LidCap.TryFraction(true, 0.4f, true, true, out var f));
        Assert.Equal(0.4f, f);
    }

    [Fact]
    public void TwoHandLidWithoutBothHandsTrackedHoldsAtZero()
    {
        Assert.True(LidCap.TryFraction(false, 0.7f, true, true, out var f));
        Assert.Equal(0f, f);
    }

    [Fact]
    public void OrdinaryCaseNotOnTheLidKeepsTheVanillaHold() =>
        Assert.False(LidCap.TryFraction(false, 0.7f, false, true, out _));
}
