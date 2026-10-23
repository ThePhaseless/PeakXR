using System.Numerics;

namespace PeakXR.Core.Tests;

public class ItemFollowTests
{
    // Arguments: ragdoll hand to the controller's hand bone, ragdoll hand to the item's grip, dt.

    [Fact]
    public void AHandOnItsControllerKeepsTheWeldWhateverTheItemsGripOffset()
    {
        // vr53 vr.230: the hand 5 cm from its controller, the apple's grip offset far larger; must stay welded.
        var g = new WeldGate();
        for (int i = 0; i < 30; i++)
            Assert.False(g.Update(0.05f, 0.4f, 0.02f));
    }

    [Fact]
    public void AnArmPastItsReachReleasesTheWeld()
    {
        // A stoop: the ragdoll hand stops 0.5 m above its controller.
        var g = new WeldGate();
        bool released = false;
        for (int i = 0; i < 10; i++)
            released = g.Update(0.5f, 0f, 0.02f);
        Assert.True(released);
    }

    [Fact]
    public void ABriefLagOfAMovingArmDoesNotRelease()
    {
        // Ordinary hand motion: the arm trails a fast controller for a tick or two (vr53: the passport went free often).
        var g = new WeldGate();
        Assert.False(g.Update(0.3f, 0f, 0.02f));
        Assert.False(g.Update(0.3f, 0f, 0.02f));
        Assert.False(g.Update(0.05f, 0f, 0.02f));
        Assert.False(g.Update(0.3f, 0f, 0.02f));
    }

    [Fact]
    public void RejoinsOnlyWithTheArmBackOnItsControllerAndAtTheItem()
    {
        var g = new WeldGate();
        for (int i = 0; i < 10; i++)
            g.Update(0.5f, 0.5f, 0.02f);
        // Arm caught up, but the free item is stuck elsewhere: stays free (a rejoin would snap the hand onto it).
        for (int i = 0; i < 10; i++)
            Assert.True(g.Update(0.02f, 0.3f, 0.02f));
        // Between rejoin and release: stays free.
        for (int i = 0; i < 10; i++)
            Assert.True(g.Update(0.1f, 0.02f, 0.02f));
        bool released = true;
        for (int i = 0; i < 10; i++)
            released = g.Update(0.02f, 0.02f, 0.02f);
        Assert.False(released);
    }

    [Theory]
    [InlineData(0.1f, false)]
    [InlineData(0.29f, false)]
    [InlineData(0.31f, true)]
    [InlineData(1f, true)]
    public void AFarFreeItemSnapsToItsPlace(float gap, bool snap) =>
        Assert.Equal(snap, ItemFollow.Snaps(gap));

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
