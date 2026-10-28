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
        for (int i = 0; i < 60; i++)
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
    public void AnArmCatchingUpForHalfASecondKeepsTheWeld()
    {
        // mp31: scripted controllers jump between poses and the arm lags 0.25-0.67 m for a moment; with the ghost hand
        // showing the real hand, only a stuck arm lets go.
        var g = new WeldGate();
        for (int i = 0; i < 20; i++)
            Assert.False(g.Update(0.6f, 0f, 0.02f));
        for (int i = 0; i < 30; i++)
            Assert.False(g.Update(0.02f, 0f, 0.02f));
    }

    [Fact]
    public void AnArmStuckForOverASecondReleases()
    {
        var g = new WeldGate();
        bool released = false;
        for (int i = 0; i < 60; i++)
            released = g.Update(0.6f, 0f, 0.02f);
        Assert.True(released);
    }

    [Fact]
    public void AHandBlockedByTerrainReleasesQuickly()
    {
        // Terrain between the shoulder and the controller: the arm is stuck now, not catching up.
        var g = new WeldGate();
        bool released = false;
        for (int i = 0; i < 12; i++)
            released = g.Update(0.6f, 0f, 0.02f, blocked: true);
        Assert.True(released);
    }

    [Fact]
    public void AnArmThatCannotReachItsControllerKeepsItsWeldWithinItsShortfall()
    {
        // vr69 vr.230: crouched at a stoop the shoulder leaves the controller beyond the arm's length; the hand sits
        // 0.33 m off with the arm 0.2 m short. Only lag past that shortfall plus the margin lets go.
        var g = new WeldGate();
        for (int i = 0; i < 60; i++)
            Assert.False(g.Update(0.33f, 0f, 0.02f, shortfall: 0.2f));
        bool released = false;
        for (int i = 0; i < 60; i++)
            released = g.Update(0.5f, 0f, 0.02f, shortfall: 0.2f);
        Assert.True(released);
    }

    [Fact]
    public void AFreeItemRejoinsAnArmAtItsBestReach()
    {
        var g = new WeldGate();
        for (int i = 0; i < 60; i++)
            g.Update(0.6f, 0.6f, 0.02f, shortfall: 0f);
        bool released = true;
        for (int i = 0; i < 10; i++)
            released = g.Update(0.25f, 0.05f, 0.02f, shortfall: 0.2f);
        Assert.False(released);
    }

    [Theory]
    [InlineData(0.5f, 0.6f, 0f)]
    [InlineData(0.9f, 0.6f, 0.3f)]
    public void ShortfallIsTheDistanceTheArmCannotCover(
        float toController,
        float arm,
        float expected
    ) =>
        Assert.Equal(
            expected,
            WeldGate.Shortfall(Vector3.Zero, new Vector3(0f, -toController, 0f), arm),
            3
        );

    [Fact]
    public void AStoopedCrouchedArmShortOfItsControllerKeepsTheWeld()
    {
        // vr65 vr.230: crouched at a 0.47 m stoop the arm ends about 0.13 m short of its controller; that is reach
        // the body has, not an arm that cannot get there, so the apple stays welded.
        var g = new WeldGate();
        for (int i = 0; i < 60; i++)
            Assert.False(g.Update(0.16f, 0f, 0.02f));
    }

    [Fact]
    public void AStoopedArmRejoinsWithoutStandingUp()
    {
        // Free while the arm caught up, then back within 0.1 m of its controller while still stooped: rejoins.
        var g = new WeldGate();
        for (int i = 0; i < 60; i++)
            g.Update(0.6f, 0.6f, 0.02f);
        bool released = true;
        for (int i = 0; i < 10; i++)
            released = g.Update(0.08f, 0.05f, 0.02f);
        Assert.False(released);
    }

    [Fact]
    public void AnArmCatchingUpWithAStoopUntilTheCrouchLandsKeepsTheWeld()
    {
        // vr61 vr.230: the arm trails the stooping controller until the crouch lowers the shoulder, about 0.15 s.
        var g = new WeldGate();
        for (int i = 0; i < 8; i++)
            Assert.False(g.Update(0.25f, 0f, 0.02f));
        for (int i = 0; i < 30; i++)
            Assert.False(g.Update(0.03f, 0f, 0.02f));
    }

    [Fact]
    public void AFreshWeldSettlesBeforeItCanLetGoAgain()
    {
        var g = new WeldGate();
        for (int i = 0; i < 60; i++)
            g.Update(0.5f, 0.5f, 0.02f);
        for (int i = 0; i < 10; i++)
            g.Update(0.02f, 0.02f, 0.02f);
        Assert.False(g.Released);
        // The snap of the rejoin jolts the arm: within the settle time it does not flip back.
        for (int i = 0; i < 20; i++)
            Assert.False(g.Update(0.3f, 0f, 0.02f));
    }

    [Fact]
    public void RejoinsWithTheArmOnItsControllerAndTheItemNearby()
    {
        // The rejoin puts the hand on the item's grip, so the item need only be near, not exact.
        var g = new WeldGate();
        for (int i = 0; i < 60; i++)
            g.Update(0.5f, 0.5f, 0.02f);
        bool released = true;
        for (int i = 0; i < 10; i++)
            released = g.Update(0.02f, 0.1f, 0.02f);
        Assert.False(released);
    }

    [Fact]
    public void RejoinsOnlyWithTheArmBackOnItsControllerAndAtTheItem()
    {
        var g = new WeldGate();
        for (int i = 0; i < 60; i++)
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

    static bool Finite(Vector3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    [Theory]
    [InlineData(0.5f)]
    [InlineData(2.5f)]
    [InlineData(-3f)]
    public void SpinIsTheSameForEitherSignOfTheTarget(float angle)
    {
        // q and -q are one rotation: the spin takes the short way to it either way.
        var from = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.3f);
        var target = Quaternion.CreateFromAxisAngle(
            Vector3.Normalize(new Vector3(1f, 2f, 0.5f)),
            angle
        );
        var a = ItemFollow.Spin(from, target, 0.02f);
        var b = ItemFollow.Spin(from, Quaternion.Negate(target), 0.02f);
        var c = ItemFollow.Spin(Quaternion.Negate(from), target, 0.02f);
        TestUtil.Near(a, b);
        TestUtil.Near(a, c);
        // Never the long way: one tick turns no more than the angle between them.
        Assert.True(
            a.Length() * 0.02f <= TestUtil.AngleDeg(from, target) * MathF.PI / 180f + 1e-3f
        );
    }

    [Fact]
    public void SpinIsZeroOnTheTarget()
    {
        var q = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1.2f);
        Assert.Equal(
            Vector3.Zero,
            ItemFollow.Spin(Quaternion.Identity, Quaternion.Identity, 0.02f)
        );
        Assert.Equal(Vector3.Zero, ItemFollow.Spin(q, q, 0.02f));
        Assert.Equal(Vector3.Zero, ItemFollow.Spin(q, Quaternion.Negate(q), 0.02f));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-0.02f)]
    public void SpinAndFollowWithoutATickStayFiniteAndBounded(float dt)
    {
        var target = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f);
        var w = ItemFollow.Spin(Quaternion.Identity, target, dt);
        Assert.True(Finite(w));
        Assert.True(w.Y > 0f && w.Length() <= ItemFollow.MaxSpin + 1e-3f);
        var v = ItemFollow.Velocity(Vector3.Zero, Vector3.UnitX, Vector3.Zero, dt);
        Assert.True(Finite(v));
        Assert.True(v.X > 0f && v.Length() <= ItemFollow.MaxSpeed + 1e-3f);
    }
}
