namespace PeakXR.Core.Tests;

public class GripIntentTests
{
    static GripContext Empty() => new GripContext { PhysicalGrab = true, PhysicalClimb = true };

    [Fact]
    public void EmptyHands_NearItem_GrabsItem()
    {
        var c = Empty();
        c.Candidate = GrabCandidate.Item;
        Assert.Equal(GripPressAction.GrabItem, GripIntent.OnPress(c));
    }

    [Fact]
    public void HoldingItem_OtherHandNearDifferentItem_PocketsIt()
    {
        // The game pockets an item picked up while another is held (Item.Interact, Item.cs:578-591): the free hand
        // takes the one it touched instead of whatever the head aims at.
        var c = Empty();
        c.HoldingItem = true;
        c.Candidate = GrabCandidate.Item;
        Assert.Equal(GripPressAction.GrabItem, GripIntent.OnPress(c));
    }

    [Fact]
    public void HoldingHand_NearDifferentItem_Regrips()
    {
        var c = Empty();
        c.HoldingItem = true;
        c.ThisHandHolds = true;
        c.Candidate = GrabCandidate.Item;
        Assert.Equal(GripPressAction.Regrip, GripIntent.OnPress(c));
    }

    [Fact]
    public void HoldingItem_FreeHandOnAnItemByAWall_PocketsIt()
    {
        // Issue #13: the free hand's item glowed and the grip climbed instead; on the ground the item under the hand
        // wins, as it does for empty hands (ItemBeatsWall).
        var c = Empty();
        c.HoldingItem = true;
        c.Candidate = GrabCandidate.Item;
        c.NearClimbable = true;
        Assert.Equal(GripPressAction.GrabItem, GripIntent.OnPress(c));
    }

    [Fact]
    public void HoldingItem_FreeHandOnAnItemByAWall_OnTheWall_Climbs()
    {
        var c = Empty();
        c.HoldingItem = true;
        c.Candidate = GrabCandidate.Item;
        c.NearClimbable = true;
        c.BodyClimbing = true;
        c.OnWall = true;
        Assert.Equal(GripPressAction.Climb, GripIntent.OnPress(c));
    }

    [Theory]
    [InlineData(true, 0f, true)]
    // The game's ground check drops for a physics step; it still lets a body jump 0.2 s after (CharacterMovement.cs:412).
    [InlineData(false, 0.1f, true)]
    [InlineData(false, 0.2f, true)]
    [InlineData(false, 0.25f, false)]
    public void OnFeet_KeepsTheGamesGroundGrace(bool grounded, float sinceGrounded, bool onFeet) =>
        Assert.Equal(onFeet, GripIntent.OnFeet(grounded, sinceGrounded));

    [Fact]
    public void PickupWait_WaitsOutTheEquipCooldown()
    {
        // Item.Interact ignores a pickup within 0.25 s of an equip (Item.cs:580): the press waits for its end.
        TestUtil.Near(0.15f, GripIntent.PickupWait(now: 10.1f, lastEquip: 10f));
        Assert.Equal(0f, GripIntent.PickupWait(now: 10.3f, lastEquip: 10f));
        // A long switch lock (an emote's LockFromSwitching(3), CharacterAnimations.cs:422) is not waited out: the
        // press goes in and the game's refusal shows.
        Assert.Equal(0f, GripIntent.PickupWait(now: 10f, lastEquip: 12f));
    }

    [Theory]
    [InlineData(0.05f, 0.14f, true)]
    // Out of the window or past the slack: the fresh search decides.
    [InlineData(GrabGrace.Seconds + 0.01f, 0.1f, false)]
    [InlineData(0.05f, 0.12f + GrabGrace.Slack + 0.01f, false)]
    public void GrabGrace_KeepsTheLastHighlight(float since, float distance, bool keeps) =>
        Assert.Equal(keeps, GrabGrace.Keeps(since, distance, 0.12f));

    [Fact]
    public void OtherHandOnHeldItem_BecomesSecondHand()
    {
        var c = Empty();
        c.HoldingItem = true;
        c.NearHeldItem = true;
        c.HeldHandedness = Handedness.OptionalTwoHand;
        Assert.Equal(GripPressAction.SecondHand, GripIntent.OnPress(c));
    }

