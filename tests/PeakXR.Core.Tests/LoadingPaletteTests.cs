namespace PeakXR.Core.Tests;

public class LoadingPaletteTests
{
    // The plane screen's fills (resources.assets LoadingScreen: Black, Color, Screen, Fade).
    static readonly Rgba Black = new(0f, 0f, 0f, 1f);
    static readonly Rgba Cream = new(0.99f, 0.85f, 0.57f, 0.43f);
    static readonly Rgba Brown = new(0.56f, 0.40f, 0.22f, 0.25f);
    static readonly Rgba ClearFade = new(0f, 0f, 0f, 0f);

    static float Max(Rgba c) => MathF.Max(c.R, MathF.Max(c.G, c.B));

    [Fact]
    public void PickKey_TakesTheMostColourfulVisibleFill()
    {
        Assert.True(
            LoadingPalette.TryPickKey(new[] { Black, Cream, Brown, ClearFade }, out var key)
        );
        Assert.Equal(Cream.R, key.R);
        Assert.Equal(Cream.B, key.B);
    }

    [Fact]
    public void PickKey_IgnoresGreysAndInvisibleFills()
    {
        var white = new Rgba(1f, 1f, 1f, 1f);
        var hiddenRed = new Rgba(1f, 0f, 0f, 0f);
        Assert.False(LoadingPalette.TryPickKey(new[] { Black, white, hiddenRed }, out _));
        Assert.False(LoadingPalette.TryPickKey(Array.Empty<Rgba>(), out _));
    }

    [Fact]
    public void For_GreyScreensGetTheDefaultRoom()
    {
        Assert.Equal(LoadingPalette.Default, LoadingPalette.For(new[] { Black }));
    }

    [Fact]
    public void FromKey_KeepsTheHueButStaysDark()
    {
        var p = LoadingPalette.FromKey(Cream);
        // Warm stays warm: red over green over blue, like the key.
        Assert.True(p.Horizon.R > p.Horizon.G && p.Horizon.G > p.Horizon.B);
        Assert.True(Max(p.Horizon) <= 0.15f, $"horizon too bright: {Max(p.Horizon)}");
        Assert.True(Max(p.Void) <= 0.04f, $"void too bright: {Max(p.Void)}");
        Assert.True(Max(p.Void) < Max(p.Horizon));
        // Lines and accent are readable over the horizon.
        Assert.True(Max(p.GridLine) > 3f * Max(p.Horizon));
        Assert.True(Max(p.Accent) > 0.8f);
    }

    [Fact]
    public void FromKey_DarkKeyIsLiftedToTheSameRoomBrightness()
    {
        var bright = LoadingPalette.FromKey(Cream);
        var dark = LoadingPalette.FromKey(Brown);
        TestUtil.Near(Max(bright.Horizon), Max(dark.Horizon));
        TestUtil.Near(Max(bright.Accent), Max(dark.Accent));
    }

    [Fact]
    public void Lerp_BlendsEveryColour()
    {
        var a = LoadingPalette.Default;
        var b = LoadingPalette.FromKey(Cream);
        Assert.Equal(a, LoadingPalette.Lerp(a, b, 0f));
        Assert.Equal(b, LoadingPalette.Lerp(a, b, 1f));
        var mid = LoadingPalette.Lerp(a, b, 0.5f);
        TestUtil.Near((a.Void.R + b.Void.R) * 0.5f, mid.Void.R);
        TestUtil.Near((a.Accent.B + b.Accent.B) * 0.5f, mid.Accent.B);
        TestUtil.Near((a.Ink.G + b.Ink.G) * 0.5f, mid.Ink.G);
        Assert.Equal(b, LoadingPalette.Lerp(a, b, 2f));
    }

    [Fact]
    public void NeedsInk_OnlyForNearBlackArt()
    {
        // The splash's black LandCrab logo vanishes in a dark room; the plane screen's dark glow is left alone.
        Assert.True(LoadingPalette.NeedsInk(new Rgba(0f, 0f, 0f, 1f)));
        Assert.False(LoadingPalette.NeedsInk(new Rgba(0.24f, 0.06f, 0f, 0.47f)));
        Assert.False(LoadingPalette.NeedsInk(new Rgba(0.98f, 0.97f, 0.94f, 1f)));
    }

    [Theory]
    [InlineData(1f, 1f, true, false, true)]
    [InlineData(0.92f, 0.95f, true, false, true)]
    [InlineData(0.6f, 1f, true, false, false)] // a side panel, not the backing
    [InlineData(1f, 1f, false, false, false)] // artwork (sun rays, globe) stays
    [InlineData(1f, 1f, true, true, false)] // a mask would hide what it clips
    public void IsBacking_FullCanvasPlainUnmaskedFill(
        float coverX,
        float coverY,
        bool plain,
        bool mask,
        bool expected
    ) => Assert.Equal(expected, LoadingPalette.IsBacking(coverX, coverY, plain, mask));
}
