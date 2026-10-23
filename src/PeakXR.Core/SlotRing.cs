using System;

namespace PeakXR.Core;

/// <summary>
/// The belt slot ring's texture profile: a white ring with a faint white middle, and a thin dark band just outside
/// the ring so it still reads against snow and bright sky. The white ring is composited over the band.
/// </summary>
public static class SlotRing
{
    /// <summary>Centre of the white ring, as a share of the texture's half width.</summary>
    public const float RingRadius = 0.86f;

    /// <summary>Centre of the dark band.</summary>
    public const float OutlineRadius = 0.955f;

    private const float RingHalf = 0.07f;
    private const float OutlineHalf = 0.035f;
    private const float OutlineAlpha = 0.8f;
    private const float FillInside = 0.78f;
    private const float FillAlpha = 0.18f;
    private const float Dark = 0.08f;

    /// <summary>Grey <paramref name="value"/> (0 black, 1 white) and <paramref name="alpha"/> at distance
    /// <paramref name="d"/> from the centre (1 = the texture's edge).</summary>
    public static void Texel(float d, out float value, out float alpha)
    {
        float ring = Math.Clamp(1f - MathF.Abs(d - RingRadius) / RingHalf, 0f, 1f);
        float white = MathF.Max(ring, d < FillInside ? FillAlpha : 0f);
        float band =
            OutlineAlpha * Math.Clamp(1f - MathF.Abs(d - OutlineRadius) / OutlineHalf, 0f, 1f);
        float under = band * (1f - white);
        alpha = white + under;
        value = alpha > 0f ? (white + under * Dark) / alpha : 1f;
    }
}
