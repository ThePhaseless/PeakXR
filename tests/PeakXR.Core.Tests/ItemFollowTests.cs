using System.Numerics;

namespace PeakXR.Core.Tests;

public class ItemFollowTests
{
    [Fact]
    public void WeldHoldsWhileTheItemStaysOnItsHand()
    {
        var g = new WeldGate();
        for (int i = 0; i < 20; i++)
            Assert.False(g.Update(0.05f, 0.02f));
    }

    [Fact]
    public void AHandPastTheArmsReachReleasesTheWeld()
    {
        // A stoop: the hand goes 0.5 m below where the standing arm can bring the item.
        var g = new WeldGate();
        bool released = false;
        for (int i = 0; i < 10; i++)
            released = g.Update(0.5f, 0.02f);
        Assert.True(released);
    }

    [Fact]
    public void ABriefJoltDoesNotRelease()
    {
        var g = new WeldGate();
        Assert.False(g.Update(0.5f, 0.02f));
        Assert.False(g.Update(0.02f, 0.02f));
        Assert.False(g.Update(0.5f, 0.02f));
    }

    [Fact]
    public void RejoinsOnlyOnceTheArmCatchesUp()
    {
        var g = new WeldGate();
        for (int i = 0; i < 10; i++)
            g.Update(0.5f, 0.02f);
        // Between rejoin and release: stays free.
        for (int i = 0; i < 10; i++)
            Assert.True(g.Update(0.1f, 0.02f));
        bool released = true;
        for (int i = 0; i < 10; i++)
            released = g.Update(0.02f, 0.02f);
        Assert.False(released);
    }

    [Fact]
    public void FollowClosesMostOfTheGapEachTick()
    {
        var v = ItemFollow.Velocity(Vector3.Zero, new Vector3(0f, -0.5f, 0f), Vector3.Zero, 0.02f);
        float moved = -v.Y * 0.02f;
        Assert.InRange(moved, 0.1f, 0.5f);
    }

    [Fact]
    public void FollowCarriesTheHandsOwnSpeed()
    {
        // On target, moving with the hand: keeps the hand's velocity, no lag build-up.
        var hand = new Vector3(1f, 0f, 0f);
        var v = ItemFollow.Velocity(Vector3.Zero, Vector3.Zero, hand, 0.02f);
        Assert.Equal(hand, v);
    }

    [Fact]
    public void FollowSpeedIsCapped() =>
        Assert.True(
            ItemFollow
                .Velocity(Vector3.Zero, new Vector3(100f, 0f, 0f), Vector3.Zero, 0.02f)
                .Length()
                <= ItemFollow.MaxSpeed + 1e-3f
        );

    [Fact]
    public void SpinTurnsTowardTheTargetRotation()
    {
        var target = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f);
        var w = ItemFollow.Spin(Quaternion.Identity, target, 0.02f);
        Assert.True(w.Y > 0f);
        Assert.InRange(w.Y * 0.02f, 0.05f, 0.5f);
    }
}
