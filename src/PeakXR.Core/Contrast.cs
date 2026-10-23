using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>WCAG 2 contrast of sRGB colours (0..1 per channel). Text needs <see cref="MinText"/> against what it sits
/// on; the game's cream dropdown box with grey text measures about 1.7:1.</summary>
public static class Contrast
{
    public const float MinText = 4.5f;

    /// <summary>Relative luminance of an sRGB colour.</summary>
    public static float Luminance(Vector3 srgb) =>
        0.2126f * Linear(srgb.X) + 0.7152f * Linear(srgb.Y) + 0.0722f * Linear(srgb.Z);

    public static float Ratio(Vector3 a, Vector3 b) => Ratio(Luminance(a), Luminance(b));

    public static float Ratio(float luminanceA, float luminanceB) =>
        (MathF.Max(luminanceA, luminanceB) + 0.05f) / (MathF.Min(luminanceA, luminanceB) + 0.05f);

    /// <summary>Black or white, whichever reads better on <paramref name="box"/>. One of them always reaches 4.58:1
    /// (the worst box, at luminance 0.18, gets that from both).</summary>
    public static Vector3 TextFor(Vector3 box) =>
        Ratio(Vector3.Zero, box) >= Ratio(Vector3.One, box) ? Vector3.Zero : Vector3.One;

    private static float Linear(float c) =>
        c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
}
