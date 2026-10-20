namespace PeakXR.Core.Tests;

public class GripPriorityTests
{
    [Fact]
    public void WhileClimbing_WallBeatsLooseItem()
    {
        var c = new GripContext
        {
            PhysicalGrab = true,
            PhysicalClimb = true,
            Candidate = GrabCandidate.Item,
            NearClimbable = true,
            BodyClimbing = true,
        };
        Assert.Equal(GripPressAction.Climb, GripIntent.OnPress(c));
    }

    [Fact]
    public void WhileClimbing_RopeOrHandleStillBeatsWall()
    {
        var c = new GripContext
        {
            PhysicalGrab = true,
            PhysicalClimb = true,
            Candidate = GrabCandidate.Interactible,
            NearClimbable = true,
            BodyClimbing = true,
        };
        Assert.Equal(GripPressAction.GrabInteractible, GripIntent.OnPress(c));
    }

    [Fact]
    public void OnGround_ItemStillBeatsWall()
    {
        var c = new GripContext
        {
            PhysicalGrab = true,
            PhysicalClimb = true,
            Candidate = GrabCandidate.Item,
            NearClimbable = true,
        };
        Assert.Equal(GripPressAction.GrabItem, GripIntent.OnPress(c));
    }
}
