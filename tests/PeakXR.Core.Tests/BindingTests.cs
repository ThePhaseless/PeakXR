namespace PeakXR.Core.Tests;

public class BindingTests
{
    const float Dt = 1f / 90f;

    static ControllerInput Pressed(VRControl control)
    {
        var c = new ControllerInput();
        switch (control)
        {
            case VRControl.Trigger:
                c.Trigger = 1f;
                break;
            case VRControl.Grip:
                c.Grip = 1f;
                break;
            case VRControl.Primary:
                c.Primary = true;
                break;
            case VRControl.Secondary:
                c.Secondary = true;
                break;
            case VRControl.StickClick:
                c.StickClick = true;
                break;
            case VRControl.Menu:
                c.Menu = true;
                break;
        }
        return c;
    }

    static ControllerInput Idle => default;

    [Theory]
    [InlineData(BindingHand.Left, false, false, true)]
    [InlineData(BindingHand.Right, true, true, false)]
    [InlineData(BindingHand.Dominant, false, true, false)]
    [InlineData(BindingHand.Dominant, true, false, true)]
    [InlineData(BindingHand.OffHand, false, false, true)]
    [InlineData(BindingHand.ItemHand, false, true, true)]
    [InlineData(BindingHand.OtherHand, false, true, false)]
    public void ResolveLeft_FollowsRoleSetting(
        BindingHand role,
        bool dominantLeft,
        bool itemInLeft,
        bool expectedLeft
    )
    {
        Assert.Equal(expectedLeft, BindingTable.ResolveLeft(role, dominantLeft, itemInLeft));
    }

    [Fact]
    public void Hold_GivesOneFramePressAndReleaseEdges()
    {
        var m = new ActionMapper();
        m.Update(Idle, Pressed(VRControl.Primary), Dt);
        Assert.True(m.Pressed(VRAction.Jump));
        Assert.True(m.Held(VRAction.Jump));

        m.Update(Idle, Pressed(VRControl.Primary), Dt);
        Assert.False(m.Pressed(VRAction.Jump));
        Assert.True(m.Held(VRAction.Jump));

        m.Update(Idle, Idle, Dt);
        Assert.True(m.Released(VRAction.Jump));
        Assert.False(m.Held(VRAction.Jump));

        m.Update(Idle, Idle, Dt);
        Assert.False(m.Released(VRAction.Jump));
    }

    [Fact]
    public void UseButtons_FollowTheHandHoldingTheItem()
    {
        var m = new ActionMapper { ItemInLeft = true };
        m.Update(Pressed(VRControl.Trigger), Idle, Dt);
        Assert.True(m.Pressed(VRAction.UsePrimary));
        Assert.False(m.Held(VRAction.UseSecondary));

        var r = new ActionMapper { ItemInLeft = false };
        r.Update(Pressed(VRControl.Trigger), Idle, Dt);
        Assert.True(r.Pressed(VRAction.UseSecondary));
        Assert.False(r.Held(VRAction.UsePrimary));
    }

    [Fact]
    public void LeftHanded_MirrorsDominantBindings()
    {
        var m = new ActionMapper { DominantLeft = true };
        m.Update(Pressed(VRControl.Primary), Idle, Dt);
        Assert.True(m.Pressed(VRAction.Jump));
        Assert.False(m.Held(VRAction.PutAway));
    }

    [Fact]
    public void Tap_FiresOnShortReleaseOnly()
    {
        var m = new ActionMapper();
        m.Update(Pressed(VRControl.Secondary), Idle, Dt);
        Assert.False(m.Held(VRAction.Ping));
        m.Update(Idle, Idle, Dt);
        Assert.True(m.Pressed(VRAction.Ping));
        m.Update(Idle, Idle, Dt);
        Assert.True(m.Released(VRAction.Ping));
        Assert.False(m.Held(VRAction.Emote));
    }

    [Fact]
    public void LongPress_HoldsAfterThresholdAndSuppressesTap()
    {
        var m = new ActionMapper { LongPressSeconds = 0.5f };
        for (int i = 0; i < 40; i++)
        {
            m.Update(Pressed(VRControl.Secondary), Idle, Dt);
            Assert.False(m.Held(VRAction.Emote));
        }
        for (int i = 0; i < 10; i++)
            m.Update(Pressed(VRControl.Secondary), Idle, Dt);
        Assert.True(m.Held(VRAction.Emote));

        m.Update(Idle, Idle, Dt);
        Assert.True(m.Released(VRAction.Emote));
        Assert.False(m.Held(VRAction.Ping));
        m.Update(Idle, Idle, Dt);
        Assert.False(m.Held(VRAction.Ping));
    }

