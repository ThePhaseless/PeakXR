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

    /// <summary>How far a small held item reaches from the grip towards the chest (HeldItemPoints.Nearest). Big items
    /// reach far more; see <see cref="ABigItemRestedAwayFromTheBodyStartsNothing"/>.</summary>
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
    private static bool Carry(
        Vector3 from,
        Vector3 to,
        float speed,
        float settle,
        float reach = ItemReach
    )
    {
        var use = new SelfUseDwell();
        float length = Vector3.Distance(from, to);
        float end = length / speed + settle;
        for (float t = 0f; t < end; t += Dt)
        {
            var hand = Vector3.Lerp(from, to, MathF.Min(1f, t * speed / length));
            if (use.Update(hand, Contact(hand, reach), Slot(hand), Dt))
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
    public void ABigItemCarriedToAnyBeltSlotStartsNothing(float speed)
    {
        for (int i = 0; i < 3; i++)
            Assert.False(Carry(FrontHold, Anchors()[i], speed, settle: 1f, reach: 0.6f));
    }

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

    /// <summary>The body press as HeldGestures feeds it: the item touches, and the hand itself is at the torso.</summary>
    private static bool Contact(Vector3 hand, float reach) =>
        BodyZones.TorsoDistance(hand, Eyes, Vector3.UnitZ, TorsoRadius) - reach <= Exit
        && BodyZones.HandAtTorso(hand, Eyes, Vector3.UnitZ, TorsoRadius);

    private static bool Rests(Vector3 hand, float reach, float seconds)
    {
        var use = new SelfUseDwell();
        bool started = false;
        for (float t = 0f; t < seconds; t += Dt)
            started |= use.Update(hand, Contact(hand, reach), false, Dt);
        return started;
    }

    /// <summary>Beside the hip with the arm hanging, as a player stands holding something.</summary>
    private static Vector3 HipHang => Eyes + new Vector3(0.25f, -0.8f, 0f);

    [Theory]
    [InlineData(0.39f)] // Bandages' far corner
    [InlineData(0.60f)] // First Aid Kit
    [InlineData(0.77f)] // Aloe Vera
    public void ABigItemRestedAwayFromTheBodyStartsNothing(float reach)
    {
        Assert.True(
            BodyZones.TorsoDistance(FrontHold, Eyes, Vector3.UnitZ, TorsoRadius) - reach <= Exit,
            "the item itself has to reach the torso from there, or this proves nothing"
        );
        Assert.False(Rests(FrontHold, reach, 2f));
        Assert.False(Rests(HipHang, reach, 2f));
    }

    [Theory]
    [InlineData(0.06f)]
    [InlineData(0.60f)]
    public void AnyItemHeldToTheChestStillStarts(float reach) =>
        Assert.True(Rests(Eyes + new Vector3(0.05f, -0.45f, 0.08f), reach, 1f));

    [Fact]
    public void AHandOutFromTheChestByAPalmStillCounts() =>
        Assert.True(
            BodyZones.HandAtTorso(
                Eyes + new Vector3(0f, -0.45f, 0.25f),
                Eyes,
                Vector3.UnitZ,
                TorsoRadius
            )
        );

    [Fact]
    public void AnItemOnTheOtherForearmCountsOnlyWithTheHandThere()
    {
        // HeldGestures' forearm radius. Hands idling side by side in front are 0.3 m apart: a kit reaches the other arm
        // from there, and that is not a press.
        const float Radius = 0.12f;
        var other = new Vector3(0f, 1.1f, 0.4f);
        var arm = other - Vector3.UnitZ * 0.17f;
        Assert.True(BodyZones.HandAtForearm(arm, other, Vector3.UnitZ, Radius));
        Assert.True(
            BodyZones.HandAtForearm(arm + Vector3.UnitX * 0.2f, other, Vector3.UnitZ, Radius)
        );
        Assert.False(
            BodyZones.HandAtForearm(arm + Vector3.UnitX * 0.3f, other, Vector3.UnitZ, Radius)
        );
    }

    [Fact]
    public void AHandOnTheChestStaysThereWithTheItemOverTheOtherForearm()
    {
        // #147: the diagnostic read torso 0.000, forearm true, hand there false, so the press never started. The free
        // hand idled 0.25 m out and 0.40 m ahead, and the Bandages' box reached its arm (arm distance negative).
        Assert.Equal(
            0f,
            BodyZones.Rest(0f, -0.1f, true, false, out bool there, out bool onForearm)
        );
        Assert.True(there);
        Assert.False(onForearm);
    }

    [Fact]
    public void AnItemOnTheForearmStillNeedsAHandAtOneOfThem()
    {
        // The hand on that forearm, off the torso: the wrap there.
        Assert.Equal(0f, BodyZones.Rest(0.1f, -0.05f, false, true, out bool there, out bool arm));
        Assert.True(there);
        Assert.True(arm);
        // Hands idling apart with a kit reaching across: nothing means it.
        BodyZones.Rest(0.1f, 0.02f, false, false, out there, out arm);
        Assert.False(there);
        // The torso nearer than the arm, or no arm in play: the torso alone answers.
        Assert.Equal(
            0.03f,
            BodyZones.Rest(0.03f, float.PositiveInfinity, true, false, out there, out arm)
        );
        Assert.True(there);
        Assert.False(arm);
    }

    [Fact]
    public void LosingContactStartsTheDwellOver()
    {
        var use = new SelfUseDwell();
        var chest = Eyes + new Vector3(0.05f, -0.45f, 0.08f);
        for (float t = 0f; t < 0.5f; t += Dt)
            use.Update(chest, true, false, Dt);
        Assert.True(use.Fraction > 0.5f);
        use.Update(chest, false, false, Dt);
        Assert.Equal(0f, use.Fraction);
        for (float t = 0f; t < 0.5f; t += Dt)
            Assert.False(use.Update(chest, true, false, Dt));
    }

    [Fact]
    public void TheDwellsAndTheStillRadiusAreTheseValues()
    {
        // Felt on a headset (#147): a change here is a change players feel, not a refactor.
        Assert.Equal(0.6f, SelfUseDwell.BodyDwell);
        Assert.Equal(0.3f, SelfUseDwell.MouthDwell);
        Assert.Equal(0.5f, SelfUseDwell.FaceDwell);
        Assert.Equal(0.05f, HandStill.Radius);
    }

    /// <summary>Plays a physical step: head and hand move together at <paramref name="speed"/> for a second.</summary>
    private static bool StepsWithHandStill(float speed, float dwell, bool onBody)
    {
        var use = new SelfUseDwell { Dwell = dwell };
        var from = Eyes + new Vector3(0.05f, -0.45f, 0.08f);
        bool started = false;
        for (float t = 0f; t < 1f; t += Dt)
        {
            var walked = Vector3.UnitZ * (speed * t);
            var hand = from + walked;
            started |= use.Update(
                onBody ? HandStill.OnBody(hand, Eyes + walked, Quaternion.Identity) : hand,
                true,
                false,
                Dt
            );
        }
        return started;
    }

    [Theory]
    [InlineData(0.6f)] // over the chest's limit of 8 cm/s
    [InlineData(0.3f)] // over the mouth's limit of 17 cm/s
    public void AHandRestingOnTheBodyStaysRestingAsTheBodySteps(float dwell)
    {
        Assert.False(StepsWithHandStill(0.3f, dwell, onBody: false));
        Assert.True(StepsWithHandStill(0.3f, dwell, onBody: true));
    }

    [Fact]
    public void AHandMovingOverAStillBodyStillMoves()
    {
        var hand = new Vector3(0.1f, 1.1f, 0.2f);
        Assert.Equal(
            0.3f,
            Vector3.Distance(
                HandStill.OnBody(hand, Eyes, Quaternion.Identity),
                HandStill.OnBody(hand + Vector3.UnitX * 0.3f, Eyes, Quaternion.Identity)
            ),
            3
        );
    }

    [Fact]
    public void AHeadTurnDoesNotMoveARestingHandOnTheBody()
    {
        // The neck pivot stays where it is when the head turns about it, which is the point of measuring from there.
        var neck = BodySlotLayout.Neck(Eyes, Quaternion.Identity);
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.6f);
        var turned = neck - Vector3.Transform(neck - Eyes, turn);
        var hand = new Vector3(0.1f, 1.1f, 0.2f);
        Assert.True(
            Vector3.Distance(
                HandStill.OnBody(hand, Eyes, Quaternion.Identity),
                HandStill.OnBody(hand, turned, turn)
            ) <= HandStill.Radius
        );
    }

    [Fact]
    public void WipingOnTheChestAsksForAStillHandAfterASecond()
    {
        var use = new SelfUseDwell();
        var chest = Eyes + new Vector3(0.05f, -0.45f, 0.08f);
        for (float t = 0f; t < 0.9f; t += Dt)
        {
            use.Update(chest + new Vector3(0.2f * MathF.Sin(t * 12f), 0f, 0f), true, false, Dt);
            Assert.False(use.NeedsStill);
        }
        for (float t = 0.9f; t < 1.1f; t += Dt)
            use.Update(chest + new Vector3(0.2f * MathF.Sin(t * 12f), 0f, 0f), true, false, Dt);
        Assert.True(use.NeedsStill);
        Assert.False(use.Active);
        use.Update(chest, false, false, Dt);
        Assert.False(use.NeedsStill);
    }

    [Fact]
    public void AFastCarryAcrossTheTorsoIsNotToldToHoldStill()
    {
        var use = new SelfUseDwell();
        var from = Eyes + new Vector3(-0.3f, -0.4f, 0.1f);
        for (float t = 0f; t < 0.75f; t += Dt)
        {
            use.Update(from + Vector3.UnitX * (0.8f * t), true, false, Dt);
            Assert.False(use.NeedsStill);
        }
    }

    [Fact]
    public void AUseUnderWayIsNotToldToHoldStill()
    {
        var use = new SelfUseDwell();
        var chest = Eyes + new Vector3(0.05f, -0.45f, 0.08f);
        for (float t = 0f; t < 2f; t += Dt)
            use.Update(chest, true, false, Dt);
        Assert.True(use.Active);
        Assert.False(use.NeedsStill);
    }

    [Fact]
    public void AnItemThatJustArrivedIsNotToldToHoldStill()
    {
        // The hand is still, and the use waits for it to move: "hold still" would say the opposite.
        var use = new SelfUseDwell();
        var chest = Eyes + new Vector3(0.05f, -0.45f, 0.08f);
        use.Arrive(chest);
        for (float t = 0f; t < 2f; t += Dt)
            use.Update(chest, true, false, Dt);
        Assert.False(use.NeedsStill);
    }

    [Fact]
    public void AHandRestingAtASlotIsToldItCanLetGo()
    {
        var use = new SelfUseDwell();
        for (float t = 0f; t < SelfUseDwell.BodyDwell - 2f * Dt; t += Dt)
        {
            use.Update(RightSlot, true, true, Dt);
            Assert.False(use.Parked);
        }
        for (int i = 0; i < 4; i++)
            use.Update(RightSlot, true, true, Dt);
        Assert.True(use.Parked);
        // Moving on, or out of the slot, takes it away at once.
        use.Update(RightSlot + Vector3.UnitX * 0.3f, true, true, Dt);
        Assert.False(use.Parked);
        for (float t = 0f; t < 1f; t += Dt)
            use.Update(RightSlot, true, true, Dt);
        use.Update(RightSlot, true, false, Dt);
        Assert.False(use.Parked);
    }

    [Fact]
    public void ReachingYourOwnSlotEndsAFriendsCastAtOnceAndItsNearRefusalStaysSpent()
    {
        var over = new HandOver { Dwell = HandOver.DwellAt(chest: true) };
        Assert.Equal(HandOverState.Apply, over.Update(true, false, true, false, 1f));
        // At the slot: the hand-over lets go now, not after its grace, so the release is not refused as mid-use.
        over.EndZone();
        Assert.Equal(
            HandOverState.None,
            over.Update(false, false, true, false, Dt, still: false, nearGap: 0.3f)
        );

        // A refused visit to the near band, a rest at a slot inside it, and on: the visit has not ended.
        var near = new HandOver { Dwell = HandOver.MouthDwell };
        HandOverState Step(bool still) =>
            near.Update(false, false, false, false, 0.2f, still, 0.3f, 0.3f, true);
        Step(true);
        Assert.Equal(HandOverState.Refuse, Step(true));
        near.EndZone();
        Assert.Equal(HandOverState.None, Step(false));
        Step(true);
        Assert.Equal(HandOverState.None, Step(true));
    }
}