    [Fact]
    public void OtherHandOnOneHandItem_IsNotSecondHand()
    {
        var c = Empty();
        c.HoldingItem = true;
        c.NearHeldItem = true;
        c.HeldHandedness = Handedness.OneHand;
        Assert.NotEqual(GripPressAction.SecondHand, GripIntent.OnPress(c));
    }

    [Fact]
    public void OtherHandOnAnItemTheGameHoldsWithBothHands_BecomesSecondHand()
    {
        // Bing Bong: a one-hand item in the table, but vanilla puts both hands on it (not rightHandOnly).
        var c = Empty();
        c.HoldingItem = true;
        c.NearHeldItem = true;
        c.HeldHandedness = Handedness.OneHand;
        c.HeldBothHandsInGame = true;
        Assert.Equal(GripPressAction.SecondHand, GripIntent.OnPress(c));
    }

    [Fact]
    public void HoldingHand_NothingNear_Regrips()
    {
        var c = Empty();
        c.HoldingItem = true;
        c.ThisHandHolds = true;
        Assert.Equal(GripPressAction.Regrip, GripIntent.OnPress(c));
    }

    [Fact]
    public void NonItemInteractible_IsGrabbedEvenWhileHolding()
    {
        var c = Empty();
        c.HoldingItem = true;
        c.Candidate = GrabCandidate.Interactible;
        Assert.Equal(GripPressAction.GrabInteractible, GripIntent.OnPress(c));
    }

    [Fact]
    public void NearWall_Climbs()
    {
        var c = Empty();
        c.NearClimbable = true;
        Assert.Equal(GripPressAction.Climb, GripIntent.OnPress(c));
    }

    [Fact]
    public void ItemBeatsWall()
    {
        var c = Empty();
        c.NearClimbable = true;
        c.Candidate = GrabCandidate.Item;
        Assert.Equal(GripPressAction.GrabItem, GripIntent.OnPress(c));
    }

    [Fact]
    public void OnWall_GripOnNothing_PressesNothing()
    {
        // A climber's hand that misses the next hold is no interact button: it picked up whatever the head or a
        // pointing hand aimed at (tester: "with the grips, items sometimes get picked up").
        var c = Empty();
        c.BodyClimbing = true;
        c.OnWall = true;
        Assert.Equal(GripPressAction.None, GripIntent.OnPress(c));
        c.Pointing = true;
        c.Aimed = AimedTarget.Other;
        Assert.Equal(GripPressAction.None, GripIntent.OnPress(c));
    }

    [Fact]
    public void OnWall_PointedAtTaker_StillInteracts()
    {
        var c = Empty();
        c.BodyClimbing = true;
        c.OnWall = true;
        c.HoldingItem = true;
        c.Pointing = true;
        c.Aimed = AimedTarget.ItemTaker;
        Assert.Equal(GripPressAction.Interact, GripIntent.OnPress(c));
    }

    [Fact]
    public void OnWall_ClimbOff_GripStaysTheInteractButton()
    {
        var c = new GripContext { OnWall = true, BodyClimbing = true };
        Assert.Equal(GripPressAction.Interact, GripIntent.OnPress(c));
    }

    [Fact]
    public void Airborne_GripShortOfTheWall_PressesNothing()
    {
        // vr.251 (run 5): pulled up a wall by the rescue hook, the free hand closed just short of the face (probe
        // Approach) and fell back to interact, which never catches; the grip must stay a catch the body flies into.
        var c = Empty();
        c.BodyClimbing = true;
        c.Airborne = true;
        Assert.Equal(GripPressAction.None, GripIntent.OnPress(c));
        // Still the interact button with climbing off, and a pointed aim at a taker still interacts.
        Assert.Equal(
            GripPressAction.Interact,
            GripIntent.OnPress(new GripContext { BodyClimbing = true, Airborne = true })
        );
        c.HoldingItem = true;
        c.Pointing = true;
        c.Aimed = AimedTarget.ItemTaker;
        Assert.Equal(GripPressAction.Interact, GripIntent.OnPress(c));
    }

    [Fact]
    public void TogglesOff_FallBackToInteract()
    {
        var c = new GripContext { NearClimbable = true, Candidate = GrabCandidate.Item };
        Assert.Equal(GripPressAction.Interact, GripIntent.OnPress(c));
    }

