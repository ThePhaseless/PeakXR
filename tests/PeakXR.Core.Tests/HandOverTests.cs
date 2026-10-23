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
    public void LeavingTheZone_Disarms()
    {
        var h = new HandOver();
        h.Update(true, false, true);
        Assert.Equal(HandOverState.None, h.Update(false, false, true));
        Assert.False(h.SuppressPrimary);
        Assert.Equal(HandOverState.None, h.Update(true, true, true));
    }
}
