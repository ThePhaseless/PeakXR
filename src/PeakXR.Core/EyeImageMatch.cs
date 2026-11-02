using System;

namespace PeakXR.Core;

/// <summary>
/// Compares two eyes' RGBA8 images for self-tests vr.480 to vr.482 (issue #5): a difference between the eyes is either
/// parallax, which a horizontal shift explains, or something per eye, which it does not. A point at column x in the left
/// image is at column x + shift in the right one.
/// </summary>
public static class EyeImageMatch
{
    /// <summary>Summed RGB change the quad must reach outside its hole for <see cref="HoleInProfile"/> to look: half of it
    /// is still over the halo inside the hole, about 25-37 near the edge in the first in-game run (340 outside).</summary>
    public const int MinHoleContrast = 80;

    /// <summary>The hole's expected width in tangents is 1.0 (radius 1/12 of a quad of 6 tan), 1.02 to 1.04 measured at
    /// half the quad's change: anything else is not that hole.</summary>
    public const float MinHoleTan = 0.85f;

    public const float MaxHoleTan = 1.15f;

    /// <summary>Whole-eye RMS (of 255) a swapped eye may reach against the other eye's normal image: three times what the
    /// frame moves by itself, within these two. The saved pairs read 0.10 to 0.15 once the hands are out.</summary>
    public const float SwapRmsFloor = 0.5f;

    public const float SwapRmsCap = 1f;

    /// <summary>The same for the share of pixels past <see cref="DrawnPixels.ChannelStep"/>; the saved pairs read at most
    /// 0.00001 without the hands.</summary>
    public const float SwapShareFloor = 0.0005f;

    public const float SwapShareCap = 0.002f;

    /// <summary>RMS in a tile a swapped eye may reach whatever that tile does by itself. The saved pairs' worst tile
    /// without the hands reads 0.97 at the view's edge and 0.38 at p99; 128 px of the other eye's image pasted into flat
    /// smoke reads 2.3 to 3.4.</summary>
    public const float SwapTileFloor = 1.5f;

    /// <summary>Over this RMS between two normal reads the frame is not still enough to judge a swap, and with it at the
    /// limit <see cref="SwapRmsCap"/> still applies.</summary>
    public const float SwapStillLimit = 0.5f;

    /// <summary>Tile side in the boxed fields; with vr.481's step of 4 that is 64 px, 0.3 % of an eye.</summary>
    private const int SwapTile = 16;

    private const float SwapNoiseFactor = 3f;

    /// <summary>Luma (Rec. 709, 0-255) boxed down by <paramref name="step"/>; the result is w / step by h / step.</summary>
    public static float[] Luma(byte[] rgba, int w, int h, int step)
    {
        int outW = w / step;
        int outH = h / step;
        var luma = new float[outW * outH];
        float scale = 1f / (step * step);
        for (int y = 0; y < outH * step; y++)
        for (int x = 0; x < outW * step; x++)
        {
            int i = (y * w + x) * 4;
            luma[y / step * outW + x / step] +=
                (0.2126f * rgba[i] + 0.7152f * rgba[i + 1] + 0.0722f * rgba[i + 2]) * scale;
        }
        return luma;
    }

    /// <summary>Root mean square of a - b; 0 for empty or differently sized fields.</summary>
    public static float Rms(float[] a, float[] b)
    {
        if (a == null || b == null || a.Length != b.Length || a.Length == 0)
            return 0f;
        double sum = 0;
        for (int i = 0; i < a.Length; i++)
            sum += (a[i] - b[i]) * (double)(a[i] - b[i]);
        return (float)Math.Sqrt(sum / a.Length);
    }

