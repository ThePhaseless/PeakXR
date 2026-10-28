namespace PeakXR.Core.Tests;

public class HandProgressTests
{
    [Theory]
    [InlineData(ProgressSource.Item, true, true, -1, true)]
    [InlineData(ProgressSource.Item, true, false, 0, false)]
    [InlineData(ProgressSource.ClimbingSpike, true, true, 1, true)]
    [InlineData(ProgressSource.Interactable, false, false, 0, true)]
    [InlineData(ProgressSource.Interactable, true, true, 1, false)]
    [InlineData(ProgressSource.Interactable, true, false, -1, true)]
    [InlineData(ProgressSource.Interactable, true, true, -1, false)]
    [InlineData(ProgressSource.Interactable, false, false, -1, false)]
    public void PickLeft_FollowsTheActingHand(
        ProgressSource source,
        bool hasItem,
        bool itemInLeft,
        int aimHand,
        bool expectedLeft
    ) => Assert.Equal(expectedLeft, ProgressHand.PickLeft(source, hasItem, itemInLeft, aimHand));

    [Theory]
    // A plain grip hold (no ray) with the left hand: the ring goes to the left hand, not the right.
    [InlineData(false, false, 0, false, true)]
    [InlineData(false, false, 1, true, false)]
    // The free hand gripping while the other holds an item.
    [InlineData(true, false, 0, false, true)]
    // No hand acting: the dominant hand (Input.DominantHand, or the one controller one-handed).
    [InlineData(false, false, -1, true, true)]
    [InlineData(false, false, -1, false, false)]
    public void PickLeft_InteractableHold_UsesTheGrippingHandThenTheDominant(
        bool hasItem,
        bool itemInLeft,
        int gripHand,
        bool leftHanded,
        bool expectedLeft
    ) =>
        Assert.Equal(
            expectedLeft,
            ProgressHand.PickLeft(
                ProgressSource.Interactable,
                hasItem,
                itemInLeft,
                -1,
                gripHand,
                leftHanded
            )
        );

    [Fact]
    public void Hidden_IsSilent()
    {
        var t = new ProgressPulseTracker();
        Assert.Equal(ProgressPulse.None, t.Update(false, 0f));
        Assert.Equal(ProgressPulse.None, t.Update(false, 0.7f));
    }

    [Fact]
    public void QuarterCrossings_Step()
    {
        var t = new ProgressPulseTracker();
        Assert.Equal(ProgressPulse.None, t.Update(true, 0.1f));
        Assert.Equal(ProgressPulse.Step, t.Update(true, 0.3f));
        Assert.Equal(ProgressPulse.None, t.Update(true, 0.4f));
        Assert.Equal(ProgressPulse.Step, t.Update(true, 0.55f));
    }

    [Fact]
    public void ReachingFull_CompletesOnce()
    {
        var t = new ProgressPulseTracker();
        t.Update(true, 0.9f);
        Assert.Equal(ProgressPulse.Complete, t.Update(true, 1f));
        Assert.Equal(ProgressPulse.None, t.Update(true, 1f));
        Assert.Equal(ProgressPulse.None, t.Update(false, 0f));
    }

    [Fact]
    public void HidingNearFull_Completes()
    {
        var t = new ProgressPulseTracker();
        t.Update(true, 0.5f);
        t.Update(true, 0.97f);
        Assert.Equal(ProgressPulse.Complete, t.Update(false, 0f));
    }

    [Fact]
    public void CancelledEarly_DoesNotComplete()
    {
        var t = new ProgressPulseTracker();
        t.Update(true, 0.6f);
        Assert.Equal(ProgressPulse.None, t.Update(false, 0f));
        // A new cast starts from scratch.
        Assert.Equal(ProgressPulse.None, t.Update(true, 0.1f));
        Assert.Equal(ProgressPulse.Step, t.Update(true, 0.26f));
    }

    [Fact]
    public void ProgressDrop_StartsNewCast()
    {
        var t = new ProgressPulseTracker();
        t.Update(true, 0.8f);
        Assert.Equal(ProgressPulse.None, t.Update(true, 0.05f));
        Assert.Equal(ProgressPulse.Step, t.Update(true, 0.3f));
    }
}
