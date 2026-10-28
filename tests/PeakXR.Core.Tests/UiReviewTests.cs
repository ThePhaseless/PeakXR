namespace PeakXR.Core.Tests;

public class UiReviewTests
{
    [Fact]
    public void MinPixels_MeetsTheFloorAtTheHudScale()
    {
        // ui.spatial's smallest HUD pixel is 0.045 deg: 26 px (the old button labels) was 1.17 deg.
        int px = ReadableText.MinPixels(0.045f);
        Assert.True(px * 0.045f >= ReadableText.MinDegrees);
        Assert.True((px - 1) * 0.045f < ReadableText.MinDegrees);
        Assert.True(26 * 0.045f < ReadableText.MinDegrees);
    }

    [Fact]
    public void ButtonLabelPlate_KeepsBothTextColoursAt4p5_EvenOverSnow()
    {
        float under = MenuBackplate.WorstUnder(ButtonLabelStyle.PlateOpacity);
        Assert.True(Contrast.Ratio(Contrast.Luminance(Vector3.One), under) >= Contrast.MinText);
        Assert.True(
            Contrast.Ratio(Contrast.Luminance(ButtonLabelStyle.GlyphColor), under)
                >= Contrast.MinText
        );
    }

    [Fact]
    public void ButtonGlyph_StaysReadableWhenItsLabelIsAtTheFloor()
    {
        // A label drawn at the smallest readable size: its button name, scaled from it, must still read.
        int label = ReadableText.MinPixels(0.045f);
        Assert.True(label * ButtonLabelStyle.GlyphScale * 0.045f >= ReadableText.MinDegrees);
    }

    [Fact]
    public void TitleTouchReach_IsOutsideTheAvoidBand()
    {
        Assert.False(TitleLayout.InAvoidBand(TitleLayout.TouchReach));
        Assert.True(TitleLayout.TouchReach <= 0.46f && TitleLayout.TouchReach >= 0.42f);
        // The old default sat inside Meta's 0.5-0.8 m band: too far to touch, too near for a ray.
        Assert.True(TitleLayout.InAvoidBand(0.55f));
    }

    [Theory]
    [InlineData("Interaction", "HapticStrength", "Haptics.Interaction")]
    [InlineData("Interaction", "Haptics", "Haptics.Interaction")]
    [InlineData("UI.Status", "Haptics", "Haptics.UI.Status")]
    [InlineData("Locomotion", "Heartbeat", "Haptics.Locomotion")]
    [InlineData("Social", "CarriedSway", "Haptics.Social")]
    [InlineData("Interaction", "ClimbSurfaceFeel", "Haptics.Interaction")]
    [InlineData("UI.Pointer", "HoverHaptics", "Haptics.UI.Pointer")]
    [InlineData("Interaction", "CookOverFire", "Items")]
    [InlineData("Interaction", "HandIntoPortal", "Items")]
    [InlineData("Interaction", "TriggerEats", "Items")]
    [InlineData("Interaction", "GrabToClimb", "Interaction")]
    [InlineData("Comfort", "Vignette", "Comfort")]
    public void DisplaySection_GathersVibrationAndItemGestures(
        string section,
        string key,
        string expected
    ) => Assert.Equal(expected, SettingsGrid.DisplaySection(section, key));

    [Fact]
    public void DisplaySection_KeepsTheGroupRule()
    {
        Assert.Equal(
            "Haptics",
            SettingsGrid.GroupOf(SettingsGrid.DisplaySection("UI.Status", "Haptics"))
        );
        Assert.Equal(
            "UI.Status",
            SettingsGrid.SubsectionOf(SettingsGrid.DisplaySection("UI.Status", "Haptics"))
        );
    }
}
