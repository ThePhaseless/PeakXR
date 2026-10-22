namespace PeakXR.Core.Tests;

public class BackdropRulesTests
{
    // A plain flat panel: an image half the canvas wide, with text and buttons drawn on top of it.
    private static BackdropTraits Panel() =>
        new()
        {
            IsImage = true,
            WidthFraction = 0.5f,
            HeightFraction = 0.6f,
            ContentAbove = 5,
            Flat = true,
        };

    [Fact]
    public void FullCanvasImage_IsADimmer()
    {
        var t = Panel();
        t.WidthFraction = 1f;
        t.HeightFraction = 0.95f;
        t.ContentAbove = 0;
        Assert.Equal(BackdropRole.Dimmer, BackdropRules.Classify(t));
    }

    [Fact]
    public void LargeImageUnderContent_IsAPanel() =>
        Assert.Equal(BackdropRole.Panel, BackdropRules.Classify(Panel()));

    [Fact]
    public void DialogWindow_IsAPanel()
    {
        // The pause menu's quit confirm: 550x200 of a 1920x1080 canvas.
        var t = Panel();
        t.WidthFraction = 550f / 1920f;
        t.HeightFraction = 200f / 1080f;
        Assert.Equal(BackdropRole.Panel, BackdropRules.Classify(t));
    }

    [Fact]
    public void LargeArtworkUnderContent_Stays()
    {
        // A drawn banner with stars and a title on it: a picture, not a fill.
        var t = Panel();
        t.Flat = false;
        Assert.Equal(BackdropRole.Content, BackdropRules.Classify(t));
    }

    [Fact]
    public void TightBackingUnderOneLabel_StaysContent()
    {
        var t = Panel();
        t.ContentAbove = 1;
        Assert.Equal(BackdropRole.Content, BackdropRules.Classify(t));
    }

    [Fact]
    public void SmallImageUnderContent_StaysContent()
    {
        // An item card or icon frame: well under the panel area threshold.
        var t = Panel();
        t.WidthFraction = 0.1f;
        t.HeightFraction = 0.15f;
        Assert.Equal(BackdropRole.Content, BackdropRules.Classify(t));
    }

    [Theory]
    [InlineData(nameof(BackdropTraits.Interactive))]
    [InlineData(nameof(BackdropTraits.LiveTexture))]
    [InlineData(nameof(BackdropTraits.Gauge))]
    public void MeaningfulImages_StayContentEvenWhenFullCanvas(string flag)
    {
        var t = Panel();
        t.WidthFraction = t.HeightFraction = 1f;
        switch (flag)
        {
            case nameof(BackdropTraits.Interactive):
                t.Interactive = true;
                break;
            case nameof(BackdropTraits.LiveTexture):
                t.LiveTexture = true;
                break;
            default:
                t.Gauge = true;
                break;
        }
        Assert.Equal(BackdropRole.Content, BackdropRules.Classify(t));
    }

    [Fact]
    public void Text_IsNeverABackdrop()
    {
        var t = Panel();
        t.IsImage = false;
        t.WidthFraction = t.HeightFraction = 1f;
        Assert.Equal(BackdropRole.Content, BackdropRules.Classify(t));
    }

    [Fact]
    public void LargePartOfAHiddenPanel_GoesWithIt()
    {
        // A header band or the outline frame of a ticket whose paper was hidden: nothing on top of it, small on
        // the canvas, but a big share of the panel it decorated.
        var t = Panel();
        t.WidthFraction = 0.6f;
        t.HeightFraction = 0.08f;
        t.ContentAbove = 0;
        t.PanelShare = 0.25f;
        t.Flat = true;
        Assert.Equal(BackdropRole.Panel, BackdropRules.Classify(t));
    }

    [Fact]
    public void LargeArtworkOnAHiddenPanel_Stays()
    {
        // A drawn picture (plain sprite, not a flat or 9-sliced fill) is content even when it is big.
        var t = Panel();
        t.WidthFraction = 0.3f;
        t.HeightFraction = 0.2f;
        t.ContentAbove = 0;
        t.PanelShare = 0.4f;
        t.Flat = false;
        Assert.Equal(BackdropRole.Content, BackdropRules.Classify(t));
    }

    [Fact]
    public void ShadowUnderACardOnAHiddenPanel_GoesWithIt()
    {
        // A drop-shadow sprite under a popup card: not a flat fill, but it sits under the card's content.
        var t = Panel();
        t.Flat = false;
        t.WidthFraction = 0.18f;
        t.HeightFraction = 0.14f;
        t.ContentAbove = 4;
        t.PanelShare = 0.26f;
        Assert.Equal(BackdropRole.Panel, BackdropRules.Classify(t));
    }

    [Fact]
    public void FillGroupedWithAHiddenPanel_GoesWithIt()
    {
        // The ticket's dotted line, the end screen's hole-punch strip, a tooltip card that moves around: small,
        // anywhere on screen, but in the same group as the paper that was hidden.
        var t = Panel();
        t.WidthFraction = 0.01f;
        t.HeightFraction = 0.4f;
        t.ContentAbove = 0;
        t.InPanelGroup = true;
        Assert.Equal(BackdropRole.Panel, BackdropRules.Classify(t));
    }

    [Fact]
    public void RowStripBelowTheCanvasEdge_GoesWithItsList()
    {
        // A settings row drawn in the list's viewport past the canvas edge: no size on the canvas, still the list's.
        var t = Panel();
        t.WidthFraction = 0f;
        t.HeightFraction = 0f;
        t.ContentAbove = 3;
        t.InPanelGroup = true;
        Assert.Equal(BackdropRole.Panel, BackdropRules.Classify(t));
    }

    [Fact]
    public void PictureGroupedWithAHiddenPanel_Stays()
    {
        // The barcode or logo printed on the ticket.
        var t = Panel();
        t.Flat = false;
        t.WidthFraction = 0.16f;
        t.HeightFraction = 0.06f;
        t.ContentAbove = 0;
        t.InPanelGroup = true;
        Assert.Equal(BackdropRole.Content, BackdropRules.Classify(t));
    }

    [Fact]
    public void BannerGroupedWithAHiddenPanel_StaysEvenWithMarksOnIt()
    {
        // The end screen's "you peaked" banner with stars and a title on it, hanging off the hidden paper.
        var t = Panel();
        t.Flat = false;
        t.WidthFraction = 0.3f;
        t.HeightFraction = 0.2f;
        t.ContentAbove = 9;
        t.InPanelGroup = true;
        Assert.Equal(BackdropRole.Content, BackdropRules.Classify(t));
    }

    [Fact]
    public void SmallArtOnAHiddenPanel_Stays()
    {
        // A barcode or logo printed on the ticket.
        var t = Panel();
        t.WidthFraction = 0.16f;
        t.HeightFraction = 0.06f;
        t.ContentAbove = 0;
        t.PanelShare = 0.02f;
        t.Flat = true;
        Assert.Equal(BackdropRole.Content, BackdropRules.Classify(t));
    }

    [Fact]
    public void Halo_ContrastsWithTheTextColour()
    {
        Assert.True(BackdropRules.NeedsLightHalo(0.2f, 0.15f, 0.1f));
        Assert.False(BackdropRules.NeedsLightHalo(1f, 1f, 1f));
        // Saturated yellow reads as light (luma, not the channel average).
        Assert.False(BackdropRules.NeedsLightHalo(1f, 0.85f, 0.2f));
    }
}
