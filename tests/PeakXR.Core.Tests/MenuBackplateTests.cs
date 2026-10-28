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
    public void IsFullPanel_APlateSpanningTheCanvasAroundSmallerContent()
    {
        var canvas = new Vector2(1920, 1080);
        // A pause menu's button column on a 16:9 plate.
        Assert.True(
            MenuBackplate.IsFullPanel(new Vector2(1900, 1060), new Vector2(600, 500), 24f, canvas)
        );
        Assert.False(
            MenuBackplate.IsFullPanel(new Vector2(1900, 500), new Vector2(1852, 452), 24f, canvas)
        );
    }

    [Fact]
    public void IsFullPanel_NotWhenTheDrawnContentReallyFillsTheCanvas() =>
        // VR run 47: the settings window (tabs, rail, rows) draws 1705x991 px; its fitted plate is no panel.
        Assert.False(
            MenuBackplate.IsFullPanel(
                new Vector2(1753, 1039),
                new Vector2(1705, 991),
                24f,
                new Vector2(1920, 1080)
            )
        );

    [Fact]
    public void DefaultOpacity_KeepsWhiteTextAtLeast4p5To1_OverAWhiteBackground() =>
        Assert.True(MenuBackplate.WorstContrast(MenuBackplate.DefaultOpacity) >= 4.5f);

    [Fact]
    public void NoPlate_WhiteTextOnWhite_IsNoContrast() =>
        Assert.Equal(1f, MenuBackplate.WorstContrast(0f), 3);

    [Fact]
    public void Scattered_WhenThePlateWouldSpanMoreThan70Degrees()
    {
        // 2 m wide at 1 m: about 90 degrees, the title's whole view.
        Assert.True(MenuBackplate.Scattered(2f, 0.5f, 1f));
        Assert.True(MenuBackplate.Scattered(0.5f, 2f, 1f));
        // A pause menu: 1 m wide at 1.5 m, about 37 degrees.
        Assert.False(MenuBackplate.Scattered(1f, 0.6f, 1.5f));
    }

    [Fact]
    public void Scattered_NoDistance_GetsNoPlate() =>
        Assert.True(MenuBackplate.Scattered(1f, 1f, 0f));

    private static int Sign(params (bool Drawn, float X, float W)[] parts)
    {
        int h = MenuBackplate.FoldStart;
        foreach (var p in parts)
            h = MenuBackplate.Fold(h, p.Drawn, p.X, 0f, p.W, 20f);
        return h;
    }

    [Fact]
    public void Fold_SameDrawnParts_SameSignature() =>
        Assert.Equal(
            Sign((true, 0f, 400f), (false, 300f, 60f)),
            Sign((true, 0f, 400f), (false, 300f, 60f))
        );

    [Fact]
    public void Fold_PartShownOrHidden_ChangesSignature() =>
        Assert.NotEqual(
            Sign((true, 0f, 400f), (false, 300f, 60f)),
            Sign((true, 0f, 400f), (true, 300f, 60f))
        );

    [Fact]
    public void Fold_DrawnPartResizedOrMoved_ChangesSignature()
    {
        Assert.NotEqual(Sign((true, 0f, 400f)), Sign((true, 0f, 412f)));
        Assert.NotEqual(Sign((true, 0f, 400f)), Sign((true, 6f, 400f)));
    }

    [Fact]
    public void Fold_HiddenPartChangingOrSubPixelJitter_KeepsSignature()
    {
        // A hidden part's tween (the extra bar shrinking away) and float noise must not trigger a re-measure.
        Assert.Equal(
            Sign((true, 0f, 400f), (false, 300f, 60f)),
            Sign((true, 0f, 400f), (false, 0f, 5f))
        );
        Assert.Equal(Sign((true, 0f, 400f)), Sign((true, 0.1f, 400.1f)));
    }

    [Fact]
    public void Fold_AnimatingFillMovingUnderOneStep_KeepsSignature()
    {
        // The stamina fill lerps its width every frame (A/StaminaBar.cs:126); moves inside one FoldPixels step must
        // not re-measure the plate.
        Assert.Equal(Sign((true, 0f, 400f)), Sign((true, 0f, 401.5f)));
        Assert.Equal(Sign((true, 0f, 400f)), Sign((true, 1.5f, 400f)));
    }

    [Fact]
    public void Unshrunk_APlateAroundTheReservedGroups()
    {
        var reserved = new Vector2(900, 200);
        Assert.True(MenuBackplate.Unshrunk(new Vector2(928, 228), reserved, 14f, 4f));
        // Fitted to a bar narrower and lower than the room its groups reserve.
        Assert.False(MenuBackplate.Unshrunk(new Vector2(628, 88), reserved, 14f, 4f));
        // Only as wide as the groups: it still shrank in height.
        Assert.False(MenuBackplate.Unshrunk(new Vector2(928, 88), reserved, 14f, 4f));
    }

    [Fact]
    public void FitsSpan_APlateAroundTheBarFrame_NotAroundItsTwiceAsWideMask()
    {
        // The stamina bar's frame spans -7..607 px; its hidden stencil mask (Bar/OutlineMask, 1214 px) -7..1207.
        Assert.True(MenuBackplate.FitsSpan(-21f, 621f, -7f, 607f, 14f, 5f));
        Assert.False(MenuBackplate.FitsSpan(-21f, 1221f, -7f, 607f, 14f, 5f));
        // Too short also fails: the frame pokes out.
        Assert.False(MenuBackplate.FitsSpan(-21f, 590f, -7f, 607f, 14f, 5f));
    }
}
