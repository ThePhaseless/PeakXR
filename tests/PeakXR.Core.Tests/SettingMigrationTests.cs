namespace PeakXR.Core.Tests;

public class SettingMigrationTests
{
    [Fact]
    public void ValueLeftAtTheOldDefaultMoves()
    {
        Assert.True(SettingMigration.ShouldMove(0.5f, 0.5f, 0.55f));
        Assert.True(SettingMigration.ShouldMove(30.000001f, 30f, 40f));
    }

    [Fact]
    public void ValueThePlayerChangedStays()
    {
        Assert.False(SettingMigration.ShouldMove(0.7f, 0.5f, 0.55f));
    }

    [Fact]
    public void AlreadyAtTheNewDefaultIsNotRewritten()
    {
        Assert.False(SettingMigration.ShouldMove(0.55f, 0.55f, 0.55f));
        Assert.False(SettingMigration.ShouldMove(0.55f, 0.5f, 0.55f));
    }
}
