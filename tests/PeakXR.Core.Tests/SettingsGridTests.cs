namespace PeakXR.Core.Tests;

public class SettingsGridTests
{
    [Theory]
    [InlineData(917f, 90f, 0f, 10)]
    [InlineData(900f, 90f, 0f, 10)]
    [InlineData(899f, 90f, 0f, 9)]
    [InlineData(917f, 90f, 10f, 9)]
    [InlineData(40f, 90f, 0f, 1)]
    [InlineData(0f, 90f, 0f, 1)]
    public void FitCountsWholeItemsAtLeastOne(float extent, float item, float spacing, int expected)
    {
        Assert.Equal(expected, SettingsGrid.Fit(extent, item, spacing));
    }

    [Theory]
    [InlineData("UI.HUD", "UI")]
    [InlineData("UI", "UI")]
    [InlineData("Interaction.Aim.Extra", "Interaction")]
    [InlineData("", "")]
    public void GroupIsTheSectionBeforeTheFirstDot(string section, string expected)
    {
        Assert.Equal(expected, SettingsGrid.GroupOf(section));
    }

    [Theory]
    [InlineData("UI.HUD", "HUD")]
    [InlineData("UI", "")]
    [InlineData("Interaction.Aim.Extra", "Aim.Extra")]
    public void SubsectionIsTheRest(string section, string expected)
    {
        Assert.Equal(expected, SettingsGrid.SubsectionOf(section));
    }
}
