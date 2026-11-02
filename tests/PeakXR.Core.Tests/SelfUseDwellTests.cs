namespace PeakXR.Core.Tests;

/// <summary>A held item never starts its use on the way past your own body: the walk from a front-of-body hold to a
/// belt slot, played frame by frame through the real slot layout and torso zone.</summary>
public class SelfUseDwellTests
{
    private const float Dt = 1f / 90f;

    /// <summary>Eyes 1.6 m up, looking along +Z, so the neck pivot and the slots are the game's layout.</summary>
    private static readonly Vector3 Eyes = new(0f, 1.6f, 0f);

    /// <summary>HeldGestures' torso radius and the press's exit distance.</summary>
    private const float TorsoRadius = 0.22f;
    private const float Exit = 0.05f;

    /// <summary>How far a held bandage reaches from the grip towards the chest (HeldItemPoints.Nearest).</summary>
    private const float ItemReach = 0.06f;

    private static readonly BodySlot[] Slots = BodySlotLayout.Create(
        BodySlotLayout.Belt,
        BodySlotLayout.MaxHotbar,
        0.12f,
        0f
    );

    private static Vector3 Neck => BodySlotLayout.Neck(Eyes, Quaternion.Identity);

    private static Vector3[] Anchors()
    {
        var a = new Vector3[Slots.Length];
        for (int i = 0; i < a.Length; i++)
            a[i] = BodySlotLayout.Anchor(Neck, 0f, Slots[i]);
        return a;
    }

    private static float Torso(Vector3 hand) =>
        BodyZones.TorsoDistance(hand, Eyes, Vector3.UnitZ, TorsoRadius) - ItemReach;

    /// <summary>The release's reach: BodyInventory.TryHeldRelease finds a slot at 1.25 times its radius.</summary>
    private static bool Slot(Vector3 hand) =>
        BodySlotLayout.Find(hand, Anchors(), Slots, Slots.Length, 1.25f, Neck, 0f) >= 0;

    /// <summary>Plays a hand along a straight line at <paramref name="speed"/>, then holds it there
    /// <paramref name="settle"/> seconds, as a player does before opening the grip. True if the use ever started.</summary>
    private static bool Carry(Vector3 from, Vector3 to, float speed, float settle)
    {
        var use = new SelfUseDwell();
        float length = Vector3.Distance(from, to);
        float end = length / speed + settle;
        for (float t = 0f; t < end; t += Dt)
        {
            var hand = Vector3.Lerp(from, to, MathF.Min(1f, t * speed / length));
            if (use.Update(hand, Torso(hand) <= Exit, Slot(hand), Dt))
                return true;
        }
        return false;
    }

    private static Vector3 FrontHold => Eyes + new Vector3(0.1f, -0.45f, 0.35f);

    private static Vector3 RightSlot => Anchors()[2];

    [Fact]
    public void TheWholeBeltSitsInTheBodyPressZone()
    {
        // Why the belt starts a use: every slot's centre is within the press's reach of the torso.
        foreach (var anchor in new[] { Anchors()[0], Anchors()[1], Anchors()[2] })
            Assert.True(Torso(anchor) <= Exit, $"{anchor} is {Torso(anchor):0.00} m off the torso");
    }

    [Theory]
    [InlineData(0.8f)]
    [InlineData(0.4f)]
    [InlineData(0.2f)]
    public void ACarryToABeltSlotAndAPauseThereStartsNothing(float speed) =>
        Assert.False(Carry(FrontHold, RightSlot, speed, settle: 1f));

    [Theory]
    [InlineData(0.8f)]
    [InlineData(0.2f)]
    public void ACarryAcrossTheChestStartsNothing(float speed)
    {
        // From one side of the body to the other: in the zone all the way, never a slot.
        var from = Eyes + new Vector3(-0.2f, -0.4f, 0.1f);
        var to = Eyes + new Vector3(0.2f, -0.4f, 0.1f);
        Assert.False(Carry(from, to, speed, settle: 0f));
    }

    [Fact]
    public void HeldStillOnTheChestItStartsAfterTheDwellAndNotBefore()
    {
        var use = new SelfUseDwell();
        var chest = Eyes + new Vector3(0.05f, -0.45f, 0.08f);
        float started = -1f;
        for (float t = 0f; t < 2f && started < 0f; t += Dt)
        {
            if (use.Update(chest, true, false, Dt))
                started = t;
        }
        Assert.InRange(started, SelfUseDwell.BodyDwell - 2f * Dt, SelfUseDwell.BodyDwell + 2f * Dt);
        Assert.True(SelfUseDwell.BodyDwell >= 0.5f, "long enough to see and leave");
    }

