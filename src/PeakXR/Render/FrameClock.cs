using System;
using PeakXR.Core;
using PeakXR.XR;
using UnityEngine;

namespace PeakXR.Render;

/// <summary>
/// Frame-time evidence for VR play and the self-test. Each frame records the frame interval
/// (<c>Time.unscaledDeltaTime</c>: VSync is off and the XR runtime paces frames, so a missed vsync shows as a doubled
/// interval), the app GPU time when the display provider or <c>FrameTimingManager</c> reports it, and the runtime's
/// dropped and re-presented frame counts. Recording is allocation-free; summaries sort a copy and run only at report
/// time. APIs: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/XR.XRDisplaySubsystem.html,
/// https://docs.unity3d.com/6000.3/Documentation/ScriptReference/FrameTimingManager.html
/// </summary>
internal static class FrameClock
{
    /// <summary>One measuring window: the periodic log, the self-test step, the adaptive quality window.</summary>
    internal sealed class Window
    {
        public readonly FrameRing Interval = new();
        public readonly FrameRing Gpu = new();
        public int Dropped;
        public int Repeated;
        public bool RuntimeCounts;
        private float started;

        public float Seconds => Time.unscaledTime - started;

        public void Reset()
        {
            Interval.Clear();
            Gpu.Clear();
            Dropped = Repeated = 0;
            RuntimeCounts = false;
            started = Time.unscaledTime;
        }

        public void Add(float interval, float gpu, bool counts, int dropped, int repeated)
        {
            Interval.Add(interval);
            Gpu.Add(gpu);
            RuntimeCounts |= counts;
            Dropped += dropped;
            Repeated += repeated;
        }
    }

    /// <summary>A window's summary: refresh, budget, interval and GPU percentiles (ms), the runtime's counts
    /// (-1 when it reports none) and the window length.</summary>
    internal readonly struct Report
    {
        public readonly float Hz;
        public readonly float BudgetMs;
        public readonly FrameSummary Interval;
        public readonly FrameSummary Gpu;
        public readonly int Dropped;
        public readonly int Repeated;
        public readonly float Seconds;

        public Report(Window w, float hz)
        {
            Hz = hz;
            BudgetMs = FrameBudget.BudgetMs(hz);
            Interval = w.Interval.Summarize(BudgetMs * FrameBudget.DroppedFactor);
            Gpu = w.Gpu.Summarize(0f);
            Dropped = w.RuntimeCounts ? w.Dropped : -1;
            Repeated = w.RuntimeCounts ? w.Repeated : -1;
            Seconds = w.Seconds;
        }

        public string Line(float renderScale)
        {
            string refresh =
                Hz > 0f
                    ? FormattableString.Invariant(
                        $"{Hz:0.#} Hz ({Source}), budget {BudgetMs:0.00} ms"
                    )
                    : "n/a";
            string gpu = Gpu.Empty
                ? "n/a"
                : FormattableString.Invariant($"{Gpu.P50:0.00}/{Gpu.P95:0.00} ms");
            string runtime =
                Dropped < 0 ? "n/a" : FormattableString.Invariant($"{Dropped}/{Repeated}");
            return FormattableString.Invariant(
                $"refresh {refresh}, frame p50/p95/p99 {Interval.P50:0.00}/{Interval.P95:0.00}/{Interval.P99:0.00} ms, over 1.5x budget {Interval.Over}/{Interval.Count}, GPU p50/p95 {gpu}, runtime dropped/repeated {runtime}, render scale {renderScale:0.00}, {Seconds:0.0} s"
            );
        }
    }

    public static readonly Window Log = new();
    public static readonly Window Step = new();
    public static readonly Window Adapt = new();

    /// <summary>The headset's refresh rate, sanity-checked (<see cref="FrameBudget.PickRefresh"/>); 0 when no source
    /// gives a real one.</summary>
    public static float RefreshHz { get; private set; }

    /// <summary>Where <see cref="RefreshHz"/> came from.</summary>
    public static RefreshSource Source { get; private set; }

