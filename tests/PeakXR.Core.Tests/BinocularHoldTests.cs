namespace PeakXR.Core.Tests;

public class BinocularHoldTests
{
    [Fact]
    public void Toggle_NeverLowers()
    {
        Assert.False(
            BinocularHold.ShouldLower(
                hold: false,
                raised: true,
                heldLocally: true,
                usePressed: false
            )
        );
    }

    [Fact]
    public void Hold_RaisedAndPressed_StaysUp()
    {
        Assert.False(
            BinocularHold.ShouldLower(hold: true, raised: true, heldLocally: true, usePressed: true)
        );
    }

    [Fact]
    public void Hold_RaisedAndReleased_Lowers()
    {
        Assert.True(
            BinocularHold.ShouldLower(
                hold: true,
                raised: true,
                heldLocally: true,
                usePressed: false
            )
        );
    }

    [Fact]
    public void Hold_AlreadyLowered_DoesNothing()
    {
        // A second toggle would raise them again.
        Assert.False(
            BinocularHold.ShouldLower(
                hold: true,
                raised: false,
                heldLocally: true,
                usePressed: false
            )
        );
    }

    [Fact]
    public void Hold_NotInLocalHand_WaitsUntilBackInHand()
    {
        // Not in the local hand (passed out, carried off): wait; lowering resumes once they are back in hand.
        Assert.False(
            BinocularHold.ShouldLower(
                hold: true,
                raised: true,
                heldLocally: false,
                usePressed: false
            )
        );
    }
}
