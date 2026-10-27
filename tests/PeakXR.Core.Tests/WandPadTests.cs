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

    [Fact]
    public void OneHanded_TopAndBottomClicksWalk()
    {
        // Accessibility.OneHanded on a wand: the one pad walks and turns, so its stick Y reaches MoveStick.
        var forward = WandPad.Map(new Vector2(0.1f, 0.8f), true, moveHand: false, oneHanded: true);
        Assert.Equal(0.8f, forward.Stick.Y);
        Assert.Equal(0f, forward.Stick.X);
        Assert.False(forward.Secondary || forward.Primary || forward.StickClick);
        Assert.Equal(0.8f, StickAxes.MoveY(forward.Stick).Y);

        var back = WandPad.Map(new Vector2(0f, -0.7f), true, moveHand: false, oneHanded: true);
        Assert.Equal(-0.7f, back.Stick.Y);
        Assert.False(back.Primary);
    }

    [Fact]
    public void OneHanded_SidesTurn_CentreClicks()
    {
        var right = WandPad.Map(new Vector2(0.8f, 0.1f), true, moveHand: false, oneHanded: true);
        Assert.Equal(new Vector2(0.8f, 0f), right.Stick);
        Assert.Equal(0f, StickAxes.MoveY(right.Stick).Y);

        // The centre is the wand's A: tap jumps, hold crouches (BindingTable.OneHandedWand).
        var centre = WandPad.Map(new Vector2(0.1f, 0f), true, moveHand: false, oneHanded: true);
        Assert.True(centre.Primary);
        Assert.False(centre.StickClick);
        Assert.Equal(Vector2.Zero, centre.Stick);
    }

    [Fact]
    public void OneHanded_TopRimSprints_BottomRimIsB()
    {
        var top = WandPad.Map(new Vector2(0f, 0.95f), true, moveHand: false, oneHanded: true);
        Assert.Equal(0.95f, top.Stick.Y);
        Assert.True(top.StickClick);

        // The wheel cannot go on a long menu press: the menu hold already recenters (Camera.RecenterHoldSeconds).
        var bottom = WandPad.Map(new Vector2(0f, -0.95f), true, moveHand: false, oneHanded: true);
        Assert.True(bottom.Secondary);
        Assert.Equal(Vector2.Zero, bottom.Stick);
    }

    const float Dt = 1f / 90f;

    static ActionMapper WandMapper() => new(BindingTable.OneHandedWand) { LongPressSeconds = 0.5f };

    static void Run(ActionMapper m, ControllerInput c, int frames)
    {
        for (int i = 0; i < frames; i++)
            m.Update(default, c, Dt);
    }

    [Fact]
    public void OneHandedWand_CentreTapJumps()
    {
        var m = WandMapper();
        Run(m, new ControllerInput { Primary = true }, 10);
        Assert.False(m.Held(VRAction.Jump));
        m.Update(default, default, Dt);
        Assert.True(m.Pressed(VRAction.Jump));
        Assert.False(m.Held(VRAction.Crouch));
    }

    [Fact]
    public void OneHandedWand_CentreHoldCrouches_WithoutJumping()
    {
        var m = WandMapper();
        Run(m, new ControllerInput { Primary = true }, 50);
        Assert.True(m.Held(VRAction.Crouch));
        m.Update(default, default, Dt);
        Assert.False(m.Pressed(VRAction.Jump));
    }

    [Fact]
    public void OneHandedWand_BottomRimPausesAndHoldsTheWheel_TopRimSprints()
    {
        var m = WandMapper();
        Run(m, new ControllerInput { Secondary = true }, 5);
        m.Update(default, default, Dt);
        Assert.True(m.Pressed(VRAction.Pause));

        Run(m, new ControllerInput { Secondary = true }, 50);
        Assert.True(m.Held(VRAction.Emote));

        var s = WandMapper();
        s.Update(default, new ControllerInput { StickClick = true }, Dt);
        Assert.True(s.Pressed(VRAction.Sprint));
        Assert.False(s.Pressed(VRAction.Jump));
    }

    [Fact]
    public void OneHandedWand_TableKeepsEveryRowOnTheOneController()
    {
        foreach (var b in BindingTable.OneHandedWand)
            Assert.True(b.Control == VRControl.Menu || b.Hand == BindingHand.Dominant);
    }

    [Fact]
    public void OneHanded_RestingThumbStillDoesNothing()
    {
        var p = WandPad.Map(new Vector2(0f, 0.9f), false, moveHand: false, oneHanded: true);
        Assert.Equal(Vector2.Zero, p.Stick);
    }
}
