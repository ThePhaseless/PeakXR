namespace PeakXR.Core.Tests;

public class PointedSecondHandTests
{
    // The free hand beside a two-hand-capable held item, within the loose second-grip reach.
    static GripContext FreeHandByTheItem() =>
        new GripContext
        {
            PhysicalGrab = true,
            PhysicalClimb = true,
            HoldingItem = true,
            HeldHandedness = Handedness.TwoHanded,
            AtSecondGrip = true,
            NearHeldItem = true,
        };

    [Fact]
    public void APointingHandAimedAtATakerInteractsInsteadOfJoiningTheItem()
    {
        var c = FreeHandByTheItem();
        c.Pointing = true;
        c.Aimed = AimedTarget.ItemTaker;
        c.Candidate = GrabCandidate.Interactible;
        Assert.Equal(GripPressAction.Interact, GripIntent.OnPress(c));
    }

    [Fact]
    public void APointingHandRightOnTheSecondGripStillJoins()
    {
        var c = FreeHandByTheItem();
        c.Pointing = true;
        c.Aimed = AimedTarget.ItemTaker;
        c.AtSecondGripTight = true;
        Assert.Equal(GripPressAction.SecondHand, GripIntent.OnPress(c));
    }

    [Fact]
    public void ATwoHandGripWithoutAPointedAimJoins()
    {
        var c = FreeHandByTheItem();
        Assert.Equal(GripPressAction.SecondHand, GripIntent.OnPress(c));
    }

    [Fact]
    public void APointedAimAtAnyInteractibleAlsoLeavesTheItemAlone()
    {
        var c = FreeHandByTheItem();
        c.NearHeldItem = false;
        c.Pointing = true;
        c.Aimed = AimedTarget.Other;
        Assert.NotEqual(GripPressAction.SecondHand, GripIntent.OnPress(c));
    }

    [Fact]
    public void AHandClosingOnTheItemBodyJoinsWhateverLiesPastIt()
    {
        var c = FreeHandByTheItem();
        c.AtSecondGrip = false;
        c.Pointing = true;
        c.Aimed = AimedTarget.Other;
        Assert.Equal(GripPressAction.SecondHand, GripIntent.OnPress(c));
    }

    [Fact]
    public void AHandOnTheItemBodyPointingAtATakerInteracts()
    {
        var c = FreeHandByTheItem();
        c.AtSecondGrip = false;
        c.Pointing = true;
        c.Aimed = AimedTarget.ItemTaker;
        Assert.Equal(GripPressAction.Interact, GripIntent.OnPress(c));
    }

    [Fact]
    public void APointedTakerGripWithSomeOtherInteractibleAtTheHandInteractsWithTheTaker()
    {
        var c = new GripContext
        {
            PhysicalGrab = true,
            Pointing = true,
            Aimed = AimedTarget.ItemTaker,
            Candidate = GrabCandidate.Interactible,
            CandidateIsAimed = false,
        };
        Assert.Equal(GripPressAction.Interact, GripIntent.OnPress(c));
    }
}
