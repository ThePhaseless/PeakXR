namespace PeakXR.Core.Tests;

public class HandOverTests
{
    [Fact]
    public void StandingClose_NeverUses()
    {
        var h = new HandOver();
        for (int i = 0; i < 200; i++)
            Assert.NotEqual(
                HandOverState.Apply,
                h.Update(inZone: true, trigger: false, usable: true)
            );
        Assert.Equal(HandOverState.Hint, h.Update(true, false, true));
    }

    [Fact]
    public void TriggerPressedAtTheMouth_Uses()
    {
        var h = new HandOver();
        h.Update(true, false, true);
        Assert.Equal(HandOverState.Apply, h.Update(true, true, true));
        Assert.Equal(HandOverState.Apply, h.Update(true, true, true));
        Assert.True(h.SuppressPrimary);
    }

    [Fact]
    public void TriggerHeldWhileArriving_IsNoHandOver()
    {
        // Eating on the way, or a trigger held for something else, never spends the item on the friend.
        var h = new HandOver();
        Assert.Equal(HandOverState.None, h.Update(true, true, true));
        Assert.False(h.SuppressPrimary);
        Assert.Equal(HandOverState.Hint, h.Update(true, false, true));
        Assert.Equal(HandOverState.Apply, h.Update(true, true, true));
    }

    [Fact]
    public void Unusable_RefusesOncePerPress_NotOnProximity()
    {
        var h = new HandOver();
        Assert.Equal(HandOverState.None, h.Update(true, false, false));
        Assert.Equal(HandOverState.Refuse, h.Update(true, true, false));
        Assert.Equal(HandOverState.None, h.Update(true, true, false));
        h.Update(true, false, false);
        Assert.Equal(HandOverState.Refuse, h.Update(true, true, false));
    }

    [Fact]
    public void UnusableItemWithItsOwnUse_KeepsItsTrigger()
    {
        // A torch or a rope spool near a friend's head is no hand-over: its trigger stays its own, nothing refused.
        var h = new HandOver();
        Assert.Equal(HandOverState.None, h.Update(true, false, false, ownUse: true));
        Assert.False(h.SuppressPrimary);
        Assert.Equal(HandOverState.None, h.Update(true, true, false, ownUse: true));
        Assert.False(h.SuppressPrimary);
    }

    [Fact]
    public void UnusableItemWithoutAUse_DoesNotTakeTheTriggerUntilPressed()
    {
        var h = new HandOver();
        h.Update(true, false, false);
        Assert.False(h.SuppressPrimary);
        Assert.Equal(HandOverState.Refuse, h.Update(true, true, false));
    }

    [Fact]
    public void ApplyingHand_RidesOutAShortWobbleOutOfTheZone()
    {
        // MP run 15: the food wobbled out of the mouth zone for a frame and the squeeze never fed again.
        var h = new HandOver();
        h.Update(true, false, true, false, 0.02f);
        Assert.Equal(HandOverState.Apply, h.Update(true, true, true, false, 0.02f));
        for (int i = 0; i < 10; i++)
            Assert.Equal(HandOverState.Apply, h.Update(false, true, true, false, 0.02f));
        Assert.Equal(HandOverState.Apply, h.Update(true, true, true, false, 0.02f));
    }

    [Fact]
    public void ApplyingHand_GivesUpAfterTheGrace()
    {
        var h = new HandOver();
        h.Update(true, false, true, false, 0.02f);
        h.Update(true, true, true, false, 0.02f);
        for (float t = 0f; t < h.Grace + 0.05f; t += 0.02f)
            h.Update(false, true, true, false, 0.02f);
        Assert.Equal(HandOverState.None, h.Update(false, true, true, false, 0.02f));
        // Back in with the trigger still held: a new squeeze is needed.
        Assert.Equal(HandOverState.None, h.Update(true, true, true, false, 0.02f));
    }

    [Theory]
    [InlineData(DenyReason.NoWall)]
    [InlineData(DenyReason.CantUse)]
    [InlineData(DenyReason.OutOfReach)]
    public void AtAFriendsMouth_AnItemRefusalSaysCantFeed(DenyReason own)
    {
        // MP run 22: a piton squeezed at the host's mouth said NoWall, its own placement refusal.
        Assert.Equal(DenyReason.CantUse, HandOver.ContextReason(own, atFriendMouth: true));
    }