    [Fact]
    public void StickFlick_PressesOncePerFlickWithHysteresis()
    {
        var m = new ActionMapper();
        m.Update(Idle, new ControllerInput { Stick = new Vector2(0f, 0.9f) }, Dt);
        Assert.True(m.Pressed(VRAction.HotbarNext));
        m.Update(Idle, new ControllerInput { Stick = new Vector2(0f, 0.5f) }, Dt);
        Assert.True(m.Held(VRAction.HotbarNext));
        m.Update(Idle, new ControllerInput { Stick = new Vector2(0f, 0.9f) }, Dt);
        Assert.False(m.Pressed(VRAction.HotbarNext));
        m.Update(Idle, new ControllerInput { Stick = new Vector2(0f, 0.1f) }, Dt);
        Assert.True(m.Released(VRAction.HotbarNext));
        m.Update(Idle, new ControllerInput { Stick = new Vector2(0f, -0.9f) }, Dt);
        Assert.True(m.Pressed(VRAction.HotbarPrevious));
    }

    [Fact]
    public void StickFlick_IgnoresMostlySidewaysPush()
    {
        var m = new ActionMapper();
        m.Update(Idle, new ControllerInput { Stick = new Vector2(0.95f, 0.75f) }, Dt);
        Assert.False(m.Held(VRAction.HotbarNext));
    }

    [Fact]
    public void DisabledAction_ReleasesAndStaysOff()
    {
        var m = new ActionMapper();
        m.Update(Idle, Pressed(VRControl.Grip), Dt);
        Assert.True(m.Held(VRAction.Interact));
        m.SetDisabled(VRAction.Interact, true);
        m.Update(Idle, Pressed(VRControl.Grip), Dt);
        Assert.True(m.Released(VRAction.Interact));
        m.Update(Idle, Pressed(VRControl.Grip), Dt);
        Assert.False(m.Held(VRAction.Interact));
    }

    [Fact]
    public void ReservedControl_SkipsOnlyThatHandsBinding()
    {
        var m = new ActionMapper();
        m.SetReserved(left: false, VRControl.Grip, true);
        m.Update(Idle, Pressed(VRControl.Grip), Dt);
        Assert.False(m.Held(VRAction.Interact));
        m.Update(Pressed(VRControl.Grip), Idle, Dt);
        Assert.True(m.Held(VRAction.Interact));
    }

    [Fact]
    public void Trigger_UsesAnalogHysteresis()
    {
        var m = new ActionMapper();
        m.Update(Idle, new ControllerInput { Trigger = 0.8f }, Dt);
        Assert.True(m.Held(VRAction.UsePrimary));
        m.Update(Idle, new ControllerInput { Trigger = 0.7f }, Dt);
        Assert.True(m.Held(VRAction.UsePrimary));
        m.Update(Idle, new ControllerInput { Trigger = 0.5f }, Dt);
        Assert.False(m.Held(VRAction.UsePrimary));
    }

    [Fact]
    public void Reset_ClearsEverything()
    {
        var m = new ActionMapper();
        m.Update(Idle, Pressed(VRControl.Primary), Dt);
        m.Reset();
        Assert.False(m.Held(VRAction.Jump));
        m.Update(Idle, Pressed(VRControl.Primary), Dt);
        Assert.True(m.Pressed(VRAction.Jump));
    }

    [Fact]
    public void PushToTalk_NoLongerOnTheMoveStickClick()
    {
        // D4: holding the sprint click to talk also toggled sprint. Hand-to-mouth is the default way to talk.
        Assert.DoesNotContain(BindingTable.Default, b => b.Action == VRAction.PushToTalk);
        var m = new ActionMapper { LongPressSeconds = 0.5f };
        for (int i = 0; i < 60; i++)
            m.Update(Pressed(VRControl.StickClick), Idle, Dt);
        Assert.False(m.Held(VRAction.PushToTalk));
        Assert.True(m.Held(VRAction.Sprint));
    }

    [Theory]
    [InlineData("OffHandStickClick", false, VRAction.Sprint)]
    [InlineData("DominantStickClick", false, VRAction.Crouch)]
    [InlineData("RightPrimary", false, VRAction.Jump)]
    [InlineData("LeftSecondary", false, VRAction.Ping)]
    [InlineData("RightStickClick", true, VRAction.Sprint)]
    public void Conflict_NamesTheActionAlreadyOnThatButton(
        string button,
        bool dominantLeft,
        VRAction expected
    )
    {
        Assert.True(ButtonName.TryParse(button, dominantLeft, out bool left, out var control));
        Assert.True(
            BindingTable.Conflict(BindingTable.Default, left, control, dominantLeft, out var action)
        );
        Assert.Equal(expected, action);
    }

