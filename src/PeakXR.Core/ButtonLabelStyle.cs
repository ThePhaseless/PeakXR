using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Look of the controller button labels (ui.spatial): light text and a cyan button name on a dark plate dense enough
/// that both stay at 4.5:1 or better even with snow in sunlight behind it, the button name as large as its label.
/// </summary>
public static class ButtonLabelStyle
{
    /// <summary>Plate opacity behind a label; 0.85 (the menu plate) leaves the cyan name near 3.3:1 over snow.</summary>
    public const float PlateOpacity = 0.92f;

    /// <summary>The button name's size relative to its label text.</summary>
    public const float GlyphScale = 1f;

    /// <summary>The button name's colour (sRGB), also its leader line.</summary>
    public static readonly Vector3 GlyphColor = new(0.45f, 0.85f, 1f);
}
