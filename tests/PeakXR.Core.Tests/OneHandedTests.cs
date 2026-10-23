using System.Numerics;

namespace PeakXR.Core.Tests;

public class OneHandedTests
{
    const float Dt = 1f / 90f;

    static ControllerInput Idle => default;

    [Fact]
    public void Table_PutsEveryRowOnTheOneController()
    {
        foreach (var b in BindingTable.OneHanded)
        {
            // Menu rows name both controllers so whichever has a menu button pauses; the other is blank.
            if (b.Control == VRControl.Menu)
                continue;
            Assert.True(
                b.Hand == BindingHand.Dominant,
                $"{b.Action} sits on {b.Hand}, which can be the missing controller"
            );
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JumpSprintAndUse_AreOnTheOneController(bool left)
    {
        var m = new ActionMapper(BindingTable.OneHanded) { DominantLeft = left, ItemInLeft = left };
        var c = new ControllerInput
        {
            Primary = true,
            StickClick = true,
            Trigger = 1f,
            Grip = 1f,
        };
        if (left)
            m.Update(c, Idle, Dt);
        else
            m.Update(Idle, c, Dt);
        Assert.True(m.Pressed(VRAction.Jump));
        Assert.True(m.Pressed(VRAction.Sprint));
        Assert.True(m.Pressed(VRAction.UsePrimary));
        Assert.True(m.Pressed(VRAction.Interact));
    }

    [Fact]
    public void StickClickHeld_CrouchesAfterTheLongPress()
    {
        var m = new ActionMapper(BindingTable.OneHanded) { LongPressSeconds = 0.5f };
        var click = new ControllerInput { StickClick = true };
        m.Update(Idle, click, Dt);
        Assert.False(m.Held(VRAction.Crouch));
        for (int i = 0; i < 50; i++)
            m.Update(Idle, click, Dt);
        Assert.True(m.Held(VRAction.Crouch));
        Assert.True(m.Held(VRAction.Sprint));
    }

    [Fact]
    public void SecondaryTap_Pauses_AndLongPress_OpensTheEmoteWheel()
    {
        var m = new ActionMapper(BindingTable.OneHanded) { LongPressSeconds = 0.5f };
        var b = new ControllerInput { Secondary = true };
        m.Update(Idle, b, Dt);
        m.Update(Idle, Idle, Dt);
        Assert.True(m.Pressed(VRAction.Pause));
        Assert.False(m.Held(VRAction.Emote));

        for (int i = 0; i < 50; i++)
            m.Update(Idle, b, Dt);
        Assert.True(m.Held(VRAction.Emote));
        m.Update(Idle, Idle, Dt);
        Assert.False(m.Pressed(VRAction.Pause));
    }

    [Fact]
    public void StickForward_NeverCyclesTheHotbar()
    {
        var m = new ActionMapper(BindingTable.OneHanded);
        var up = new ControllerInput { Stick = new Vector2(0f, 1f) };
        m.Update(Idle, up, Dt);
        Assert.False(m.Pressed(VRAction.HotbarNext));
        Assert.False(m.Pressed(VRAction.ScrollForward));
        // Dead or passed out the stick does not walk, so its flick still picks the teammate to watch.
        Assert.True(m.Pressed(VRAction.SpectateNext));
    }

    [Fact]
    public void Mapper_TakesANewTable()
    {
        var m = new ActionMapper { Bindings = BindingTable.OneHanded, LongPressSeconds = 0.5f };
        m.Update(Idle, new ControllerInput { Secondary = true }, Dt);
        m.Update(Idle, Idle, Dt);
        // B on the right hand is drop and ping in the two-hand table; one-handed it pauses.
        Assert.True(m.Pressed(VRAction.Pause));
    }

    [Theory]
    [InlineData(0.2f, 0.9f, 0.9f)]
    [InlineData(-0.3f, -0.8f, -0.8f)]
    [InlineData(0.9f, 0.2f, 0f)]
    [InlineData(0.5f, 0.5f, 0f)]
    public void MoveY_IsTheForwardBackPartTheTurnDoesNotTake(float x, float y, float forward)
    {
        var move = StickAxes.MoveY(new Vector2(x, y));
        Assert.Equal(0f, move.X);
        Assert.Equal(forward, move.Y);
        // The two never both act on one push.
        Assert.True(move.Y == 0f || StickAxes.TurnX(new Vector2(x, y)) == 0f);
    }

    [Theory]
    [InlineData(false, true, true, 0f, true)]
    [InlineData(false, true, false, 0f, false)]
    [InlineData(true, true, false, 0f, true)]
    [InlineData(true, false, true, 0.1f, true)]
    [InlineData(true, true, false, 1.2f, false)]
    [InlineData(true, false, false, 0f, false)]
    public void CheerPose_OneHandedTakesOneStillRaisedHand(
        bool oneHanded,
        bool leftUp,
        bool rightUp,
        float speed,
        bool cheer
    ) => Assert.Equal(cheer, EmoteGestures.CheerPose(oneHanded, leftUp, rightUp, speed));

    [Fact]
    public void ControllerLabels_ReadTheOneHandedTable()
    {
        var t = BindingTable.OneHanded;
        Assert.False(ControllerLabels.ForAction(t, VRAction.Ping, false, false, out _, out _));
        Assert.True(
            ControllerLabels.ForAction(t, VRAction.Emote, false, false, out bool left, out var c)
        );
        Assert.False(left);
        Assert.Equal(VRControl.Secondary, c);
    }
}
