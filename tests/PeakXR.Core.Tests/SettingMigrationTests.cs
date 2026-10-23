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
}