    [Fact]
    public void Release_FastWhileHolding_Throws()
    {
        var r = GripIntent.OnRelease(
            thisHandHolds: true,
            canDrop: true,
            speed: 3f,
            throwSpeed: 2f,
            throwEnabled: true,
            releaseToDrop: false
        );
        Assert.Equal(GripReleaseAction.Throw, r);
    }

    [Fact]
    public void Release_SlowWhileHolding_KeepsItem()
    {
        var r = GripIntent.OnRelease(true, true, 0.5f, 2f, true, false);
        Assert.Equal(GripReleaseAction.Keep, r);
    }

    [Fact]
    public void Release_SlowWithReleaseToDrop_Drops()
    {
        var r = GripIntent.OnRelease(true, true, 0.5f, 2f, true, true);
        Assert.Equal(GripReleaseAction.Drop, r);
    }

    [Fact]
    public void Release_ThrowDisabled_FastIsNotThrow()
    {
        Assert.Equal(
            GripReleaseAction.Keep,
            GripIntent.OnRelease(true, true, 9f, 2f, false, false)
        );
    }

    [Fact]
    public void Release_CannotDrop_Keeps()
    {
        Assert.Equal(GripReleaseAction.Keep, GripIntent.OnRelease(true, false, 9f, 2f, true, true));
    }

    [Theory]
    [InlineData(GripReleaseAction.Throw)]
    [InlineData(GripReleaseAction.Drop)]
    public void ReleaseRefusal_MidCast_IsTooSoon(GripReleaseAction result)
    {
        // CharacterItems.DoDropping (CharacterItems.cs:254-258) ignores drop and throw while the item casts.
        Assert.Equal(DenyReason.TooSoon, GripIntent.ReleaseRefusal(result, true, 0.3f));
    }

    [Fact]
    public void ReleaseRefusal_MidCastOnUnthrowable_StillTooSoon()
    {
        Assert.Equal(
            DenyReason.TooSoon,
            GripIntent.ReleaseRefusal(GripReleaseAction.Throw, false, 0.1f)
        );
    }

    [Fact]
    public void ReleaseRefusal_ThrowUnthrowable_CantThrow()
    {
        Assert.Equal(
            DenyReason.CantThrow,
            GripIntent.ReleaseRefusal(GripReleaseAction.Throw, false, 0f)
        );
    }

    [Theory]
    [InlineData(GripReleaseAction.Throw, true, 0f)]
    [InlineData(GripReleaseAction.Drop, false, 0f)]
    [InlineData(GripReleaseAction.Keep, true, 0.5f)]
    [InlineData(GripReleaseAction.Extension, true, 0.5f)]
    public void ReleaseRefusal_Otherwise_None(
        GripReleaseAction result,
        bool canThrow,
        float progress
    )
    {
        Assert.Equal(DenyReason.None, GripIntent.ReleaseRefusal(result, canThrow, progress));
    }

    [Fact]
    public void Release_EmptyHand_DoesNothing()
    {
        Assert.Equal(GripReleaseAction.Keep, GripIntent.OnRelease(false, true, 9f, 2f, true, true));
    }

    [Fact]
    public void Pointing_IndexOutOthersCurled()
    {
        Assert.True(GripIntent.IsPointing(new FingerCurls(0.7f, 0.1f, 0.9f, 0.9f, 0.9f)));
        Assert.False(GripIntent.IsPointing(new FingerCurls(0.7f, 0.8f, 0.9f, 0.9f, 0.9f)));
        Assert.False(GripIntent.IsPointing(new FingerCurls(0f, 0f, 0.1f, 0.1f, 0.1f)));
    }

    [Fact]
    public void Pointing_NotWithTheFingerResting_OnTheTrigger()
    {
        // A fist with the index on the trigger (touch, no pull) is a grip, not a point: the thumbs-up pose
        // (EmoteGestures.ThumbsUp) and a plain grip on Index or Touch controllers.
        var fist = FingerCurlMapper.Map(0f, true, 1f, false);
        Assert.False(GripIntent.IsPointing(fist));
        Assert.False(GripIntent.IsPingAimPoint(fist, 1f));
        Assert.True(GripIntent.IsPointing(FingerCurlMapper.Map(0f, false, 1f, false)));
    }

