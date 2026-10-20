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
    public void HoldingItem_OtherHandNearDifferentItem_DoesNotPickItUp()
    {
        var c = Empty();
        c.HoldingItem = true;
        c.Candidate = GrabCandidate.Item;
        Assert.Equal(GripPressAction.Interact, GripIntent.OnPress(c));
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
}