    [Fact]
    public void Conflict_EveryNamedButtonIsTaken_SoPushToTalkHasNoFreeDefault()
    {
        foreach (var name in ButtonName.All)
        {
            if (!ButtonName.TryParse(name, false, out bool left, out var control))
                continue;
            Assert.True(
                BindingTable.Conflict(BindingTable.Default, left, control, false, out _),
                name
            );
        }
    }

    [Fact]
    public void Conflict_FalseOnAnEmptyTable()
    {
        Assert.False(
            BindingTable.Conflict(Array.Empty<Binding>(), true, VRControl.Primary, false, out _)
        );
    }

    [Fact]
    public void DefaultTable_EveryActionHasABinding()
    {
        // Push-to-talk is bound only by Voice.PushToTalkButton (D4).
        foreach (var action in Enum.GetValues<VRAction>())
            if (action != VRAction.PushToTalk)
                Assert.Contains(BindingTable.Default, b => b.Action == action);
    }

    [Fact]
    public void TurnAxis_OnlyWhenSidewaysDominates()
    {
        Assert.Equal(0.8f, StickAxes.TurnX(new Vector2(0.8f, 0.3f)));
        Assert.Equal(0f, StickAxes.TurnX(new Vector2(0.5f, 0.8f)));
    }

    static void HoldMenu(ActionMapper m, float seconds)
    {
        int frames = (int)MathF.Round(seconds / Dt);
        for (int i = 0; i < frames; i++)
            m.Update(Pressed(VRControl.Menu), Idle, Dt);
        m.Update(Idle, Idle, Dt);
    }

    [Theory]
    [InlineData(0.1f, true)]
    [InlineData(0.7f, true)]
    [InlineData(0.95f, true)]
    [InlineData(1.2f, false)]
    public void Pause_TapsUpToTheMenuHoldTime(float seconds, bool pauses)
    {
        var m = new ActionMapper { LongPressSeconds = 0.5f, MenuHoldSeconds = 1f };
        HoldMenu(m, seconds);
        Assert.Equal(pauses, m.Pressed(VRAction.Pause));
    }

    [Fact]
    public void MenuHoldTime_LeavesOtherTapsAlone()
    {
        var m = new ActionMapper { LongPressSeconds = 0.5f, MenuHoldSeconds = 1f };
        for (int i = 0; i < 63; i++)
            m.Update(Pressed(VRControl.Secondary), Idle, Dt);
        m.Update(Idle, Idle, Dt);
        Assert.False(m.Pressed(VRAction.Ping));
    }

    [Theory]
    [InlineData("RightSecondary", false, false, VRControl.Secondary)]
    [InlineData("RightSecondary", true, false, VRControl.Secondary)]
    [InlineData("LeftPrimary", false, true, VRControl.Primary)]
    [InlineData("DominantSecondary", false, false, VRControl.Secondary)]
    [InlineData("DominantSecondary", true, true, VRControl.Secondary)]
    [InlineData("OffHandPrimary", false, true, VRControl.Primary)]
    [InlineData("OffHandPrimary", true, false, VRControl.Primary)]
    [InlineData("DominantStickClick", true, true, VRControl.StickClick)]
    public void ButtonName_ResolvesHandRoles(
        string name,
        bool dominantLeft,
        bool expectedLeft,
        VRControl expected
    )
    {
        Assert.True(ButtonName.TryParse(name, dominantLeft, out bool left, out var control));
        Assert.Equal(expectedLeft, left);
        Assert.Equal(expected, control);
    }

    [Theory]
    [InlineData("None")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("LeftTrigger")]
    public void ButtonName_RejectsNoneAndUnknown(string name) =>
        Assert.False(ButtonName.TryParse(name, false, out _, out _));

    [Theory]
    [InlineData(false, 0.9f, VRAction.SpectateNext)]
    [InlineData(false, -0.9f, VRAction.SpectatePrevious)]
    [InlineData(true, 0.9f, VRAction.SpectateNext)]
    public void Spectate_FlicksTheDominantStick(bool dominantLeft, float y, VRAction expected)
    {
        var m = new ActionMapper { DominantLeft = dominantLeft };
        m.SetDisabled(VRAction.HotbarNext, true);
        m.SetDisabled(VRAction.HotbarPrevious, true);
        var flick = new ControllerInput { Stick = new Vector2(0f, y) };
        if (dominantLeft)
            m.Update(flick, Idle, Dt);
        else
            m.Update(Idle, flick, Dt);
        Assert.True(m.Pressed(expected));
        Assert.False(m.Pressed(VRAction.HotbarNext));
    }
}
