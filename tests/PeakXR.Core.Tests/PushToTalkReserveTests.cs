namespace PeakXR.Core.Tests;

/// <summary>
/// Voice.PushToTalkButton picks a button that already has a job in the layout (BindingTable.Conflict). Reserved in
/// the mapper, that job stops while the button talks; every other button keeps working.
/// </summary>
public class PushToTalkReserveTests
{
    const float Dt = 1f / 90f;

    static ActionMapper Mapper() => new() { DominantLeft = false, ItemInLeft = false };

    [Fact]
    public void APickedButtonWithAJob_IsAConflict()
    {
        Assert.True(ButtonName.TryParse("RightPrimary", false, out bool left, out var control));
        Assert.True(
            BindingTable.Conflict(BindingTable.Default, left, control, false, out var action)
        );
        Assert.Equal(VRAction.Jump, action);
    }

    [Fact]
    public void Reserved_TheOldJobNoLongerFires()
    {
        Assert.True(ButtonName.TryParse("RightPrimary", false, out bool left, out var control));
        BindingTable.Conflict(BindingTable.Default, left, control, false, out var action);
        var m = Mapper();
        m.SetReserved(left, control, true);
        m.Update(default, default, Dt);
        m.Update(default, new ControllerInput { Primary = true }, Dt);
        Assert.False(m.Pressed(action));
        Assert.False(m.Held(action));
    }

    [Fact]
    public void Unreserved_TheJobFiresAgain()
    {
        Assert.True(ButtonName.TryParse("RightPrimary", false, out bool left, out var control));
        var m = Mapper();
        m.SetReserved(left, control, true);
        m.SetReserved(left, control, false);
        m.Update(default, default, Dt);
        m.Update(default, new ControllerInput { Primary = true }, Dt);
        Assert.True(m.Pressed(VRAction.Jump));
    }

    [Fact]
    public void Reserving_LeavesTheOtherButtonsAlone()
    {
        Assert.True(ButtonName.TryParse("RightPrimary", false, out bool left, out var control));
        var m = Mapper();
        m.SetReserved(left, control, true);
        m.Update(default, default, Dt);
        m.Update(default, new ControllerInput { Primary = true, Secondary = true }, Dt);
        Assert.False(m.Pressed(VRAction.Jump));
        Assert.True(
            BindingTable.Conflict(
                BindingTable.Default,
                false,
                VRControl.Secondary,
                false,
                out var other
            )
        );
        Assert.True(m.Held(other) || m.Pressed(other), $"{other} should still work");
    }
}
