namespace PeakXR.Core;

/// <summary>What a UI graphic on a world-space menu is, for deciding what stays visible in VR.</summary>
public enum BackdropRole
{
    /// <summary>Meaningful: buttons, text, icons, gauges, item art, highlights. Left alone.</summary>
    Content,

    /// <summary>Full-canvas dim or vignette: a flat slab once the canvas floats in the world.</summary>
    Dimmer,

    /// <summary>A large backing that only frames the content drawn on top of it on a flat screen.</summary>
    Panel,
}

/// <summary>Facts about one UI graphic, measured once when its page is shown.</summary>
public struct BackdropTraits
{
    /// <summary>An Image or RawImage; text and meshes are never backdrops.</summary>
    public bool IsImage;

    /// <summary>A Selectable on it or above it (button face, slider track, toggle box).</summary>
    public bool Interactive;

    /// <summary>Shows a render texture (character preview, camera feed).</summary>
    public bool LiveTexture;

    /// <summary>A filled image: a bar, ring or meter.</summary>
    public bool Gauge;

    /// <summary>Its rect, clipped to the canvas, as a fraction of the canvas size.</summary>
    public float WidthFraction,
        HeightFraction;

    /// <summary>Graphics drawn after it whose centre lies inside its rect.</summary>
    public int ContentAbove;

    /// <summary>A plain colour fill: no sprite, or a 9-sliced or tiled one (not a drawn picture).</summary>
    public bool Flat;

    /// <summary>Under the same parent as an already hidden panel (its own children included): part of that card
    /// wherever it sits on screen.</summary>
    public bool InPanelGroup;

    /// <summary>Share of an already hidden panel's area it covers, its centre inside that panel; 0 when none.</summary>
    public float PanelShare;
}

/// <summary>
/// Picks the flat-screen backdrops off a menu so its buttons and text float in the world with depth. Errs toward
/// keeping: anything not clearly a dimmer or a framing panel is content.
/// </summary>
public static class BackdropRules
{
    /// <summary>Full-canvas: at least this fraction of the canvas both ways (dimmers, vignettes).</summary>
    public const float FullCanvas = 0.9f;

    /// <summary>A panel covers at least this fraction of the canvas area (a 550x200 dialog on 1920x1080 is 5%);
    /// smaller backings (settings rows, item cards, a tight label backing) stay.</summary>
    public const float PanelArea = 0.04f;

    /// <summary>A panel frames at least this many graphics; one label on a backing is an element, not a panel.</summary>
    public const int PanelContent = 2;

    /// <summary>A fill covering at least this share of a hidden panel is part of its surface (header band, section
    /// frame, card shadow) and goes with it; smaller ones (dividers, a reward label) stay.</summary>
    public const float PartShare = 0.04f;

    public static BackdropRole Classify(in BackdropTraits t)
    {
        if (!t.IsImage || t.Interactive || t.LiveTexture || t.Gauge)
            return BackdropRole.Content;
        if (t.WidthFraction >= FullCanvas && t.HeightFraction >= FullCanvas)
            return BackdropRole.Dimmer;
        bool framing = t.ContentAbove >= PanelContent;
        // On a hidden panel: a flat fill, or anything content sits on (a card's shadow), goes with it. Grouped with
        // one but elsewhere on screen: only a flat fill; a picture hanging off the panel (a banner) stays.
        if (t.PanelShare >= PartShare && (t.Flat || framing))
            return BackdropRole.Panel;
        if (t.InPanelGroup && t.Flat)
            return BackdropRole.Panel;
        // Elsewhere only a plain fill counts: a drawn picture (banner, map) with marks on it is content.
        return t.Flat && framing && t.WidthFraction * t.HeightFraction >= PanelArea
            ? BackdropRole.Panel
            : BackdropRole.Content;
    }

    /// <summary>Dark text that sat on a light panel gets a light halo once the panel is gone; light text a dark
    /// one. Rec. 601 luma, so saturated yellows and cyans count as light.</summary>
    public static bool NeedsLightHalo(float r, float g, float b) =>
        0.299f * r + 0.587f * g + 0.114f * b < 0.5f;
}
