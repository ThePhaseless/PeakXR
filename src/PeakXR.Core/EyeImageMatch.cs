using System;

namespace PeakXR.Core;

/// <summary>
/// Compares two eyes' RGBA8 images for self-tests vr.480 to vr.482 (issue #5): a difference between the eyes is either
/// parallax, which a horizontal shift explains, or something per eye, which it does not. A point at column x in the left
/// image is at column x + shift in the right one.
/// </summary>
public static class EyeImageMatch
{
    /// <summary>Pixels in a row that must all change for <see cref="HoleEdges"/> to call it an edge, so one sparkle
    /// inside the hole is not taken for one.</summary>
    private const int EdgeRun = 5;

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
    /// From column <paramref name="start"/> on <paramref name="row"/>, walks left and right to the first run of
    /// <see cref="EdgeRun"/> pixels whose summed RGB change between the images is at least <paramref name="threshold"/>,
    /// and returns the run's end nearest the start. A side with no such run returns -1.
    /// </summary>
    public static (int Left, int Right) HoleEdges(
        byte[] off,
        byte[] on,
        int w,
        int row,
        int start,
        int threshold
    )
    {
        if (
            off == null
            || on == null
            || off.Length != on.Length
            || start < 0
            || start >= w
            || row < 0
            || (row + 1) * w * 4 > off.Length
        )
            return (-1, -1);
        return (
            Walk(off, on, w, row, start, threshold, -1),
            Walk(off, on, w, row, start, threshold, 1)
        );
    }

    private static int Walk(
        byte[] off,
        byte[] on,
        int w,
        int row,
        int start,
        int threshold,
        int dir
    )
    {
        int run = 0;
        for (int x = start; x >= 0 && x < w; x += dir)
        {
            int i = (row * w + x) * 4;
            int change =
                Math.Abs(off[i] - on[i])
                + Math.Abs(off[i + 1] - on[i + 1])
                + Math.Abs(off[i + 2] - on[i + 2]);
            run = change >= threshold ? run + 1 : 0;
            if (run == EdgeRun)
                return x - (EdgeRun - 1) * dir;
        }
        return -1;
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
