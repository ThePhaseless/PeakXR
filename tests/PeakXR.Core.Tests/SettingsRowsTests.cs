namespace PeakXR.Core.Tests;

public class SettingsRowsTests
{
    public enum Mode
    {
        A,
        B,
    }

    [Flags]
    public enum Bits
    {
        None = 0,
        X = 1,
    }

    [Theory]
    [InlineData(typeof(bool), false, false, true, RowKind.Toggle)]
    [InlineData(typeof(Mode), false, false, true, RowKind.Choice)]
    [InlineData(typeof(Bits), false, false, true, RowKind.Text)]
    [InlineData(typeof(string), false, true, true, RowKind.Choice)]
    [InlineData(typeof(float), true, false, true, RowKind.Slider)]
    [InlineData(typeof(int), true, false, true, RowKind.Slider)]
    [InlineData(typeof(double), true, false, true, RowKind.Slider)]
    [InlineData(typeof(float), false, false, true, RowKind.Text)]
    [InlineData(typeof(string), false, false, true, RowKind.Text)]
    [InlineData(typeof(object), false, false, false, RowKind.Unsupported)]
    public void Classify(Type type, bool hasRange, bool hasList, bool canConvert, RowKind expected)
    {
        Assert.Equal(expected, SettingsRows.Classify(type, hasRange, hasList, canConvert));
    }

    [Theory]
    [InlineData("SnapTurnAngle", "Snap turn angle")]
    [InlineData("HUDDistance", "HUD distance")]
    [InlineData("UseVR", "Use VR")]
    [InlineData("menu_width", "Menu width")]
    [InlineData("Fov90", "Fov 90")]
    [InlineData("Enabled", "Enabled")]
    [InlineData("", "")]
    public void Prettify(string key, string expected)
    {
        Assert.Equal(expected, SettingsRows.Prettify(key));
    }

    [Fact]
    public void IsIntegral()
    {
        Assert.True(SettingsRows.IsIntegral(typeof(int)));
        Assert.True(SettingsRows.IsIntegral(typeof(byte)));
        Assert.False(SettingsRows.IsIntegral(typeof(float)));
    }

    [Theory]
    [InlineData(3.6f, true, 0f, 10f, "4")]
    [InlineData(1.234f, false, 0f, 3f, "1.23")]
    [InlineData(12.34f, false, 0f, 50f, "12.3")]
    [InlineData(123.4f, false, 0f, 500f, "123")]
    public void Format(float value, bool integral, float min, float max, string expected)
    {
        Assert.Equal(expected, SettingsRows.Format(value, integral, min, max));
    }
}

public class SettingsPageTests
{
    [Fact]
    public void ShortList_IsOnePage()
    {
        Assert.Equal(1, SettingsRows.PageCount(5, 12));
        Assert.Equal(1, SettingsRows.PageCount(0, 12));
        Assert.Equal(1, SettingsRows.PageCount(12, 12));
    }

    [Fact]
    public void LongList_SplitsIntoPagesOfAtMostPageRows()
    {
        Assert.Equal(3, SettingsRows.PageCount(25, 12));
        SettingsRows.PageSlice(25, 12, 0, out int start, out int count);
        Assert.Equal((0, 12), (start, count));
        SettingsRows.PageSlice(25, 12, 2, out start, out count);
        Assert.Equal((24, 1), (start, count));
    }

    [Fact]
    public void PageOutOfRange_IsClamped()
    {
        // The list shrank (demos folded) while a later page was open: show its last page.
        SettingsRows.PageSlice(25, 12, 7, out int start, out int count);
        Assert.Equal((24, 1), (start, count));
        SettingsRows.PageSlice(25, 12, -3, out start, out count);
        Assert.Equal((0, 12), (start, count));
        Assert.Equal(2, SettingsRows.ClampPage(9, 25, 12));
    }
}