    [Fact]
    public void AwayFromAMouth_TheItemsOwnReasonStands()
    {
        Assert.Equal(
            DenyReason.NoWall,
            HandOver.ContextReason(DenyReason.NoWall, atFriendMouth: false)
        );
    }

    [Fact]
    public void BesideAFriendsMouth_ASqueezeSaysCloser_AtTheirMouth()
    {
        // MP run 26 said NotHere (your own mouth) there; a silent squeeze would break the no-silent-failures rule.
        var r = HandOver.Context(DenyReason.NotHere, false, true, out bool atFriend);
        Assert.Equal(DenyReason.OutOfReach, r);
        Assert.True(atFriend);
    }

    [Fact]
    public void NearAFriend_OtherRefusalsStand()
    {
        var r = HandOver.Context(DenyReason.NoStamina, false, true, out bool atFriend);
        Assert.Equal(DenyReason.NoStamina, r);
        Assert.False(atFriend);
    }

    [Fact]
    public void AwayFromFriends_NotHereStands()
    {
        var r = HandOver.Context(DenyReason.NotHere, false, false, out bool atFriend);
        Assert.Equal(DenyReason.NotHere, r);
        Assert.False(atFriend);
    }

    [Fact]
    public void AtTheMouthWithAnUnfeedableItem_CantUseWins()
    {
        var r = HandOver.Context(DenyReason.NotHere, true, true, out bool atFriend);
        Assert.Equal(DenyReason.CantUse, r);
        Assert.False(atFriend);
    }

    [Fact]
    public void ContextNeverInventsARefusal()
    {
        Assert.Equal(DenyReason.None, HandOver.ContextReason(DenyReason.None, atFriendMouth: true));
    }

    [Fact]
    public void LeavingTheZone_Disarms()
    {
        var h = new HandOver();
        h.Update(true, false, true);
        Assert.Equal(HandOverState.None, h.Update(false, false, true));
        Assert.False(h.SuppressPrimary);
        Assert.Equal(HandOverState.None, h.Update(true, true, true));
    }

    // Food at a friend's mouth feeds like food at your own mouth: held there, no trigger needed.
    static HandOver Mouth() => new() { Dwell = HandOver.MouthDwell };

    [Fact]
    public void FoodHeldAtTheMouth_FeedsAfterTheDwell()
    {
        var h = Mouth();
        for (float t = 0f; t < 0.25f; t += 0.05f)
            Assert.Equal(HandOverState.Hint, h.Update(true, false, true, false, 0.05f));
        Assert.Equal(HandOverState.Apply, h.Update(true, false, true, false, 0.1f));
        Assert.Equal(HandOverState.Apply, h.Update(true, false, true, false, 0.05f));
        Assert.True(h.SuppressPrimary);
    }

    [Fact]
    public void FoodCarriedPastTheMouthWhileWalking_NeverFeeds()
    {
        // MP run 13 (#93): walking up to the host with food at chest height fed them after the dwell.
        var h = Mouth();
        for (int i = 0; i < 20; i++)
            Assert.Equal(
                HandOverState.Hint,
                h.Update(true, false, true, false, 0.05f, still: false)
            );
    }

    [Fact]
    public void TheDwellCountsFromWhenTheFeederStops()
    {
        var h = Mouth();
        h.Update(true, false, true, false, 0.25f, still: false);
        Assert.Equal(HandOverState.Hint, h.Update(true, false, true, false, 0.25f));
        Assert.Equal(HandOverState.Apply, h.Update(true, false, true, false, 0.1f));
        // Once it feeds, a step does not cut the feed short.
        Assert.Equal(HandOverState.Apply, h.Update(true, false, true, false, 0.05f, still: false));
    }

    [Fact]
    public void TheTriggerFeedsAtOnce_EvenWhileWalking()
    {
        var h = Mouth();
        h.Update(true, false, true, false, 0.05f, still: false);
        Assert.Equal(HandOverState.Apply, h.Update(true, true, true, false, 0.05f, still: false));
    }

    [Theory]
    [InlineData(0f, true)]
    [InlineData(0.49f, true)]
    [InlineData(0.5f, false)]
    [InlineData(1.4f, false)]
    public void StillMeansALevelSpeedUnderHalfAMetrePerSecond(float speed, bool still)
    {
        Assert.Equal(still, HandOver.Still(speed));
    }

    [Fact]
    public void DwellFeed_IgnoresATriggerHeldOnArrival()
    {
        var h = Mouth();
        h.Update(true, true, true, false, 0.2f);
        Assert.Equal(HandOverState.Apply, h.Update(true, true, true, false, 0.2f));
    }

