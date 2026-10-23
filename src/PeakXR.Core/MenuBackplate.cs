using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// The dark rounded plate behind a VR menu. A floating menu over sand, snow or sky leaves light text at about 1.7:1;
/// a plate sized to what the menu draws (plus a margin) keeps it readable without bringing back the game's 16:9
/// monitor panel. All sizes are canvas pixels.
/// </summary>
public static class MenuBackplate
{
    /// <summary>Space between the content's outer edge and the plate's.</summary>
    public const float MarginPixels = 24f;

    /// <summary>Corner radius of the plate.</summary>
    public const float CornerPixels = 24f;

    /// <summary>Default <c>UI.MenuBackplateOpacity</c>: the lowest round value that keeps white text at 4.5:1 or more
    /// over a white background (<see cref="WorstContrast"/>).</summary>
    public const float DefaultOpacity = 0.85f;

    /// <summary>Plate colour (sRGB). Every channel stays above the title's black-silhouette cut (0.05).</summary>
    public static readonly Vector3 Color = new(0.07f, 0.08f, 0.1f);

    /// <summary>The plate around content spanning <paramref name="min"/>..<paramref name="max"/>. False when nothing
    /// was drawn (min past max).</summary>
    public static bool Fit(
        Vector2 min,
        Vector2 max,
        float margin,
        out Vector2 center,
        out Vector2 size
    )
    {
        center = size = default;
        if (max.X <= min.X || max.Y <= min.Y)
            return false;
        center = (min + max) * 0.5f;
        size = max - min + new Vector2(2f * margin);
        return true;
    }

    /// <summary>Each plate edge lies <paramref name="margin"/> outside the content's, within
    /// <paramref name="tolerance"/>.</summary>
    public static bool FitsContent(
        Vector2 center,
        Vector2 size,
        Vector2 contentMin,
        Vector2 contentMax,
        float margin,
        float tolerance
    )
    {
        var lo = center - size * 0.5f;
        var hi = center + size * 0.5f;
        return MathF.Abs(contentMin.X - margin - lo.X) <= tolerance
            && MathF.Abs(contentMin.Y - margin - lo.Y) <= tolerance
            && MathF.Abs(contentMax.X + margin - hi.X) <= tolerance
            && MathF.Abs(contentMax.Y + margin - hi.Y) <= tolerance;
    }

    /// <summary>The plate covers most of the canvas both ways: the full-screen panel it must not become.</summary>
    public static bool IsFullPanel(Vector2 size, Vector2 canvas) =>
        size.X >= BackdropRules.FullCanvas * canvas.X
        && size.Y >= BackdropRules.FullCanvas * canvas.Y;

    /// <summary>WCAG contrast of white text on the plate at <paramref name="opacity"/> over a white background, the
    /// worst case (sun on snow). Blended in linear light, as URP does.</summary>
    public static float WorstContrast(float opacity)
    {
        float plate =
            0.2126f * Linear(Color.X) + 0.7152f * Linear(Color.Y) + 0.0722f * Linear(Color.Z);
        float under = opacity * plate + (1f - opacity);
        return 1.05f / (under + 0.05f);
    }

    private static float Linear(float c) =>
        c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
}