    private static float displayHz;
    private static float lastGoodHz;

    /// <summary>
    /// SteamVR's OpenXR runtime reports the throttled rate (run 57 over ALVR: a 72 Hz headset read as 36, 24 and 18 Hz),
    /// so a rate outside 60-240 Hz is unknown and the next source is tried: <c>XRDevice.refreshRate</c>
    /// (https://docs.unity3d.com/6000.3/Documentation/ScriptReference/XR.XRDevice-refreshRate.html), the last sane
    /// headset reading, then the desktop monitor
    /// (https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Screen-currentResolution.html). Logged when the
    /// chosen rate changes.
    /// </summary>
    private static void PickRefresh()
    {
        float device = FrameBudget.Sane(displayHz) ? 0f : DeviceHz();
        float screen =
            FrameBudget.Sane(displayHz) || FrameBudget.Sane(device) || FrameBudget.Sane(lastGoodHz)
                ? 0f
                : (float)Screen.currentResolution.refreshRateRatio.value;
        float hz = FrameBudget.PickRefresh(displayHz, device, lastGoodHz, screen, out var source);
        if (source is RefreshSource.Display or RefreshSource.Device)
            lastGoodHz = hz;
        Source = source;
        if (hz == RefreshHz)
            return;
        RefreshHz = hz;
        Plugin.Log.LogInfo(
            FormattableString.Invariant(
                $"[render.fixes] refresh {hz:0.#} Hz from {source} (display reported {displayHz:0.#} Hz, device {device:0.#} Hz, screen {screen:0.#} Hz)"
            )
        );
    }

#pragma warning disable CS0618 // Obsolete in favour of the display subsystem; it is only the backup source here.
    private static float DeviceHz() => UnityEngine.XR.XRDevice.refreshRate;
#pragma warning restore CS0618

    private static readonly UnityEngine.FrameTiming[] Timings = new UnityEngine.FrameTiming[1];

    public static Report Summarize(Window w) => new(w, RefreshHz);

    public static void ResetAll()
    {
        lastGoodHz = 0f;
        Log.Reset();
        Step.Reset();
        Adapt.Reset();
    }

    /// <summary>Once per frame while VR renders.</summary>
    public static void Tick()
    {
        var d = XRRuntimeInfo.Display;
        float interval = Time.unscaledDeltaTime * 1000f;
        float gpu = GpuMs(d);
        bool counts = false;
        int dropped = 0;
        int repeated = 0;
        if (d != null)
        {
            displayHz = d.TryGetDisplayRefreshRate(out float hz) ? hz : 0f;
            // Frames dropped since the last update, and how often the current frame was presented (2+ = reprojected).
            if (d.TryGetDroppedFrameCount(out int dr))
            {
                counts = true;
                dropped = Math.Max(0, dr);
            }
            if (d.TryGetFramePresentCount(out int presents))
            {
                counts = true;
                repeated = Math.Max(0, presents - 1);
            }
        }
        else
            displayHz = 0f;
        PickRefresh();
        Log.Add(interval, gpu, counts, dropped, repeated);
        Step.Add(interval, gpu, counts, dropped, repeated);
        Adapt.Add(interval, gpu, counts, dropped, repeated);
    }

    /// <summary>App GPU time (ms), or 0. FrameTimingManager needs the "Frame Timing Stats" player setting, which a
    /// shipped game may lack, so it is only the fallback.</summary>
    private static float GpuMs(UnityEngine.XR.XRDisplaySubsystem d)
    {
        // Providers report seconds or milliseconds; no real GPU frame takes under half a millisecond.
        if (d != null && d.TryGetAppGPUTimeLastFrame(out float g) && g > 0f)
            return g < 0.5f ? g * 1000f : g;
        if (!FrameTimingManager.IsFeatureEnabled())
            return 0f;
        FrameTimingManager.CaptureFrameTimings();
        return FrameTimingManager.GetLatestTimings(1, Timings) > 0
            ? (float)Timings[0].gpuFrameTime
            : 0f;
    }
}