    [Fact]
    public void ALivingHandsTremorStillCountsAsStill()
    {
        // 1.5 cm of wobble at a few hertz: a real hand held to the chest.
        var use = new SelfUseDwell();
        var chest = Eyes + new Vector3(0.05f, -0.45f, 0.08f);
        bool started = false;
        for (float t = 0f; t < SelfUseDwell.BodyDwell + 0.1f; t += Dt)
            started |= use.Update(
                chest + new Vector3(0.015f * MathF.Sin(t * 25f), 0.015f * MathF.Cos(t * 19f), 0f),
                true,
                false,
                Dt
            );
        Assert.True(started);
    }

    [Fact]
    public void ASlotInReachNeverLetsItStartHoweverStillTheHand()
    {
        var use = new SelfUseDwell();
        for (float t = 0f; t < 3f; t += Dt)
            Assert.False(use.Update(RightSlot, true, true, Dt));
    }

    [Fact]
    public void ASlotInReachStopsAUseUnderWay()
    {
        // The cast has started on the chest and the hand goes on to the belt: nothing may be spent, and the holster's
        // SwitchBlocked (a cast under way) has to clear before the grip opens.
        var use = new SelfUseDwell();
        var chest = Eyes + new Vector3(0.05f, -0.45f, 0.08f);
        for (float t = 0f; t < 1f; t += Dt)
            use.Update(chest, true, false, Dt);
        Assert.True(use.Active);
        Assert.False(use.Update(chest, true, true, Dt));
        Assert.False(use.Active);
    }

    [Fact]
    public void AUseUnderWayRidesOutTheHandsMotionUntilItLeavesTheBody()
    {
        // A wiping or wrapping motion does not cancel it; only leaving does.
        var use = new SelfUseDwell();
        var chest = Eyes + new Vector3(0.05f, -0.45f, 0.08f);
        for (float t = 0f; t < 1f; t += Dt)
            use.Update(chest, true, false, Dt);
        Assert.True(use.Update(chest + new Vector3(0.2f, 0f, 0f), true, false, Dt));
        Assert.False(use.Update(chest + new Vector3(0.6f, 0f, 0f), false, false, Dt));
    }

    [Fact]
    public void AnItemThatArrivesAtTheBodyWaitsForTheHandToMoveThenAHoldCounts()
    {
        // Drawn from a belt slot, or picked up from a waist-high table: the hand did not choose this contact. Lifting
        // it to the chest without ever leaving the torso must still work.
        var use = new SelfUseDwell();
        use.Arrive(RightSlot);
        var belt = RightSlot;
        for (float t = 0f; t < 1f; t += Dt)
            Assert.False(use.Update(belt, true, false, Dt));
        var chest = Eyes + new Vector3(0.1f, -0.45f, 0.08f);
        for (float t = 0f; t < 0.3f; t += Dt)
            Assert.False(use.Update(Vector3.Lerp(belt, chest, t / 0.3f), true, false, Dt));
        bool started = false;
        for (float t = 0f; t < SelfUseDwell.BodyDwell + 0.1f; t += Dt)
            started |= use.Update(chest, true, false, Dt);
        Assert.True(started);
    }

    [Fact]
    public void ItSaysItIsSettlingOnlyOnceTheHandHasRestedAWhile()
    {
        // The ring shows a hold, not a carry: half the dwell is more than a hand in motion ever collects.
        var use = new SelfUseDwell();
        var chest = Eyes + new Vector3(0.05f, -0.45f, 0.08f);
        use.Update(chest, true, false, Dt);
        Assert.False(use.Settling);
        for (float t = 0f; t < SelfUseDwell.BodyDwell * 0.6f; t += Dt)
            use.Update(chest, true, false, Dt);
        Assert.True(use.Settling);
    }

    [Fact]
    public void AMovingHandDoesNotCountForAFriendsDwellEither()
    {
        // The same hand rule on a friend's chest: a slow carry past them, both bodies standing still, never heals.
        var over = new HandOver { Dwell = HandOver.DwellAt(chest: true) };
        var still = new HandStill();
        var from = new Vector3(0f, 1.2f, 0.4f);
        for (float t = 0f; t < 2f; t += Dt)
        {
            var hand = from + new Vector3(0.2f * t, 0f, 0f);
            Assert.NotEqual(
                HandOverState.Apply,
                over.Update(true, false, true, false, Dt, still.Update(hand))
            );
        }
    }

    [Fact]
    public void AFriendsChestWaitsAsLongAsYourOwnBody() =>
        Assert.Equal(SelfUseDwell.BodyDwell, HandOver.DwellAt(chest: true));
}