    [Fact]
    public void PingAimPoint_IndexOutThumbLiftedGripOpen()
    {
        // Interaction.Ping.Hand = Pointing: the aiming point is made with the grip open, so it never makes a pointed
        // grip that interacts with what it points at (#89). The thumb on the stick or face buttons is no aim.
        var open = FingerCurlMapper.Map(0f, false, 0.1f, false);
        Assert.True(GripIntent.IsPingAimPoint(open, 0.1f));
        Assert.False(GripIntent.IsPingAimPoint(FingerCurlMapper.Map(0f, false, 0.1f, true), 0.1f));
        Assert.False(GripIntent.IsPingAimPoint(FingerCurlMapper.Map(0f, true, 0.1f, false), 0.1f));
        // The old finger gun (grip closed) is a pointed grip, not a ping aim.
        var gun = FingerCurlMapper.Map(0f, false, 1f, false);
        Assert.True(GripIntent.IsPointing(gun));
        Assert.False(GripIntent.IsPingAimPoint(gun, 1f));
        // A Vive wand (no trigger touch sensor, ControllerCurls counts the index as resting) never aims.
        Assert.False(GripIntent.IsPingAimPoint(FingerCurlMapper.Map(0f, true, 0f, false), 0f));
    }

    [Fact]
    public void ExtensionClaim_BeatsGrabClimbAndInteract()
    {
        var c = Empty();
        c.Candidate = GrabCandidate.Item;
        c.NearClimbable = true;
        c.ExtensionClaim = true;
        Assert.Equal(GripPressAction.Extension, GripIntent.OnPress(c));
    }

    [Fact]
    public void SecondHand_BeatsExtensionClaim()
    {
        var c = Empty();
        c.HoldingItem = true;
        c.NearHeldItem = true;
        c.HeldHandedness = Handedness.TwoHanded;
        c.ExtensionClaim = true;
        Assert.Equal(GripPressAction.SecondHand, GripIntent.OnPress(c));
    }

    [Fact]
    public void ExtensionClaim_WorksWithPhysicalTogglesOff()
    {
        var c = new GripContext { ExtensionClaim = true };
        Assert.Equal(GripPressAction.Extension, GripIntent.OnPress(c));
    }

    [Fact]
    public void Release_ClaimedByExtension_BeatsThrowAndCanDrop()
    {
        Assert.Equal(
            GripReleaseAction.Extension,
            GripIntent.OnRelease(true, false, 9f, 2f, true, false, extensionClaims: true)
        );
    }

    [Fact]
    public void Release_FastOverASlotOrPack_Throws()
    {
        // Issue #83: an underhand toss let go over the front belt slot or by a pack is a throw, not a holster: the
        // extensions (whose release hook acts as it claims) are not asked, so no claim reaches OnRelease.
        bool fast = GripIntent.Throws(true, true, 9f, 2f, true);
        Assert.True(fast);
        Assert.Equal(
            GripReleaseAction.Throw,
            GripIntent.OnRelease(true, true, 9f, 2f, true, false, extensionClaims: !fast)
        );
        // A placed, slow release there is still the holster.
        Assert.False(GripIntent.Throws(true, true, 0.5f, 2f, true));
        // No throw the game would take (hand throws off, an item that cannot be dropped or thrown): the slot keeps
        // its chance, rather than a refusal.
        Assert.False(GripIntent.Throws(true, true, 9f, 2f, false));
        Assert.False(GripIntent.Throws(false, true, 9f, 2f, true));
        Assert.False(GripIntent.Throws(true, false, 9f, 2f, true));
    }

