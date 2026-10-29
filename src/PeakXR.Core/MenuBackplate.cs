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

    /// <summary>Cuts a part's <paramref name="min"/>..<paramref name="max"/> to a mask's rect above it: a Mask or
    /// RectMask2D draws its children only inside itself, whatever their own rects. False when nothing is left.</summary>
    public static bool ClipTo(ref Vector2 min, ref Vector2 max, Vector2 clipMin, Vector2 clipMax)
    {
        min = Vector2.Max(min, clipMin);
        max = Vector2.Min(max, clipMax);
        return max.X > min.X && max.Y > min.Y;
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
        return FitsSpan(lo.X, hi.X, contentMin.X, contentMax.X, margin, tolerance)
            && FitsSpan(lo.Y, hi.Y, contentMin.Y, contentMax.Y, margin, tolerance);
    }

    /// <summary>Along one axis: the plate's ends lie <paramref name="margin"/> outside the part's, within
    /// <paramref name="tolerance"/>. Checks a plate against one named part (the stamina bar's frame) along its length
    /// when other parts set its height.</summary>
    public static bool FitsSpan(
        float plateLo,
        float plateHi,
        float partLo,
        float partHi,
        float margin,
        float tolerance
    ) =>
        MathF.Abs(partLo - margin - plateLo) <= tolerance
        && MathF.Abs(partHi + margin - plateHi) <= tolerance;

    /// <summary>A <paramref name="plate"/> within <paramref name="tolerance"/> of the room its content's groups
    /// <paramref name="reserved"/> plus the margin, both ways: fitted to the groups' rects (which keep room for parts
    /// that are mostly hidden) instead of shrinking to what is drawn.</summary>
    public static bool Unshrunk(Vector2 plate, Vector2 reserved, float margin, float tolerance) =>
        plate.X >= reserved.X + (2f * margin) - tolerance
        && plate.Y >= reserved.Y + (2f * margin) - tolerance;

    /// <summary>The plate covers most of the canvas both ways while what the menu draws, plus the margin, does not:
    /// a 16:9 screen panel instead of a plate fitted to the content. A settings window whose tabs, rail and rows
    /// really fill the canvas gets a canvas-sized plate and is not one (VR run 47: 1705x991 px drawn).</summary>
    public static bool IsFullPanel(Vector2 plate, Vector2 content, float margin, Vector2 canvas)
    {
        var full = BackdropRules.FullCanvas * canvas;
        var drawn = content + new Vector2(2f * margin);
        return plate.X >= full.X && plate.Y >= full.Y && (drawn.X < full.X || drawn.Y < full.Y);
    }

    /// <summary>WCAG contrast of white text on the plate at <paramref name="opacity"/> over a white background, the
    /// worst case (sun on snow). Blended in linear light, as URP does.</summary>
    public static float WorstContrast(float opacity) => Contrast.Ratio(1f, WorstUnder(opacity));

    /// <summary>Luminance under text on the plate at <paramref name="opacity"/> with a white background behind
    /// it, the worst case.</summary>
    public static float WorstUnder(float opacity) =>
        opacity * Contrast.Luminance(Color) + (1f - opacity);

    /// <summary>Widest a plate may look: past this the content is scattered over the view (the title's sign, logo and
    /// credits) and one plate around it all would be the full-view 2D panel, so none is drawn.</summary>
    public const float MaxSpanDegrees = 70f;

    /// <summary>A plate <paramref name="width"/> by <paramref name="height"/> metres at <paramref name="distance"/>
    /// from the eye would span more than <see cref="MaxSpanDegrees"/> either way (or the eye is on it).</summary>
    public static bool Scattered(float width, float height, float distance) =>
        distance <= 1e-3f || SpanDegrees(MathF.Max(width, height), distance) > MaxSpanDegrees;

    /// <summary>Angle a span of <paramref name="size"/> metres covers, centred at <paramref name="distance"/>.</summary>
    public static float SpanDegrees(float size, float distance) =>
        2f * MathF.Atan(size * 0.5f / distance) * (180f / MathF.PI);

    /// <summary>Seed for <see cref="Fold"/>.</summary>
    public const int FoldStart = 17;

    /// <summary>Step, in canvas pixels, of the positions and sizes <see cref="Fold"/> keys on: about one headset
    /// pixel on the wrist at reading distance and well inside a plate's margin, so a plate up to one step stale still
    /// covers its content. Finer steps re-measured nearly every frame while the stamina fill animated.</summary>
    public const float FoldPixels = 4f;

    /// <summary>Folds one part of a plate's content into a change signature: whether it is drawn and, if so, its
    /// position and size in <see cref="FoldPixels"/> steps. A plate refitted only when the signature changes follows parts that
    /// appear, vanish, move or resize, without measuring world corners every frame or reacting to hidden parts.</summary>
    public static int Fold(int hash, bool drawn, float x, float y, float width, float height)
    {
        unchecked
        {
            hash = hash * 31 + (drawn ? 1 : 0);
            if (!drawn)
                return hash;
            hash = hash * 31 + Step(x);
            hash = hash * 31 + Step(y);
            hash = hash * 31 + Step(width);
            return hash * 31 + Step(height);
        }
    }

    private static int Step(float v) => (int)MathF.Floor(v / FoldPixels);
}
