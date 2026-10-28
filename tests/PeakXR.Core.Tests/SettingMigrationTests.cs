namespace PeakXR.Core.Tests;

public class SettingMigrationTests
{
    [Fact]
    public void ValueLeftAtTheOldDefaultMoves()
    {
        Assert.True(SettingMigration.ShouldMove(0.5f, 0.5f, 0.55f, alreadyRan: false));
        Assert.True(SettingMigration.ShouldMove(30.000001f, 30f, 40f, alreadyRan: false));
    }

    [Fact]
    public void ValueThePlayerChangedStays()
    {
        Assert.False(SettingMigration.ShouldMove(0.7f, 0.5f, 0.55f, alreadyRan: false));
    }

    [Fact]
    public void AlreadyAtTheNewDefaultIsNotRewritten()
    {
        Assert.False(SettingMigration.ShouldMove(0.55f, 0.55f, 0.55f, alreadyRan: false));
        Assert.False(SettingMigration.ShouldMove(0.55f, 0.5f, 0.55f, alreadyRan: false));
    }

    [Fact]
    public void RunsOnceSoAPlayerCanSetTheOldValueBack()
    {
        Assert.False(SettingMigration.ShouldMove(0.5f, 0.5f, 0.55f, alreadyRan: true));
    }

    [Fact]
    public void MarkerKeyNamesTheSettingAndTheOldDefault()
    {
        Assert.Equal(
            "UI.Keyboard.KeyboardDistance.From0.5",
            SettingMigration.MarkerKey("UI.Keyboard", "KeyboardDistance", 0.5f)
        );
    }

    // HapticStrength: old default 0.5 -> new default 1, the scale doubled (range 0..2).
    static float Rescale(float saved, bool existed = true, bool ran = false) =>
        SettingMigration.Rescaled(saved, 0.5f, 1f, 2f, 0f, 2f, existed, ran);

    [Fact]
    public void Rescale_TheOldDefaultMovesToTheNewOne() => Assert.Equal(1f, Rescale(0.5f));

    [Theory]
    [InlineData(0.25f, 0.5f)]
    [InlineData(0.8f, 1.6f)]
    [InlineData(1f, 2f)]
    [InlineData(0f, 0f)]
    public void Rescale_AChosenValueScalesSoItFeelsTheSame(float saved, float moved) =>
        Assert.Equal(moved, Rescale(saved), 5);

    [Fact]
    public void Rescale_ClampsToTheRange() =>
        Assert.Equal(2f, SettingMigration.Rescaled(1.5f, 0.5f, 1f, 2f, 0f, 2f, true, false));

    [Fact]
    public void Rescale_NeverTouchesAFreshConfigOrRunsTwice()
    {
        // No saved value: the new default was written fresh, already on the new scale.
        Assert.Equal(1f, Rescale(1f, existed: false));
        Assert.Equal(1.6f, Rescale(1.6f, ran: true));
        Assert.Equal(0.5f, Rescale(0.5f, ran: true));
    }

    static bool RunSaved(string raw, out float value, bool ran = false) =>
        SettingMigration.RescaleSaved(raw, 0.5f, 1f, 2f, 0f, 2f, ran, out value);

    [Theory]
    [InlineData("0.5", 1f)]
    [InlineData("0.8", 1.6f)]
    [InlineData(" 0.25 ", 0.5f)]
    public void RescaleSaved_ASavedNumberIsRescaled(string raw, float expected)
    {
        Assert.True(RunSaved(raw, out var v));
        Assert.Equal(expected, v, 5);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("0,5")]
    public void RescaleSaved_AnUnparseableValueIsLeftAlone(string raw)
    {
        // BepInEx falls back to the new default for it; doubling that would be wrong.
        Assert.False(RunSaved(raw, out _));
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e39")]
    public void RescaleSaved_ANonFiniteValueIsLeftAlone(string raw) =>
        Assert.False(RunSaved(raw, out _));

    [Theory]
    [InlineData(120f, "Comfort.SmoothTurnSpeed.From120")]
    [InlineData(0.15f, "Comfort.SmoothTurnSpeed.From0.15")]
    [InlineData(0.1f, "Comfort.SmoothTurnSpeed.From0.1")]
    public void MarkerKey_WritesTheShortRoundTripForm(float oldDefault, string expected) =>
        Assert.Equal(
            expected,
            SettingMigration.MarkerKey("Comfort", "SmoothTurnSpeed", oldDefault)
        );

    [Fact]
    public void RescaleSaved_NothingSavedOrAlreadyRan_NoRescale()
    {
        Assert.False(RunSaved(null, out _));
        Assert.False(RunSaved("0.8", out _, ran: true));
    }
}
