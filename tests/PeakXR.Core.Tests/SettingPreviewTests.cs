namespace PeakXR.Core.Tests;

public class SettingPreviewTests
{
    [Theory]
    [InlineData("Comfort", "VignetteStrength", PreviewKind.Vignette)]
    [InlineData("Comfort", "Vignette", PreviewKind.Vignette)]
    [InlineData("Comfort", "TurnMode", PreviewKind.Turn)]
    [InlineData("Comfort", "SnapTurnAngle", PreviewKind.Turn)]
    [InlineData("Comfort", "SmoothTurnSpeed", PreviewKind.Turn)]
    [InlineData("Locomotion", "MoveRelativeTo", PreviewKind.MoveDirection)]
    [InlineData("UI", "FadeSeconds", PreviewKind.Fade)]
    [InlineData("Camera", "FadeSeconds", PreviewKind.Fade)]
    [InlineData("UI.HUD", "HudMode", PreviewKind.Hud)]
    [InlineData("UI.HUD", "HudDistance", PreviewKind.Hud)]
    [InlineData("UI.HUD", "WristHand", PreviewKind.Wrist)]
    [InlineData("UI.HUD", "WristWidth", PreviewKind.Wrist)]
    [InlineData("UI", "MenuDistance", PreviewKind.MenuPlacement)]
    [InlineData("UI", "MenuTilt", PreviewKind.MenuPlacement)]
    [InlineData("UI", "StatusOverlays", PreviewKind.StatusGlow)]
    [InlineData("UI.Status", "PeripheralIntensity", PreviewKind.StatusGlow)]
    [InlineData("UI", "NameTagScale", PreviewKind.NameTag)]
    [InlineData("UI.Pointer", "HoverHaptics", PreviewKind.Haptic)]
    [InlineData("Interaction", "HapticStrength", PreviewKind.Haptic)]
    [InlineData("Inventory", "SlotRadius", PreviewKind.BeltSlots)]
    [InlineData("Inventory", "SlotLayout", PreviewKind.BeltSlots)]
    [InlineData("Render", "RenderScale", PreviewKind.Info)]
    [InlineData("Voice", "Anything", PreviewKind.Info)]
    public void Kind_by_section_and_key(string section, string key, PreviewKind expected)
    {
        Assert.Equal(expected, SettingPreviewTable.KindFor(section, key));
    }

    [Theory]
    [InlineData(PreviewKind.Vignette, true)]
    [InlineData(PreviewKind.Turn, true)]
    [InlineData(PreviewKind.Info, true)]
    [InlineData(PreviewKind.Fade, false)]
    [InlineData(PreviewKind.Haptic, false)]
    [InlineData(PreviewKind.MenuPlacement, false)]
    public void Hover_shows_only_passive_visuals(PreviewKind kind, bool visual)
    {
        Assert.Equal(visual, SettingPreviewTable.VisualOnHover(kind));
    }

    [Theory]
    [InlineData("XR", "DepthSubmission", true)]
    [InlineData("XR", "FallbackToOpenVR", true)]
    [InlineData("Interaction.Binoculars", "LensResolution", true)]
    [InlineData("UI", "TitleOnBeach", true)]
    [InlineData("Render", "RenderScale", true)]
    [InlineData("XR", "PauseInputOnFocusLoss", false)]
    [InlineData("Comfort", "SnapTurnAngle", false)]
    public void Apply_note_only_for_entries_that_do_not_apply_at_once(
        string section,
        string key,
        bool hasNote
    )
    {
        Assert.Equal(hasNote, SettingPreviewTable.ApplyNote(section, key) != null);
    }

    [Fact]
    public void Change_shows_at_once_then_ends()
    {
        var clock = new PreviewClock();
        clock.Change();
        Assert.True(clock.Active);
        Assert.False(clock.FromHover);
        clock.Tick(PreviewClock.ChangeSeconds + 0.01f);
        Assert.False(clock.Active);
        Assert.Equal(0f, clock.Alpha);
    }

    [Fact]
    public void Hover_starts_after_dwell()
    {
        var clock = new PreviewClock();
        clock.HoverEnter();
        Assert.False(clock.Tick(PreviewClock.HoverDwell * 0.5f));
        Assert.False(clock.Active);
        Assert.True(clock.Tick(PreviewClock.HoverDwell));
        Assert.True(clock.Active);
        Assert.True(clock.FromHover);
        Assert.False(clock.Tick(0.01f));
    }

    [Fact]
    public void Sweeping_past_a_row_shows_nothing()
    {
        var clock = new PreviewClock();
        clock.HoverEnter();
        clock.Tick(PreviewClock.HoverDwell * 0.5f);
        clock.HoverExit();
        Assert.False(clock.Tick(PreviewClock.HoverDwell));
        Assert.False(clock.Active);
    }

    [Fact]
    public void Change_cancels_a_pending_hover()
    {
        var clock = new PreviewClock();
        clock.HoverEnter();
        clock.Change();
        Assert.False(clock.Tick(PreviewClock.HoverDwell));
        Assert.False(clock.FromHover);
    }

    [Fact]
    public void Fades_out_over_the_last_moment()
    {
        var clock = new PreviewClock();
        clock.Change();
        Assert.Equal(1f, clock.Alpha);
        clock.Tick(PreviewClock.ChangeSeconds - PreviewClock.FadeOut * 0.5f);
        Assert.InRange(clock.Alpha, 0.45f, 0.55f);
        Assert.InRange(clock.Elapsed, 1.5f, PreviewClock.ChangeSeconds);
    }

    [Fact]
    public void Stop_ends_at_once()
    {
        var clock = new PreviewClock();
        clock.Change();
        clock.Stop();
        Assert.False(clock.Active);
    }
}
