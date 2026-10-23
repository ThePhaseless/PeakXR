namespace PeakXR.Core.Tests;

public class TargetNameRuleTests
{
    [Theory]
    // UI.TargetNames, UI.ColorBlindNames, the game's colour-blind mode, the target is an item -> name shown
    [InlineData(true, ColorBlindNames.Off, false, false, true)]
    [InlineData(false, ColorBlindNames.FollowGame, false, true, false)]
    [InlineData(false, ColorBlindNames.FollowGame, true, true, true)]
    [InlineData(false, ColorBlindNames.FollowGame, true, false, false)]
    [InlineData(false, ColorBlindNames.On, false, true, true)]
    [InlineData(false, ColorBlindNames.Off, true, true, false)]
    public void Show(bool names, ColorBlindNames mode, bool game, bool item, bool expected) =>
        Assert.Equal(expected, TargetNameRule.Show(names, mode, game, item));
}
