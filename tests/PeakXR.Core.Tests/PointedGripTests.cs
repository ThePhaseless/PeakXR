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
        // At the item's off-hand grip: next to rock, the grip anchor is what makes it a join (vr.51's chain fix).
        c.AtSecondGrip = true;
        c.HeldHandedness = Handedness.TwoHanded;
        Assert.Equal(GripPressAction.SecondHand, GripIntent.OnPress(c));
    }
}

public class PointedGripCandidateTests
{
    [Fact]
    public void APointedGripWithAnInteractibleAtTheHandTakesTheCandidatePath()
    {
        var c = new GripContext
        {
            PhysicalGrab = true,
            PhysicalClimb = true,
            Candidate = GrabCandidate.Interactible,
            PointedAtTaker = true,
        };
        Assert.Equal(GripPressAction.GrabInteractible, GripIntent.OnPress(c));
    }

    [Fact]
    public void APointedGripWithAnInteractibleAtTheHandBesideRockStillTakesIt()
    {
        var c = new GripContext
        {
            PhysicalGrab = true,
            PhysicalClimb = true,
            NearClimbable = true,
            HoldConfirmed = true,
            Candidate = GrabCandidate.Interactible,
            PointedAtTaker = true,
        };
        Assert.Equal(GripPressAction.GrabInteractible, GripIntent.OnPress(c));
    }

    [Fact]
    public void APointedGripWithALooseItemAtAnEmptyHandPicksItUp()
    {
        var c = new GripContext
        {
            PhysicalGrab = true,
            Candidate = GrabCandidate.Item,
            PointedAtTaker = true,
        };
        Assert.Equal(GripPressAction.GrabItem, GripIntent.OnPress(c));
    }
}