    /// <summary>sqrt(sum (l - r shifted)^2 / sum l^2) on the overlap: 0 when the shift explains the difference, and
    /// sqrt 2 for uncorrelated zero-mean fields (about 0.7 for uncorrelated all-positive ones, so compare differences
    /// of images, not raw luma, when that matters).</summary>
    public static float Residual(float[] l, float[] r, int w, int h, int shift)
    {
        if (Math.Abs(shift) >= w)
            throw new ArgumentOutOfRangeException(nameof(shift), shift, "no overlap");
        double difference = 0;
        double mass = 0;
        for (int y = 0; y < h; y++)
        for (int x = Math.Max(0, -shift); x < Math.Min(w, w - shift); x++)
        {
            double v = l[y * w + x];
            double d = v - r[y * w + x + shift];
            difference += d * d;
            mass += v * v;
        }
        if (mass > 0)
            return (float)Math.Sqrt(difference / mass);
        return difference > 0 ? float.PositiveInfinity : 0f;
    }

    /// <summary>The shift in [min, max] with the lowest <see cref="Residual"/>; throws when any shift in the range
    /// leaves no overlap.</summary>
    public static int BestShift(float[] l, float[] r, int w, int h, int min, int max)
    {
        if (min > max || Math.Abs(min) >= w || Math.Abs(max) >= w)
            throw new ArgumentOutOfRangeException(nameof(min), $"{min}..{max} of width {w}");
        int best = min;
        float lowest = float.PositiveInfinity;
        for (int shift = min; shift <= max; shift++)
        {
            float residual = Residual(l, r, w, h, shift);
            if (residual < lowest)
            {
                lowest = residual;
                best = shift;
            }
        }
        return best;
    }

    /// <summary>|mass a - mass b| / the larger mass, mass being the sum of absolute values: 0 when both eyes got the
    /// same amount of the effect.</summary>
    public static float MassRatio(float[] a, float[] b)
    {
        double ma = Mass(a);
        double mb = Mass(b);
        double larger = Math.Max(ma, mb);
        return larger > 0 ? (float)(Math.Abs(ma - mb) / larger) : 0f;
    }

    /// <summary>The right column minus the left column of the eyes' parallel axes, in an image <paramref name="width"/>
    /// wide: the shift of something at infinity on uncanted eyes.</summary>
    public static float AxisShift(int width, in EyeView a, in EyeView b) =>
        width * (AxisColumn(b) - AxisColumn(a));

    /// <summary>
    /// Per column in [<paramref name="colFrom"/>, <paramref name="colTo"/>], the median over rows
    /// [<paramref name="rowFrom"/>, <paramref name="rowTo"/>] of the summed RGB change between the images, so scene detail
    /// on one row cannot break a hole in two. Columns are the viewport's: a target can be wider than it.
    /// </summary>
    public static int[] ChangeProfile(
        byte[] off,
        byte[] on,
        int w,
        int rowFrom,
        int rowTo,
        int colFrom,
        int colTo
    )
    {
        if (off == null || on == null || off.Length != on.Length)
            throw new ArgumentException("two images of one size are needed", nameof(on));
        var profile = new int[colTo - colFrom + 1];
        var column = new int[rowTo - rowFrom + 1];
        for (int x = colFrom; x <= colTo; x++)
        {
            for (int y = rowFrom; y <= rowTo; y++)
            {
                int i = (y * w + x) * 4;
                column[y - rowFrom] =
                    Math.Abs(off[i] - on[i])
                    + Math.Abs(off[i + 1] - on[i + 1])
                    + Math.Abs(off[i + 2] - on[i + 2]);
            }
            Array.Sort(column);
            profile[x - colFrom] = column[column.Length / 2];
        }
        return profile;
    }

    /// <summary>
    /// The first and last column of a quad's hole: the longest run under half the quad's own change, read from the
    /// profile's upper quartile, as the hole covers well under half of a row. So it needs no start column and sits above
    /// any halo the quad leaves inside the hole. (-1, -1) when the quad changed less than <paramref name="minOutside"/> or
    /// the run reaches the profile's end, where its centre is unknown.
    /// </summary>
    public static (int Left, int Right) HoleInProfile(int[] profile, int minOutside)
    {
        var sorted = (int[])profile.Clone();
        Array.Sort(sorted);
        int outside = sorted[sorted.Length * 3 / 4];
        if (outside < minOutside)
            return (-1, -1);
        int threshold = outside / 2;
        int bestFrom = -1;
        int bestLength = 0;
        int from = -1;
        for (int x = 0; x <= profile.Length; x++)
        {
            if (x < profile.Length && profile[x] < threshold)
            {
                if (from < 0)
                    from = x;
                continue;
            }
            if (from >= 0 && x - from > bestLength)
            {
                bestFrom = from;
                bestLength = x - from;
            }
            from = -1;
        }
        if (bestFrom <= 0 || bestFrom + bestLength >= profile.Length)
            return (-1, -1);
        return (bestFrom, bestFrom + bestLength - 1);
    }