    [Fact]
    public void Release_ExtensionClaim_IgnoredForEmptyHand()
    {
        Assert.Equal(
            GripReleaseAction.Keep,
            GripIntent.OnRelease(false, true, 9f, 2f, true, true, extensionClaims: true)
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HoldingHand_TighteningNearUnprobedRock_Keeps(bool bodyClimbing)
    {
        // VR run 38: the holding hand's grip closed with only the grip sphere touching something, was read as a climb,
        // and the climb start put the sticky item down.
        var c = Empty();
        c.HoldingItem = true;
        c.ThisHandHolds = true;
        c.NearClimbable = true;
        c.BodyClimbing = bodyClimbing;
        Assert.Equal(GripPressAction.Regrip, GripIntent.OnPress(c));
    }

    [Fact]
    public void HoldingHand_OnAProbedHold_StillCatchesTheWall()
    {
        var c = Empty();
        c.HoldingItem = true;
        c.ThisHandHolds = true;
        c.NearClimbable = true;
        c.HoldConfirmed = true;
        c.BodyClimbing = true;
        Assert.Equal(GripPressAction.Climb, GripIntent.OnPress(c));
    }

    [Fact]
    public void HoldingHand_GripOpen_KeepsTheItem() =>
        Assert.Equal(
            GripReleaseAction.Keep,
            GripIntent.OnRelease(true, true, 0.1f, 2f, true, releaseToDrop: false)
        );

    [Theory]
    [InlineData(GripPressAction.None)]
    [InlineData(GripPressAction.Interact)]
    [InlineData(GripPressAction.Regrip)]
    [InlineData(GripPressAction.Climb)]
    public void ItemArrivingInAClosedGrip_IsNotArmed(GripPressAction action) =>
        // A hotbar or wheel select, a friend's hand-over or a game equip while the hand happens to grip.
        Assert.False(ReleaseArming.OnArrive(gripClosed: true, action, isTheGrabbedItem: false));

    [Fact]
    public void TheItemTheGripClosedOn_IsArmed()
    {
        Assert.True(ReleaseArming.OnArrive(true, GripPressAction.GrabItem, isTheGrabbedItem: true));
        // A slot draw: the grip closed on the slot that gave it.
        Assert.True(ReleaseArming.OnArrive(true, GripPressAction.Extension, false));
        // A grip that fell back to the game's interact and picked up the hovered item: that item is the grip's.
        Assert.True(ReleaseArming.OnArrive(true, GripPressAction.Interact, isTheGrabbedItem: true));
        // Another item arriving during a grab is not the one grabbed.
        Assert.False(
            ReleaseArming.OnArrive(true, GripPressAction.GrabItem, isTheGrabbedItem: false)
        );
    }

    [Fact]
    public void ItemArrivingInAnOpenHand_IsArmedForTheNextClose() =>
        Assert.True(ReleaseArming.OnArrive(gripClosed: false, GripPressAction.None, false));

    [Fact]
    public void ClosedGrip_ItemArrives_Release_Keeps()
    {
        bool armed = ReleaseArming.OnArrive(true, GripPressAction.Regrip, false);
        Assert.Equal(
            GripReleaseAction.Keep,
            GripIntent.OnRelease(true, true, 9f, 2f, true, releaseToDrop: true, armed: armed)
        );
    }

    [Fact]
    public void CloseThenRelease_Drops()
    {
        // A fresh close arms the release.
        bool armed = ReleaseArming.OnPress();
        Assert.Equal(
            GripReleaseAction.Drop,
            GripIntent.OnRelease(true, true, 0.1f, 2f, true, releaseToDrop: true, armed: armed)
        );
        Assert.Equal(
            GripReleaseAction.Throw,
            GripIntent.OnRelease(true, true, 9f, 2f, true, releaseToDrop: true, armed: armed)
        );
    }

    static GripContext HoldingTwoHanded()
    {
        var c = Empty();
        c.HoldingItem = true;
        c.HeldHandedness = Handedness.TwoHanded;
        return c;
    }

    [Fact]
    public void FreeHandAwayFromTheHeldItem_AtAClimbable_Climbs()
    {
        // VR run 51: a free hand on a chain 0.45 m from the held launcher joined the launcher instead.
        var c = HoldingTwoHanded();
        c.NearClimbable = true;
        Assert.Equal(GripPressAction.Climb, GripIntent.OnPress(c));
    }

    [Fact]
    public void FreeHandBesideTheHeldItem_OnARopeOrChain_GrabsIt()
    {
        // Brushing the launcher's body is not taking its second grip when the hand closes on a line.
        var c = HoldingTwoHanded();
        c.NearHeldItem = true;
        c.Candidate = GrabCandidate.Interactible;
        Assert.Equal(GripPressAction.GrabInteractible, GripIntent.OnPress(c));
    }

    [Fact]
    public void FreeHandBesideTheHeldItem_AtAWall_Climbs()
    {
        var c = HoldingTwoHanded();
        c.NearHeldItem = true;
        c.NearClimbable = true;
        Assert.Equal(GripPressAction.Climb, GripIntent.OnPress(c));
    }

    [Fact]
    public void FreeHandAtTheOffHandGrip_IsSecondHand_EvenByAWall()
    {
        var c = HoldingTwoHanded();
        c.AtSecondGrip = true;
        c.NearHeldItem = true;
        c.NearClimbable = true;
        c.Candidate = GrabCandidate.Interactible;
        Assert.Equal(GripPressAction.SecondHand, GripIntent.OnPress(c));
    }

    [Fact]
    public void FreeHandOnTheHeldItem_NothingElseThere_IsSecondHand()
    {
        var c = HoldingTwoHanded();
        c.NearHeldItem = true;
        Assert.Equal(GripPressAction.SecondHand, GripIntent.OnPress(c));
    }

    [Fact]
    public void ItemHand_OnAClimbHandle_KeepsItsItem()
    {
        // Vanilla hangs from a handle without putting the item away (ClimbHandle.cs:54-67, CharacterClimbing.cs:640-647):
        // the free hand holds the handle. A hand that holds a torch does not hang from a piton with it.
        var c = Empty();
        c.HoldingItem = true;
        c.ThisHandHolds = true;
        c.Candidate = GrabCandidate.Interactible;
        c.CandidateIsHandle = true;
        Assert.Equal(GripPressAction.Regrip, GripIntent.OnPress(c));
    }

    [Fact]
    public void FreeHand_OnAClimbHandle_HangsWhileTheOtherHandHolds()
    {
        var c = Empty();
        c.HoldingItem = true;
        c.Candidate = GrabCandidate.Interactible;
        c.CandidateIsHandle = true;
        Assert.Equal(GripPressAction.GrabInteractible, GripIntent.OnPress(c));
    }

    [Fact]
    public void ItemHand_OnOtherInteractibles_StillGrabs()
    {
        // A rope or vine grab puts the item away itself (RopeSegment.cs:42, JungleVine.cs:113); luggage opens.
        var c = Empty();
        c.HoldingItem = true;
        c.ThisHandHolds = true;
        c.Candidate = GrabCandidate.Interactible;
        Assert.Equal(GripPressAction.GrabInteractible, GripIntent.OnPress(c));
    }

    [Fact]
    public void ItemHand_OnAPitonAgainstTheWall_KeepsItsItem_NoClimb()
    {
        // VR run 59: the piton sits on climbable rock, so the item hand's grip read as a wall catch, the climb start
        // put the item away (Stowed). On a handle the item hand keeps its item; only HandsFull follows.
        var c = Empty();
        c.HoldingItem = true;
        c.ThisHandHolds = true;
        c.Candidate = GrabCandidate.Interactible;
        c.CandidateIsHandle = true;
        c.NearClimbable = true;
        c.HoldConfirmed = true;
        c.BodyClimbing = true;
        Assert.Equal(GripPressAction.Regrip, GripIntent.OnPress(c));
        c.BodyClimbing = false;
        Assert.Equal(GripPressAction.Regrip, GripIntent.OnPress(c));
    }

    static GripContext ItemHandByAPiton(float toHandle, float toRock)
    {
        var c = Empty();
        c.HoldingItem = true;
        c.ThisHandHolds = true;
        c.Candidate = GrabCandidate.Interactible;
        c.CandidateIsHandle = true;
        c.HandleDistance = toHandle;
        c.RockDistance = toRock;
        c.NearClimbable = true;
        c.HoldConfirmed = true;
        c.BodyClimbing = true;
        return c;
    }

    [Fact]
    public void ItemHand_ClosingOnTheHandle_RockBehind_Regrips() =>
        Assert.Equal(GripPressAction.Regrip, GripIntent.OnPress(ItemHandByAPiton(0.02f, 0.06f)));

    [Fact]
    public void ItemHand_OnRockAwayFromAHandle_Climbs() =>
        // Vanilla climbs and puts the item away (CharacterClimbing.cs:569); a piton 0.3 m off changes nothing.
        Assert.Equal(GripPressAction.Climb, GripIntent.OnPress(ItemHandByAPiton(0.3f, 0.01f)));

    [Fact]
    public void ItemHand_OnAPitonSetInTheRock_Regrips()
    {
        // VR run 60: the piton sits on the rock face, so the rock under the hand reads a little nearer than the
        // handle's own surface. Within the handle's depth it is still the handle the hand closes on.
        Assert.Equal(GripPressAction.Regrip, GripIntent.OnPress(ItemHandByAPiton(0.06f, 0.03f)));
        Assert.Equal(GripPressAction.Regrip, GripIntent.OnPress(ItemHandByAPiton(0.09f, 0.0f)));
    }
}

public class PickupAnswerTests
{
    [Theory]
    // A pocket slot: the pickup worked.
    [InlineData((byte)1, false, true, PickupCue.Success)]
    [InlineData((byte)1, true, true, PickupCue.Success)]
    // Pockets full, empty hands: the temporary slot is equipped into the hand that asked.
    [InlineData((byte)250, false, false, PickupCue.Success)]
    // Pockets full while holding something: vanilla equips it over the held item.
    [InlineData((byte)250, false, true, PickupCue.Swapped)]
    // Pockets full while climbing: vanilla drops it behind the hip.
    [InlineData((byte)250, true, true, PickupCue.Dropped)]
    [InlineData((byte)250, true, false, PickupCue.Dropped)]
    public void TheCueSaysWhatTheGameDid(byte slot, bool climbing, bool holding, PickupCue cue) =>
        Assert.Equal(cue, GripIntent.PickupAnswer(slot, climbing, holding));
}

public class GestureInteractTests
{
    [Fact]
    public void NoPose_InteractStaysDue()
    {
        Assert.Equal(1f, GripIntent.GestureInteractAt(1f, posed: false, gripAt: 1f, now: 1f));
    }

    [Fact]
    public void Posed_InteractWaitsWhileThePoseHolds()
    {
        // Issue #82: a thumbs-up fist or a fist at the mouth pressed the head-aimed interact at once.
        float at = GripIntent.GestureInteractAt(1f, posed: true, gripAt: 1f, now: 1f);
        Assert.Equal(1f + GripIntent.GestureWindow, at, 3);
        at = GripIntent.GestureInteractAt(at, posed: true, gripAt: 1f, now: 1.2f);
        Assert.Equal(1.2f + GripIntent.GestureWindow, at, 3);
        // The pose ended: due a window later, not pushed on.
        Assert.Equal(at, GripIntent.GestureInteractAt(at, posed: false, gripAt: 1f, now: 1.3f));
    }

    [Fact]
    public void Posed_NeverWaitsPastTheCap()
    {
        // A gaze grab made with the thumb lifted comes late, never lost.
        float at = GripIntent.GestureInteractAt(1f, posed: true, gripAt: 1f, now: 5f);
        Assert.Equal(1f + GripIntent.GestureMaxWait, at, 3);
    }

    [Fact]
    public void Posed_NeverShortensALongerWait()
    {
        Assert.Equal(9f, GripIntent.GestureInteractAt(9f, posed: true, gripAt: 1f, now: 1f));
    }

    [Fact]
    public void QuickPosedTap_InteractsAtTheRelease()
    {
        // Issue #82: a grip opened before its posed wait ran out pressed nothing at all.
        float at = GripIntent.GestureInteractAt(1f, posed: true, gripAt: 1f, now: 1f);
        Assert.True(GripIntent.ReleaseTaps(at, 1f, 1.1f, gestureTook: false));
        // The gesture took the hand (the thumbs-up fired, the hand talks): nothing.
        Assert.False(GripIntent.ReleaseTaps(at, 1f, 1.1f, gestureTook: true));
        // The wait was over: the interact already pressed while the grip was closed.
        Assert.False(GripIntent.ReleaseTaps(at, 1f, at + 0.01f, gestureTook: false));
        // A thumbs-up held 0.5 s and given up before its dwell is no tap, though the pose kept the wait going.
        at = GripIntent.GestureInteractAt(at, posed: true, gripAt: 1f, now: 1.5f);
        Assert.False(GripIntent.ReleaseTaps(at, 1f, 1.5f, gestureTook: false));
    }
}
