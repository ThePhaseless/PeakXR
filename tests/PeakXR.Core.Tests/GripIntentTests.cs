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
}
