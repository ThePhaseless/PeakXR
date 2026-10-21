using System;

namespace PeakXR.Core;

/// <summary>
/// Rounded-rectangle coverage for the soft backdrop drawn behind world-space menus: a hard-edged
/// rectangle reads as a slab pasted into the scene, a rounded one with a feathered rim blends in.
/// Coordinates are relative to the rectangle centre, in any unit (texels for the generated sprite).
/// </summary>
public static class RoundedRect
{
    /// <summary>Signed distance from (<paramref name="x"/>, <paramref name="y"/>) to the edge of a
    /// rectangle with the given half extents and corner <paramref name="radius"/>: negative inside.</summary>
    public static float Distance(float x, float y, float halfWidth, float halfHeight, float radius)
    {
        float qx = MathF.Abs(x) - (halfWidth - radius);
        float qy = MathF.Abs(y) - (halfHeight - radius);
        float ox = MathF.Max(qx, 0f),
            oy = MathF.Max(qy, 0f);
        return MathF.Sqrt(ox * ox + oy * oy) + MathF.Min(MathF.Max(qx, qy), 0f) - radius;
    }

    /// <summary>Opacity 0..1: clear on and outside the edge, opaque deeper than
    /// <paramref name="feather"/> inside, smoothstep between.</summary>
    public static float Alpha(
        float x,
        float y,
        float halfWidth,
        float halfHeight,
        float radius,
        float feather
    )
    {
        float d = Distance(x, y, halfWidth, halfHeight, radius);
        if (feather <= 0f)
            return d < 0f ? 1f : 0f;
        float t = Math.Clamp(-d / feather, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
