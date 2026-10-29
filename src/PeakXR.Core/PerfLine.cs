using System;
using System.Text;

namespace PeakXR.Core;

/// <summary>What holds a frame past the headset's budget, as the per-second perf line reads it.</summary>
public enum FrameBound
{
    /// <summary>Frames arrive within the budget.</summary>
    OnRate,

    /// <summary>The main thread's own work (scripts, culling, render recording) takes the budget.</summary>
    MainThread,

    /// <summary>The GPU time the runtime reports takes the budget.</summary>
    Gpu,

    /// <summary>Neither does: the main thread waits on the render thread, the compositor or a present.</summary>
    Waiting,

    /// <summary>No headset rate to judge against.</summary>
    Unknown,
}

/// <summary>
/// Pieces of the per-second perf line (render.fixes): fixed-point numbers appended digit by digit, so the line is
/// built in a reused <see cref="StringBuilder"/> with no boxing or number formatting, and the verdict on where the frame
/// goes. A frame over budget with nothing saturated is the trap issue #6 describes: CPU and GPU both under 80 % while
/// the runtime shows a fraction of the refresh.
/// </summary>
public static class PerfLine
{
    /// <summary>A frame up to this share over the budget still counts as on rate (averages carry the odd miss).</summary>
    public const float OnRateSlack = 1.1f;

    public static FrameBound Judge(float frameMs, float mainBusyMs, float gpuMs, float budgetMs)
    {
        if (!(budgetMs > 0f))
            return FrameBound.Unknown;
        if (!(frameMs > OnRateSlack * budgetMs))
            return FrameBound.OnRate;
        float limit = FramePacing.GpuShare * budgetMs;
        bool main = mainBusyMs > limit;
        bool gpu = gpuMs > limit;
        if (main && gpu)
            return mainBusyMs >= gpuMs ? FrameBound.MainThread : FrameBound.Gpu;
        if (main)
            return FrameBound.MainThread;
        return gpu ? FrameBound.Gpu : FrameBound.Waiting;
    }

    public static string Name(FrameBound bound) =>
        bound switch
        {
            FrameBound.OnRate => "on rate",
            FrameBound.MainThread => "main thread",
            FrameBound.Gpu => "GPU",
            FrameBound.Waiting => "waiting (render thread, compositor or present)",
            _ => "not judged (no headset rate)",
        };

    /// <summary>Appends <paramref name="value"/> with <paramref name="decimals"/> digits after the point, or "n/a" for a
    /// negative or non-finite value (a source that reported nothing).</summary>
    public static StringBuilder Append(StringBuilder sb, float value, int decimals)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            return sb.Append("n/a");
        long scale = 1;
        for (int i = 0; i < decimals; i++)
            scale *= 10;
        long scaled = (long)Math.Round((double)value * scale, MidpointRounding.AwayFromZero);
        AppendDigits(sb, scaled / scale);
        if (decimals <= 0)
            return sb;
        sb.Append('.');
        long frac = scaled % scale;
        for (long d = scale / 10; d > 0; d /= 10)
        {
            sb.Append((char)('0' + (int)(frac / d)));
            frac %= d;
        }
        return sb;
    }

    static void AppendDigits(StringBuilder sb, long value)
    {
        long d = 1;
        while (value / d >= 10)
            d *= 10;
        for (; d > 0; d /= 10)
        {
            sb.Append((char)('0' + (int)(value / d)));
            value %= d;
        }
    }
}