    [Fact]
    public void DwellFeed_RidesOutAWobble_WithoutATrigger()
    {
        var h = Mouth();
        h.Update(true, false, true, false, 0.4f);
        Assert.Equal(HandOverState.Apply, h.Update(false, false, true, false, 0.1f));
        Assert.Equal(HandOverState.Apply, h.Update(true, false, true, false, 0.02f));
        for (float t = 0f; t < h.Grace + 0.05f; t += 0.05f)
            h.Update(false, false, true, false, 0.05f);
        Assert.Equal(HandOverState.None, h.Update(false, false, true, false, 0.05f));
        // Back in: the dwell starts over.
        Assert.Equal(HandOverState.Hint, h.Update(true, false, true, false, 0.05f));
    }

    [Fact]
    public void DwellWithAnUnfeedableItem_RefusesOncePerVisit()
    {
        var h = Mouth();
        Assert.Equal(HandOverState.None, h.Update(true, false, false, false, 0.2f));
        Assert.Equal(HandOverState.Refuse, h.Update(true, false, false, false, 0.2f));
        Assert.Equal(HandOverState.Refused, h.Update(true, false, false, false, 0.2f));
        h.Update(false, false, false, false, 0.2f);
        h.Update(true, false, false, false, 0.2f);
        Assert.Equal(HandOverState.Refuse, h.Update(true, false, false, false, 0.2f));
    }

    [Fact]
    public void DwellNeverTakesAnItemWithItsOwnUse()
    {
        var h = Mouth();
        for (int i = 0; i < 20; i++)
            Assert.Equal(HandOverState.None, h.Update(true, false, false, true, 0.1f));
    }

    [Fact]
    public void AHealingItemHeldOnTheChest_AppliesAfterTheDwell_NoTrigger()
    {
        // Issue #45: a bandage on a friend's chest works as pressing it to your own body does.
        var h = new HandOver { Dwell = HandOver.DwellAt(chest: true) };
        Assert.Equal(HandOverState.Hint, h.Update(true, false, true, false, 0.4f));
        Assert.Equal(HandOverState.Apply, h.Update(true, false, true, false, 0.4f));
    }

    [Fact]
    public void BothZonesDwell_TheChestAsLongAsAPressToYourOwnBody()
    {
        Assert.Equal(HandOver.MouthDwell, HandOver.DwellAt(chest: false));
        Assert.Equal(SelfUseDwell.BodyDwell, HandOver.DwellAt(chest: true));
        Assert.True(HandOver.DwellAt(chest: true) > 0f);
    }

    [Fact]
    public void TheDwellCountsOnlyWithTheItemsControllerHeldPlaceAtTheSpot()
    {
        // #119: the body weld held a bag on the host's head with the controller 0.63 m off, and food went in 1.15 s.
        var h = Mouth();
        for (int i = 0; i < 20; i++)
            Assert.Equal(
                HandOverState.Hint,
                h.Update(true, false, true, false, 0.1f, true, placeGap: 0.63f)
            );
        Assert.Equal(
            HandOverState.Hint,
            h.Update(true, false, true, false, 0.1f, true, placeGap: HandOver.PlaceRadius)
        );
        Assert.Equal(
            HandOverState.Hint,
            h.Update(true, false, true, false, 0.1f, true, placeGap: 0.2f)
        );
        Assert.Equal(
            HandOverState.Apply,
            h.Update(true, false, true, false, 0.1f, true, placeGap: 0.2f)
        );
    }

    [Fact]
    public void TheControllerLeavingThePlaceStartsTheDwellOver()
    {
        var h = Mouth();
        h.Update(true, false, true, false, 0.2f, true, placeGap: 0.1f);
        h.Update(true, false, true, false, 0.01f, true, placeGap: 0.5f);
        Assert.Equal(
            HandOverState.Hint,
            h.Update(true, false, true, false, 0.2f, true, placeGap: 0.1f)
        );
    }

    [Fact]
    public void TheTriggerStillFeedsAtOnceWithThePlaceFarOff()
    {
        var h = Mouth();
        h.Update(true, false, true, false, 0.01f, true, placeGap: 0.63f);
        Assert.Equal(
            HandOverState.Apply,
            h.Update(true, true, true, false, 0.01f, true, placeGap: 0.63f)
        );
    }

