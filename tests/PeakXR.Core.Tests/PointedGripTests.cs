namespace PeakXR.Core.Tests;

public class PointedGripTests
{
    static GripContext AgainstRock() =>
        new GripContext
        {
            PhysicalGrab = true,
            PhysicalClimb = true,
            HoldingItem = true,
            NearClimbable = true,
            HoldConfirmed = true,
        };

    [Fact]
    public void PointingAtAnAcceptingTargetBeatsAClimbableInReach()
    {
        var c = AgainstRock();
        c.PointedAtTaker = true;
        Assert.Equal(GripPressAction.Interact, GripIntent.OnPress(c));
    }

    [Fact]
    public void PointingAtAnAcceptingTargetBeatsABodySlotClaim()
    {
        var c = AgainstRock();
        c.ExtensionClaim = true;
        c.PointedAtTaker = true;
        Assert.Equal(GripPressAction.Interact, GripIntent.OnPress(c));
    }

    [Fact]
    public void AGripThatIsNotAPointedAimStillClimbs()
    {
        var c = AgainstRock();
        Assert.Equal(GripPressAction.Climb, GripIntent.OnPress(c));
    }

    [Fact]
    public void AFreeHandJoiningATwoHandItemStillJoins()
    {
        var c = AgainstRock();
        c.PointedAtTaker = true;
        c.NearHeldItem = true;
        c.HeldHandedness = Handedness.TwoHanded;
        Assert.Equal(GripPressAction.SecondHand, GripIntent.OnPress(c));
    }
}
