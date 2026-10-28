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
    public void HoldingItem_OtherHandNearDifferentItemAndWall_Climbs()
    {
        var c = Empty();
        c.HoldingItem = true;
        c.Candidate = GrabCandidate.Item;
        c.NearClimbable = true;
        Assert.Equal(GripPressAction.Climb, GripIntent.OnPress(c));
    }

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
    public void OtherHandOnOneHandedItem_IsNotSecondHand()
    {
        var c = Empty();
        c.HoldingItem = true;
        c.NearHeldItem = true;
        c.HeldHandedness = Handedness.OneHanded;
        Assert.NotEqual(GripPressAction.SecondHand, GripIntent.OnPress(c));
    }

    [Fact]
    public void OtherHandOnAnItemTheGameHoldsWithBothHands_BecomesSecondHand()
    {
        // Bing Bong: one-handed in the table, but vanilla puts both hands on it (not rightHandOnly).
        var c = Empty();
        c.HoldingItem = true;
        c.NearHeldItem = true;
        c.HeldHandedness = Handedness.OneHanded;
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
        // A fist with the index on the trigger (touch, no pull) is a grip, not a finger gun: the thumbs-up pose
        // (EmoteGestures.ThumbsUp) and a plain grip on Index or Touch controllers.
        var fist = FingerCurlMapper.Map(0f, true, 1f, false);
        Assert.False(GripIntent.IsPointing(fist));
        Assert.False(GripIntent.IsFingerGun(fist));
        Assert.True(GripIntent.IsPointing(FingerCurlMapper.Map(0f, false, 1f, false)));
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
    public void Release_ExtensionClaim_IgnoredForEmptyHand()
    {
        Assert.Equal(
            GripReleaseAction.Keep,
            GripIntent.OnRelease(false, true, 9f, 2f, true, true, extensionClaims: true)
        );
    }

    [Fact]
    public void PointedInteract_WaitsWhileTheFingerGunAims()
    {
        // A pointing hand is aiming a ping: however long it holds, the interact never comes due (WP6 found a held
        // ping aim at an item picking it up after 0.35 s).
        float at = 0.35f;
        for (float now = 0f; now < 2f; now += 0.1f)
        {
            at = GripIntent.DeferPointedInteract(at, pointing: true, now, FingerGun.DefaultWindow);
            Assert.True(at > now);
        }
    }

    [Fact]
    public void PointedInteract_ComesDueOneWindowAfterTheIndexCurls()
    {
        float at = GripIntent.DeferPointedInteract(
            0.35f,
            pointing: true,
            1f,
            FingerGun.DefaultWindow
        );
        at = GripIntent.DeferPointedInteract(at, pointing: false, 1.1f, FingerGun.DefaultWindow);
        TestUtil.Near(1f + FingerGun.DefaultWindow, at);
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
    public void OneHanded_ItemHand_HangsAndKeepsItsItem()
    {
        // One-handed play has no free hand: the item hand takes the handle, and vanilla keeps the item while hanging.
        var c = Empty();
        c.HoldingItem = true;
        c.ThisHandHolds = true;
        c.Candidate = GrabCandidate.Interactible;
        c.CandidateIsHandle = true;
        c.OneHanded = true;
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
