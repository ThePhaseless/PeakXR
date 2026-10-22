using System.Collections.Generic;
using System.Linq;

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
    [InlineData(1361f, 600f, 48f, 2, 2)]
    [InlineData(1361f, 600f, 48f, 3, 2)]
    [InlineData(1361f, 400f, 48f, 3, 3)]
    [InlineData(1361f, 400f, 48f, 2, 2)]
    [InlineData(900f, 600f, 48f, 2, 1)]
    [InlineData(100f, 600f, 48f, 2, 1)]
    public void ColumnsFitTheWidth(float width, float min, float gap, int max, int expected)
    {
        Assert.Equal(expected, SettingsGrid.Columns(width, min, gap, max));
    }

    [Fact]
    public void ColumnWidthSharesTheGaps()
    {
        Assert.Equal(656.5f, SettingsGrid.ColumnWidth(1361f, 2, 48f), 3);
        Assert.Equal(1361f, SettingsGrid.ColumnWidth(1361f, 1, 48f), 3);
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

    static bool[] Rows(string shape) => shape.Select(c => c == 'H').ToArray();

    static string Show(IReadOnlyList<GridPage> pages, string shape) =>
        string.Join(
            " | ",
            pages.Select(p =>
                string.Join(
                    " / ",
                    p.Columns.Select(c =>
                        string.Concat(c.Select(i => shape[i] == 'H' ? "H" + i : "" + i))
                    )
                )
            )
        );

    [Fact]
    public void RowsFillColumnsThenPages()
    {
        var pages = SettingsGrid.Paginate(Rows("rrrrrrr"), rows: 3, columns: 2);
        Assert.Equal("012 / 345 | 6", Show(pages, "rrrrrrr"));
    }

    [Fact]
    public void EverythingFitsOnOnePage()
    {
        var pages = SettingsGrid.Paginate(Rows("rrr"), rows: 3, columns: 2);
        Assert.Single(pages);
        Assert.Single(pages[0].Columns);
    }

    [Fact]
    public void HeaderNeverEndsAColumn()
    {
        // Rows 0,1 then a header at slot 2 of 3: it moves to the next column with its first row.
        const string shape = "rrHrr";
        Assert.Equal("01 / H234", Show(SettingsGrid.Paginate(Rows(shape), 3, 2), shape));
    }

    [Fact]
    public void ContinuedSectionRepeatsItsHeader()
    {
        const string shape = "Hrrrr";
        Assert.Equal("H012 / H034", Show(SettingsGrid.Paginate(Rows(shape), 3, 2), shape));
    }

    [Fact]
    public void RepeatCarriesAcrossPages()
    {
        const string shape = "Hrrrrrr";
        Assert.Equal("H012 / H034 | H056", Show(SettingsGrid.Paginate(Rows(shape), 3, 2), shape));
    }

    [Fact]
    public void RowsBeforeAnyHeaderAreNotRepeated()
    {
        const string shape = "rrrr";
        Assert.Equal("012 / 3", Show(SettingsGrid.Paginate(Rows(shape), 3, 2), shape));
    }

    [Fact]
    public void OneRowColumnsStillTerminate()
    {
        const string shape = "Hrr";
        var pages = SettingsGrid.Paginate(Rows(shape), 1, 2);
        Assert.Equal("H0 / 1 | 2", Show(pages, shape));
    }

    [Fact]
    public void EmptyListIsOneEmptyPage()
    {
        var pages = SettingsGrid.Paginate(Array.Empty<bool>(), 3, 2);
        Assert.Single(pages);
        Assert.Single(pages[0].Columns);
        Assert.Empty(pages[0].Columns[0]);
    }

    [Fact]
    public void EveryRowAppearsOnceInOrder()
    {
        var shape = "rrHrrrrrHrHrrrrrrrrrrHr";
        var pages = SettingsGrid.Paginate(Rows(shape), 4, 2);
        var seen = pages
            .SelectMany(p => p.Columns)
            .SelectMany(c => c)
            .Where(i => shape[i] == 'r')
            .ToList();
        Assert.Equal(Enumerable.Range(0, shape.Length).Where(i => shape[i] == 'r'), seen);
        Assert.All(pages, p => Assert.All(p.Columns, c => Assert.InRange(c.Count, 1, 4)));
        Assert.All(pages, p => Assert.InRange(p.Columns.Count, 1, 2));
    }

    [Fact]
    public void LastColumnBesideAnEmptyOneOpensRight()
    {
        // A preview opens on the side with room: the right unless a later column on the page holds rows.
        var pages = SettingsGrid.Paginate(Rows("rrrrr"), 3, 2);
        Assert.True(SettingsGrid.OpensLeft(pages[0], 0));
        Assert.False(SettingsGrid.OpensLeft(pages[0], 1));
        var single = SettingsGrid.Paginate(Rows("rr"), 3, 2);
        Assert.False(SettingsGrid.OpensLeft(single[0], 0));
    }

    [Theory]
    [InlineData(840f, 18, 48f, 30f, 1, 46.667f)]
    [InlineData(840f, 10, 48f, 30f, 1, 48f)]
    [InlineData(400f, 18, 48f, 30f, 2, 44.444f)]
    [InlineData(100f, 18, 48f, 30f, 3, 16.667f)]
    public void RailSplitsIntoColumnsWhenChipsGetTooThin(
        float height,
        int count,
        float max,
        float min,
        int columns,
        float pitch
    )
    {
        SettingsGrid.Rail(height, count, max, min, out int cols, out float p);
        Assert.Equal(columns, cols);
        Assert.Equal(pitch, p, 2);
    }

    [Theory]
    [InlineData(new float[0], new float[0], 0f, 100f)]
    [InlineData(new[] { 80f }, new[] { 110f }, 0f, 80f)]
    [InlineData(new[] { 80f, -10f }, new[] { 110f, 10f }, 10f, 80f)]
    [InlineData(new[] { 20f, 30f }, new[] { 40f, 50f }, 50f, 100f)]
    [InlineData(new[] { 200f }, new[] { 300f }, 0f, 100f)]
    [InlineData(new[] { -5f }, new[] { 105f }, 100f, 100f)]
    public void RailTakesTheLargestFreeSpan(float[] lows, float[] highs, float lo, float hi)
    {
        // The rail runs down the free side of the panel between the game's own buttons (Back, Blocklist).
        SettingsGrid.FreeSpan(0f, 100f, lows, highs, out float a, out float b);
        Assert.Equal(lo, a, 3);
        Assert.Equal(hi, b, 3);
    }
}
