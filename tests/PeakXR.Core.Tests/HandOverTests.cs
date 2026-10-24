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
}