    [Fact]
    public void AnItemTheyCannotTakeIsNotRefusedWithThePlaceFarOff()
    {
        var h = Mouth();
        for (int i = 0; i < 20; i++)
            Assert.NotEqual(
                HandOverState.Refuse,
                h.Update(true, false, false, false, 0.1f, true, placeGap: 0.63f)
            );
    }

    [Fact]
    public void BothMoversHaveToBeStill()
    {
        Assert.True(HandOver.Still(0.1f, 0.1f));
        Assert.False(HandOver.Still(0.6f, 0.1f));
        // A friend walking into food held out is no hand-over.
        Assert.False(HandOver.Still(0.1f, 0.6f));
    }

    [Fact]
    public void ABandageOnTheChestOfAFriendWalkedIntoDoesNotCount()
    {
        // docs/controls.md: "Passing close never starts one."
        var h = new HandOver { Dwell = HandOver.DwellAt(chest: true) };
        for (int i = 0; i < 20; i++)
            Assert.NotEqual(
                HandOverState.Apply,
                h.Update(true, false, true, false, 0.1f, still: false, placeGap: 0.1f)
            );
    }

    static HandOverState Near(
        HandOver h,
        bool usable = true,
        float dt = 0.1f,
        bool trigger = false,
        bool ownUse = false,
        bool still = true,
        float placeGap = 0.4f,
        float nearGap = 0.35f,
        bool looking = true
    ) => h.Update(false, trigger, usable, ownUse, dt, still, placeGap, nearGap, looking);

    [Fact]
    public void JustShortOfTheMouth_FoodGetsTheRingAfterTheDwell()
    {
        // #112: with the trigger up, 0.22-0.45 m off the mouth said nothing at all.
        var h = Mouth();
        Assert.Equal(HandOverState.None, Near(h));
        Assert.Equal(HandOverState.None, Near(h));
        Assert.Equal(HandOverState.Hint, Near(h, dt: 0.2f));
        Assert.Equal(HandOverState.Hint, Near(h));
        Assert.False(h.SuppressPrimary);
    }

    [Fact]
    public void JustShortOfTheMouth_AnItemTheyCannotTakeIsRefusedOncePerVisit()
    {
        var h = Mouth();
        Assert.Equal(HandOverState.None, Near(h, usable: false));
        Assert.Equal(HandOverState.Refuse, Near(h, usable: false, dt: 0.3f));
        for (int i = 0; i < 20; i++)
            Assert.Equal(HandOverState.Refused, Near(h, usable: false));
        // Out of the band and back: a new try.
        h.Update(false, false, false, false, 0.1f);
        Near(h, usable: false, dt: 0.1f);
        Assert.Equal(HandOverState.Refuse, Near(h, usable: false, dt: 0.3f));
    }

    [Fact]
    public void JustShortOfTheMouth_NothingWhileMoving()
    {
        var h = Mouth();
        for (int i = 0; i < 20; i++)
            Assert.Equal(HandOverState.None, Near(h, usable: false, still: false));
    }

    [Fact]
    public void JustShortOfTheMouth_NothingWithTheHandElsewhere()
    {
        var h = Mouth();
        for (int i = 0; i < 20; i++)
            Assert.Equal(HandOverState.None, Near(h, usable: false, placeGap: 0.63f));
    }

    [Fact]
    public void JustShortOfTheMouth_AMoveStartsTheDwellOver()
    {
        var h = Mouth();
        Assert.Equal(HandOverState.None, Near(h, dt: 0.2f));
        Assert.Equal(HandOverState.None, Near(h, dt: 0.01f, still: false));
        Assert.Equal(HandOverState.None, Near(h, dt: 0.2f));
        Assert.Equal(HandOverState.Hint, Near(h, dt: 0.2f));
    }

    [Fact]
    public void JustShortOfTheMouth_AWobblePastTheBandIsTheSameVisit()
    {
        var h = Mouth();
        Near(h, usable: false, dt: 0.1f);
        Assert.Equal(HandOverState.Refuse, Near(h, usable: false, dt: 0.3f));
        Assert.Equal(HandOverState.None, Near(h, usable: false, nearGap: 0.5f));
        Assert.Equal(HandOverState.Refused, Near(h, usable: false, dt: 0.4f));
        Assert.Equal(HandOverState.Refused, Near(h, usable: false, dt: 0.4f));
    }

