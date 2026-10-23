namespace PeakXR.Core.Tests;

public class PointedGripNarrowTests
{
    // "Pointing" curls are what any grip with the finger off the trigger reads as.
    static GripContext OrdinaryGripAtWall() =>
        new GripContext
        {
            PhysicalGrab = true,
            PhysicalClimb = true,
            NearClimbable = true,
            HoldConfirmed = true,
            Pointing = true,
        };

    [Fact]
    public void AnOrdinaryGripAtAWallWithAnInteractibleInTheRayClimbs()
    {
        var c = OrdinaryGripAtWall();
        c.Aimed = AimedTarget.Other;
        Assert.Equal(GripPressAction.Climb, GripIntent.OnPress(c));
    }

    [Fact]
    public void TheSameGripAtTheStatueWithAnAmuletInHandInteracts()
    {
        var c = OrdinaryGripAtWall();
        c.HoldingItem = true;
        c.Aimed = AimedTarget.ItemTaker;
        Assert.Equal(GripPressAction.Interact, GripIntent.OnPress(c));
    }

    [Fact]
    public void AnItemTakerInTheRayWithoutPointingStillClimbs()
    {
        var c = OrdinaryGripAtWall();
        c.Pointing = false;
        c.Aimed = AimedTarget.ItemTaker;
        Assert.Equal(GripPressAction.Climb, GripIntent.OnPress(c));
    }
}
