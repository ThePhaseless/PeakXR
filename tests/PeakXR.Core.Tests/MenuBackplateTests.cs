using System.Numerics;

namespace PeakXR.Core.Tests;

public class MenuBackplateTests
{
    [Fact]
    public void Fit_GrowsContentByTheMarginOnEverySide()
    {
        Assert.True(
            MenuBackplate.Fit(
                new Vector2(-100, -50),
                new Vector2(300, 150),
                24f,
                out var c,
                out var s
            )
        );
        Assert.Equal(new Vector2(100, 50), c);
        Assert.Equal(new Vector2(448, 248), s);
    }

    [Fact]
    public void Fit_NothingDrawn_GivesNoPlate() =>
        Assert.False(
            MenuBackplate.Fit(
                new Vector2(float.MaxValue, float.MaxValue),
                new Vector2(float.MinValue, float.MinValue),
                24f,
                out _,
                out _
            )
        );

    [Fact]
    public void FitsContent_AcceptsTheFittedPlate_RejectsAFullCanvasOne()
    {
        var lo = new Vector2(-100, -50);
        var hi = new Vector2(300, 150);
        MenuBackplate.Fit(lo, hi, 24f, out var c, out var s);
        Assert.True(MenuBackplate.FitsContent(c, s, lo, hi, 24f, 2f));
        Assert.False(
            MenuBackplate.FitsContent(Vector2.Zero, new Vector2(1920, 1080), lo, hi, 24f, 2f)
        );
    }

    [Fact]
    public void IsFullPanel_OnlyWhenThePlateSpansTheCanvasBothWays()
    {
        var canvas = new Vector2(1920, 1080);
        Assert.True(MenuBackplate.IsFullPanel(new Vector2(1900, 1060), canvas));
        Assert.False(MenuBackplate.IsFullPanel(new Vector2(1900, 500), canvas));
        Assert.False(MenuBackplate.IsFullPanel(new Vector2(700, 1060), canvas));
    }

    [Fact]
    public void DefaultOpacity_KeepsWhiteTextAtLeast4p5To1_OverAWhiteBackground() =>
        Assert.True(MenuBackplate.WorstContrast(MenuBackplate.DefaultOpacity) >= 4.5f);

    [Fact]
    public void NoPlate_WhiteTextOnWhite_IsNoContrast() =>
        Assert.Equal(1f, MenuBackplate.WorstContrast(0f), 3);
}