    [Fact]
    public void JustShortOfTheMouth_ClearlyPastTheBandIsANewVisit()
    {
        var h = Mouth();
        Near(h, usable: false, dt: 0.1f);
        Assert.Equal(HandOverState.Refuse, Near(h, usable: false, dt: 0.3f));
        Assert.Equal(HandOverState.None, Near(h, usable: false, nearGap: 0.51f));
        Near(h, usable: false, dt: 0.1f);
        Assert.Equal(HandOverState.Refuse, Near(h, usable: false, dt: 0.3f));
    }

    [Fact]
    public void JustShortOfTheMouth_ARefusalWaitsForTheLook()
    {
        var h = Mouth();
        for (int i = 0; i < 20; i++)
            Assert.Equal(HandOverState.None, Near(h, usable: false, looking: false));
        Assert.Equal(HandOverState.Refuse, Near(h, usable: false));
    }

    [Fact]
    public void JustShortOfTheMouth_TheRingNeedsNoLook()
    {
        var h = Mouth();
        Near(h, dt: 0.1f, looking: false);
        Assert.Equal(HandOverState.Hint, Near(h, dt: 0.3f, looking: false));
    }

    [Fact]
    public void AtTheMouth_ARefusalNeedsNoLook()
    {
        var h = Mouth();
        h.Update(true, false, false, false, 0.1f, true, 0.1f, looking: false);
        Assert.Equal(
            HandOverState.Refuse,
            h.Update(true, false, false, false, 0.3f, true, 0.1f, looking: false)
        );
    }

    [Fact]
    public void JustShortOfTheMouth_TheTriggerStaysTheItemsOwn()
    {
        // HeldGestures.MouthHint and UseDenial speak for a press here (HandOver.Context).
        var h = Mouth();
        for (int i = 0; i < 20; i++)
        {
            Assert.Equal(HandOverState.None, Near(h, trigger: true));
            Assert.False(h.SuppressPrimary);
        }
    }

    [Fact]
    public void JustShortOfTheMouth_AnItemWithItsOwnUseIsNoHandOver()
    {
        var h = Mouth();
        for (int i = 0; i < 20; i++)
            Assert.Equal(HandOverState.None, Near(h, usable: false, ownUse: true));
    }

    [Fact]
    public void ARefusalAtTheMouthIsNotRepeatedBesideIt()
    {
        var h = Mouth();
        h.Update(true, false, false, false, 0.1f);
        Assert.Equal(HandOverState.Refuse, h.Update(true, false, false, false, 0.3f));
        for (int i = 0; i < 20; i++)
            Assert.NotEqual(HandOverState.Refuse, Near(h, usable: false));
        Assert.Equal(HandOverState.Refused, Near(h, usable: false));
    }

    [Fact]
    public void ARefusedItemKeptAgainstThemStaysRefused_NeverSilent()
    {
        // #112, modded B: the near band's CantUse fired, then the host's body kept the Compass 0.22 m off with its
        // controller's place elsewhere, and nothing more came for 8 s.
        var h = Mouth();
        Near(h, usable: false, dt: 0.1f);
        Assert.Equal(HandOverState.Refuse, Near(h, usable: false, dt: 0.3f));
        for (int i = 0; i < 20; i++)
            Assert.Equal(
                HandOverState.Refused,
                h.Update(true, false, false, false, 0.1f, true, placeGap: 0.63f, nearGap: 0.22f)
            );
    }

    [Fact]
    public void ARefusedItemBroughtToTheMouthIsRefusedThereToo()
    {
        var h = Mouth();
        Near(h, usable: false, dt: 0.1f);
        Assert.Equal(HandOverState.Refuse, Near(h, usable: false, dt: 0.3f));
        Assert.Equal(
            HandOverState.Refused,
            h.Update(true, false, false, false, 0.1f, true, placeGap: 0.1f, nearGap: 0.1f)
        );
        Assert.Equal(
            HandOverState.Refuse,
            h.Update(true, false, false, false, 0.3f, true, placeGap: 0.1f, nearGap: 0.1f)
        );
        Assert.Equal(
            HandOverState.Refused,
            h.Update(true, false, false, false, 0.1f, true, placeGap: 0.1f, nearGap: 0.1f)
        );
    }

