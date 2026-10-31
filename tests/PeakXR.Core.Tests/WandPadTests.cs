namespace PeakXR.Core.Tests;

public class WandPadTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RestingThumb_DoesNothing(bool moveHand)
    {
        // Touched anywhere, not clicked: no walk, turn, flick or button (review #22).
        foreach (
            var at in new[]
            {
                new Vector2(0f, 0.9f),
                new Vector2(0.8f, 0f),
                new Vector2(0f, -0.9f),
                Vector2.Zero,
            }
        )
        {
            var p = WandPad.Map(at, false, moveHand);
            Assert.Equal(Vector2.Zero, p.Stick);
            Assert.False(p.Primary || p.Secondary || p.StickClick);
        }
    }

    [Fact]
    public void MoveHand_ClickWalksTowardTheThumb()
    {
        var p = WandPad.Map(new Vector2(0.3f, 0.7f), true, true);
        Assert.Equal(new Vector2(0.3f, 0.7f), p.Stick);
        // Its clicks walk: the top is forward, not a button.
        Assert.False(p.Primary || p.Secondary || p.StickClick);
    }

    [Fact]
    public void MoveHand_ClickAtTheRimSprints()
    {
        Assert.True(WandPad.Map(new Vector2(0f, 0.95f), true, true).StickClick);
        Assert.False(WandPad.Map(new Vector2(0f, 0.6f), true, true).StickClick);
    }

    [Fact]
    public void MoveHand_CentreClickIsBY_NotAWalk()
    {
        // The wand's stand-in for the off hand's B/Y: tap pings, hold opens the emote wheel.
        var p = WandPad.Map(new Vector2(0.1f, -0.1f), true, true);
        Assert.True(p.Secondary);
        Assert.Equal(Vector2.Zero, p.Stick);
        Assert.False(p.Primary || p.StickClick);
        Assert.False(WandPad.Map(new Vector2(0f, 0.5f), true, true).Secondary);
    }

    [Fact]
    public void TurnHand_SideClicksTurn_TopAndBottomAreButtons_CentreIsTheStickClick()
    {
        var right = WandPad.Map(new Vector2(0.8f, 0.1f), true, false);
        Assert.Equal(new Vector2(0.8f, 0f), right.Stick);
        Assert.False(right.Primary || right.Secondary || right.StickClick);

        var top = WandPad.Map(new Vector2(0.1f, 0.8f), true, false);
        Assert.True(top.Secondary);
        // A button click is never also a flick (HotbarNext) or a turn.
        Assert.Equal(Vector2.Zero, top.Stick);

        var bottom = WandPad.Map(new Vector2(-0.1f, -0.8f), true, false);
        Assert.True(bottom.Primary);
        Assert.Equal(Vector2.Zero, bottom.Stick);

        var centre = WandPad.Map(new Vector2(0.1f, 0.1f), true, false);
        Assert.True(centre.StickClick);
        Assert.Equal(Vector2.Zero, centre.Stick);
    }
}

public class WandPadLatchTests
{
    [Fact]
    public void CentreClickSlidingToTheRim_EndsBYWithoutATap_ThenSprints()
    {
        var l = new WandPadLatch();
        Assert.True(l.Map(Vector2.Zero, true, moveHand: true).Secondary);
        Assert.False(l.Cancelled);
        var rim = l.Map(new Vector2(0f, 0.95f), true, moveHand: true);
        Assert.True(l.Cancelled);
        Assert.False(rim.Secondary);
        Assert.True(rim.StickClick);
        // Back across the centre: still no B/Y for this click.
        Assert.False(l.Map(Vector2.Zero, true, moveHand: true).Secondary);
        Assert.False(l.Cancelled);
        l.Map(Vector2.Zero, false, moveHand: true);
        Assert.True(l.Map(Vector2.Zero, true, moveHand: true).Secondary);
    }

    [Fact]
    public void RimClickCrossingTheCentre_NeverBecomesBY()
    {
        var l = new WandPadLatch();
        Assert.True(l.Map(new Vector2(0.9f, 0f), true, moveHand: true).StickClick);
        var mid = l.Map(new Vector2(0.1f, 0f), true, moveHand: true);
        Assert.False(mid.Secondary);
        Assert.False(l.Cancelled);
    }

    [Fact]
    public void TurnHand_MapsAsBefore()
    {
        var l = new WandPadLatch();
        Assert.True(l.Map(Vector2.Zero, true, moveHand: false).StickClick);
        Assert.True(l.Map(new Vector2(0f, 0.9f), true, moveHand: false).Secondary);
        Assert.False(l.Cancelled);
    }
}
