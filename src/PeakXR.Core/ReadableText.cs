using System;

namespace PeakXR.Core;

/// <summary>
/// The shared minimum size for world-space text. What decides readability in a headset is the angle a glyph
/// spans, not its size in metres, so a label far away must be bigger than one at the hand. Text drawn for a
/// monitor (title-stage timer, speech bubbles, kiosk passes, loading text) grows until its em height meets
/// <see cref="MinDegrees"/> at the distance it is seen from, and never shrinks.
/// </summary>
public static class ReadableText
{
    /// <summary>Smallest em height (TMP font size) in degrees: about 0.65 degrees of x-height, the usual
    /// legibility floor for headset text.</summary>
    public const float MinDegrees = 1.3f;

    /// <summary>Largest <c>UI.TextScale</c>.</summary>
    public const float MaxTextScale = 2f;

    /// <summary><c>UI.TextScale</c> as applied: 1 to <see cref="MaxTextScale"/>. It only ever grows text on top of
    /// the floor, so anything under 1 (or NaN from a hand-edited config) is 1.</summary>
    public static float Scale(float textScale) =>
        float.IsNaN(textScale) ? 1f : Math.Clamp(textScale, 1f, MaxTextScale);

    /// <summary>A text size (degrees, or degrees per pixel) raised to <paramref name="floor"/>, then grown by the
    /// player's <paramref name="textScale"/>: never below the floor, linear above it.</summary>
    public static float Scaled(float size, float floor, float textScale) =>
        MathF.Max(size, floor) * Scale(textScale);

    /// <summary>Smallest whole font size, in pixels of <paramref name="degreesPerPixel"/>, that meets
    /// <see cref="MinDegrees"/>.</summary>
    public static int MinPixels(float degreesPerPixel) =>
        (int)MathF.Ceiling(MinDegrees / MathF.Max(degreesPerPixel, 1e-4f) - 1e-4f);

    /// <summary>Angle in degrees that <paramref name="height"/> metres spans at <paramref name="distance"/>;
    /// 180 when the eye is on it.</summary>
    public static float Degrees(float height, float distance) =>
        distance > 1e-4f ? 2f * MathF.Atan(height * 0.5f / distance) * UnityQuat.Rad2Deg : 180f;

    /// <summary>Scale (1 to <paramref name="maxScale"/>) that brings text <paramref name="height"/> metres tall
    /// at <paramref name="distance"/> up to <see cref="MinDegrees"/>. 1 for empty or degenerate input.</summary>
    public static float Grow(float height, float distance, float maxScale)
    {
        if (height <= 1e-6f || distance <= 1e-4f)
            return 1f;
        float needed = 2f * distance * MathF.Tan(MinDegrees * 0.5f * UnityQuat.Deg2Rad);
        return Math.Clamp(needed / height, 1f, MathF.Max(1f, maxScale));
    }

    /// <summary>Median of the first <paramref name="count"/> values, the body text size of a group so one
    /// footnote or heading does not decide it. Sorts that part of the array in place; 0 when empty.</summary>
    public static float Median(float[] values, int count)
    {
        if (values == null || count <= 0)
            return 0f;
        count = Math.Min(count, values.Length);
        Array.Sort(values, 0, count);
        int mid = count / 2;
        return count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) * 0.5f;
    }
}