    /// <summary>
    /// A swapped eye against the other eye's <paramref name="normal"/> image, judged by what the frame does with no swap:
    /// <paramref name="later"/> is a normal read after the swapped one, so it spans at least the same time. Fields are
    /// boxed luma; the shares come from <see cref="DrawnPixels.ChangedShare"/>.
    /// </summary>
    public static EyeSwapReading ReadSwap(
        float[] swapped,
        float[] normal,
        float[] later,
        int w,
        int h,
        float share,
        float stillShare
    )
    {
        if (swapped.Length != w * h || normal.Length != w * h || later.Length != w * h)
            throw new ArgumentException($"fields must be {w} by {h}", nameof(swapped));
        int past = 0;
        float worst = 0f;
        for (int y = 0; y < h; y += SwapTile)
        for (int x = 0; x < w; x += SwapTile)
        {
            float tile = TileRms(swapped, normal, w, h, x, y);
            if (tile > worst)
                worst = tile;
            if (
                tile > Math.Max(SwapTileFloor, SwapNoiseFactor * TileRms(later, normal, w, h, x, y))
            )
                past++;
        }
        return new EyeSwapReading(
            Rms(swapped, normal),
            Rms(later, normal),
            share,
            stillShare,
            past,
            worst
        );
    }

    private static float TileRms(float[] a, float[] b, int w, int h, int fromX, int fromY)
    {
        int toX = Math.Min(w, fromX + SwapTile);
        int toY = Math.Min(h, fromY + SwapTile);
        double sum = 0;
        for (int y = fromY; y < toY; y++)
        for (int x = fromX; x < toX; x++)
        {
            double d = a[y * w + x] - b[y * w + x];
            sum += d * d;
        }
        return (float)Math.Sqrt(sum / ((toX - fromX) * (toY - fromY)));
    }

    private static float AxisColumn(in EyeView eye) => -eye.Left / (eye.Right - eye.Left);

    private static double Mass(float[] field)
    {
        double sum = 0;
        foreach (var v in field)
            sum += Math.Abs(v);
        return sum;
    }
}

/// <summary>What <see cref="EyeImageMatch.ReadSwap"/> found. A stale per-eye texture shows as a tile past its own noise
/// even when the whole eye's RMS dilutes it, or as RMS or share past their limits when it covers more.</summary>
public readonly struct EyeSwapReading
{
    public readonly float Rms;
    public readonly float StillRms;
    public readonly float Share;
    public readonly float StillShare;
    public readonly int TilesPastNoise;
    public readonly float WorstTile;

    public EyeSwapReading(
        float rms,
        float stillRms,
        float share,
        float stillShare,
        int tilesPastNoise,
        float worstTile
    )
    {
        Rms = rms;
        StillRms = stillRms;
        Share = share;
        StillShare = stillShare;
        TilesPastNoise = tilesPastNoise;
        WorstTile = worstTile;
    }

    public bool Matches =>
        TilesPastNoise == 0
        && Rms <= Limit(StillRms, EyeImageMatch.SwapRmsFloor, EyeImageMatch.SwapRmsCap)
        && Share <= Limit(StillShare, EyeImageMatch.SwapShareFloor, EyeImageMatch.SwapShareCap);

    private static float Limit(float still, float floor, float cap) =>
        Math.Min(cap, Math.Max(floor, 3f * still));

    public override string ToString() =>
        FormattableString.Invariant(
            $"RMS {Rms:0.00} share {Share:0.00000}, tile {WorstTile:0.00}, {TilesPastNoise} tiles past their noise, still {StillRms:0.00} share {StillShare:0.00000}"
        );
}
