using System;

namespace PeakXR.Core;

/// <summary>
/// The menu plate's opacity from what is behind it as the eye sees it (<c>UI.MenuBackplateAdaptive</c>): just enough
/// that white text keeps <see cref="TargetContrast"/> over the brightest part of the background, so a menu over snow
/// or sky gets the full plate and one in a dark interior a light one that lets the scene show through.
/// </summary>
public static class BackdropContrast
{
    /// <summary>White text on the plate keeps at least this: above WCAG's 4.5:1 for text, with room for the
    /// background moving between two samples.</summary>
    public const float TargetContrast = 6f;

    /// <summary>The plate never thins past this, so it still groups the menu over a black background.</summary>
    public const float MinOpacity = 0.4f;

    /// <summary>Share of the samples counted as the background's bright part.</summary>
    public const float BrightShare = 0.1f;

    /// <summary>Seconds to cover most of the way to a darker plate (background brightened: text at risk) and to a
    /// lighter one.</summary>
    public const float DarkenSeconds = 0.15f;
    public const float LightenSeconds = 1f;

    /// <summary>Plate opacity for a background of relative luminance <paramref name="background"/> (0 black, 1
    /// white), at most <paramref name="maxOpacity"/> (the player's <c>UI.MenuBackplateOpacity</c>).</summary>
    public static float NeededOpacity(float background, float maxOpacity)
    {
        float plate = Contrast.Luminance(MenuBackplate.Color);
        // Luminance under the text that still gives the target ratio with white (1).
        float under = 1.05f / TargetContrast - 0.05f;
        float needed = background > under ? (background - under) / (background - plate) : 0f;
        float floor = MathF.Min(MinOpacity, maxOpacity);
        return Math.Clamp(needed, floor, maxOpacity);
    }

    /// <summary>One step from <paramref name="current"/> toward <paramref name="target"/>: fast when the plate
    /// must darken, slow when it may lighten, so a glance at the sun never leaves text unreadable.</summary>
    public static float Approach(float current, float target, float dt)
    {
        float seconds = target > current ? DarkenSeconds : LightenSeconds;
        float k = 1f - MathF.Exp(-3f * MathF.Max(dt, 0f) / seconds);
        return current + (target - current) * k;
    }

    /// <summary>Luminance histogram of sRGB samples; <see cref="Bright"/> is its top <see cref="BrightShare"/>.
    /// Allocates only when made.</summary>
    public sealed class Meter
    {
        private const int Bins = 64;
        private static readonly float[] Linear = MakeLinear();
        private readonly int[] bins = new int[Bins];
        private int count;

        public void Clear()
        {
            Array.Clear(bins, 0, Bins);
            count = 0;
        }

        public void Add(byte r, byte g, byte b)
        {
            float l = 0.2126f * Linear[r] + 0.7152f * Linear[g] + 0.0722f * Linear[b];
            bins[Math.Min(Bins - 1, (int)(l * Bins))]++;
            count++;
        }

        /// <summary>Luminance at the top <see cref="BrightShare"/> of the samples (bin upper edge); 1 with no samples,
        /// the safe side.</summary>
        public float Bright()
        {
            if (count == 0)
                return 1f;
            int left = Math.Max(1, (int)MathF.Ceiling(count * BrightShare));
            for (int i = Bins - 1; i >= 0; i--)
            {
                left -= bins[i];
                if (left <= 0)
                    return (i + 1) / (float)Bins;
            }
            return 1f / Bins;
        }

        private static float[] MakeLinear()
        {
            var t = new float[256];
            for (int i = 0; i < 256; i++)
            {
                float c = i / 255f;
                t[i] = c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
            }
            return t;
        }
    }
}