    [Fact]
    public void ASqueezeOnARefusedItemAtTheMouthRefusesAgain_EachPress()
    {
        var h = Mouth();
        h.Update(true, false, false, false, 0.1f, true, placeGap: 0.1f);
        Assert.Equal(
            HandOverState.Refuse,
            h.Update(true, false, false, false, 0.3f, true, placeGap: 0.1f)
        );
        Assert.Equal(
            HandOverState.Refused,
            h.Update(true, false, false, false, 0.1f, true, placeGap: 0.1f)
        );
        Assert.Equal(
            HandOverState.Refuse,
            h.Update(true, true, false, false, 0.1f, true, placeGap: 0.1f)
        );
        h.Update(true, false, false, false, 0.1f, true, placeGap: 0.1f);
        Assert.Equal(
            HandOverState.Refuse,
            h.Update(true, true, false, false, 0.1f, true, placeGap: 0.1f)
        );
    }

    [Fact]
    public void ALastingRefusalNeedsNoLook_ButTheFirstDoes()
    {
        var h = Mouth();
        Near(h, usable: false, dt: 0.4f, looking: false);
        Assert.Equal(HandOverState.None, Near(h, usable: false, looking: false));
        Assert.Equal(HandOverState.Refuse, Near(h, usable: false));
        Assert.Equal(HandOverState.Refused, Near(h, usable: false, looking: false));
    }

    [Theory]
    [InlineData(true, 0.2f, true)]
    [InlineData(true, 0.22f, true)]
    [InlineData(true, 0.23f, false)]
    [InlineData(false, 0.2f, false)]
    public void AFriendsBodyHoldingTheItemOffCountsWithTheControllerAtTheSpot(
        bool kept,
        float placeGap,
        bool counts
    ) =>
        // #112: a Frisbee on the host's head, 0.24 m off against the 0.22 m zone, with the controller on the mouth.
        Assert.Equal(counts, HandOver.HeldOffAtSpot(kept, placeGap));

    [Theory]
    [InlineData(0.22f, true)]
    [InlineData(0.23f, false)]
    public void TheDwellCountsOutToThePlaceRadius(float placeGap, bool applies)
    {
        var h = Mouth();
        h.Update(true, false, true, false, 0.2f, true, placeGap);
        Assert.Equal(
            applies ? HandOverState.Apply : HandOverState.Hint,
            h.Update(true, false, true, false, 0.2f, true, placeGap)
        );
    }

    [Theory]
    [InlineData(0.45f, HandOverState.Hint)]
    [InlineData(0.46f, HandOverState.None)]
    public void JustShortOfTheMouth_CountsOutToTheNearPlaceRadius(
        float placeGap,
        HandOverState state
    )
    {
        var h = Mouth();
        Near(h, dt: 0.2f, placeGap: placeGap);
        Assert.Equal(state, Near(h, dt: 0.2f, placeGap: placeGap));
    }

    [Fact]
    public void ABagKeptOnAFriendWithTheControllerAwayIsNotAtTheMouth()
    {
        // #119, its worst case: 0.36 m from the grip to the far corner, pointing at the mouth, with the controller
        // 0.63 m off. The friend's body keeps the box on their mouth; at the held place it is 0.27 m short.
        var place = new Vector3(0.63f, 0f, 0f);
        var kept = new Vector3(0.36f, 0f, 0f);
        float gap = FeedZone.PlaceGap(
            new Vector3(0.18f, 0f, 0f),
            new Vector3(0.18f, 0.05f, 0.05f),
            kept,
            place,
            Vector3.Zero
        );
        Assert.Equal(0.27f, gap, 3);
        var h = Mouth();
        for (int i = 0; i < 20; i++)
            Assert.NotEqual(
                HandOverState.Apply,
                h.Update(true, false, true, false, 0.1f, true, gap)
            );
    }

    [Theory]
    [InlineData("Apple Berry Red")]
    [InlineData("Berrynana Yellow")]
    public void RealFoodWithItsFarEndAtTheMouthCounts(string prefab)
    {
        // Their pivots lie at one end of the box, 0.38 and 0.51 m from the end on the mouth: the pivot is no measure.
        Assert.True(ItemDrawnBox.TryGet(prefab, out var lo, out var hi));
        var place = new Vector3(0f, -lo.Y, 0f);
        Assert.True(place.Y > 0.35f);
        float gap = FeedZone.PlaceGap(
            place + (lo + hi) / 2f,
            (hi - lo) / 2f,
            place,
            place,
            Vector3.Zero
        );
        var h = Mouth();
        h.Update(true, false, true, false, 0.2f, true, gap);
        Assert.Equal(HandOverState.Apply, h.Update(true, false, true, false, 0.2f, true, gap));
    }
}
