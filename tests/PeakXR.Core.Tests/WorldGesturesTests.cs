using System.Numerics;

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
