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
}
