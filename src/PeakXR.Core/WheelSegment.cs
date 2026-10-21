using System;

namespace PeakXR.Core;

/// <summary>
/// Where a radial-wheel slice is actually drawn. Every emote and backpack slice draws the same wedge sprite
/// (UI_Radial_Segment, 1024 px) stretched over its square rect, rotated about the wheel centre; the rect's
/// corners, the hole in the middle and the gaps between slices are transparent. Its raycast graphic is a
/// 3000 px invisible mask, so without this test a pointer anywhere in a 45-degree sector, far outside the
/// wheel, would pick the slice. Measured from the sprite's alpha (&gt; 0.5): radius 0.221-0.455 of the
/// width, 0.7-42.4 degrees counter-clockwise from the slice's up.
/// </summary>
public static class WheelSegment
{
    public const float InnerFraction = 0.221f;
    public const float OuterFraction = 0.455f;
    public const float StartDegrees = 0.7f;
    public const float EndDegrees = 42.4f;

    /// <summary>A point in the middle of the drawn wedge.</summary>
    public const float MidDegrees = (StartDegrees + EndDegrees) * 0.5f;
    public const float MidFraction = (InnerFraction + OuterFraction) * 0.5f;

    /// <summary>
    /// Hand travel that starts a pick on a wheel whose drawn outer radius is <paramref name="outerRadius"/>:
    /// <paramref name="depth"/> 0 is the ring's inner edge, 1 its outer edge (clamped), so the hand never picks
    /// from the empty hole in the middle.
    /// </summary>
    public static float PickStart(float outerRadius, float depth)
    {
        float inner = outerRadius * (InnerFraction / OuterFraction);
        float t = MathF.Min(MathF.Max(depth, 0f), 1f);
        return inner + t * (outerRadius - inner);
    }

    /// <summary>True when (<paramref name="x"/>, <paramref name="y"/>), in the slice rect's local space (origin at
    /// the wheel centre, y along the slice's up), lies on the drawn wedge of a slice <paramref name="width"/> wide.</summary>
    public static bool Contains(float x, float y, float width)
    {
        if (width <= 0f)
            return false;
        float r = MathF.Sqrt(x * x + y * y) / width;
        if (r < InnerFraction || r > OuterFraction)
            return false;
        float degrees = MathF.Atan2(-x, y) * (180f / MathF.PI);
        return degrees >= StartDegrees && degrees <= EndDegrees;
    }
}
