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
    /// <summary>One frame's times (ms, 0 when not reported).</summary>
    internal readonly struct Sample
    {
        public readonly float Interval;
        public readonly float Gpu;
        public readonly float CpuMain;
        public readonly float CpuRender;

        public Sample(float interval, float gpu, float cpuMain, float cpuRender)
        {
            Interval = interval;
            Gpu = gpu;
            CpuMain = cpuMain;
            CpuRender = cpuRender;
        }
    }

    /// <summary>One measuring window: the periodic log, the self-test step, the adaptive quality window.</summary>
    internal sealed class Window
    {
        public readonly FrameRing Interval = new();
        public readonly FrameRing Gpu = new();
        public readonly FrameRing CpuMain = new();
        public readonly FrameRing CpuRender = new();
        public int Dropped;
        public int Repeated;
        public bool RuntimeCounts;

        /// <summary>Frames this window saw; its rings keep only their last <see cref="FrameRing.DefaultCapacity"/>.</summary>
        public int Frames;
        private float started;

        public float Seconds => Time.unscaledTime - started;

        public void Reset()
        {
            Interval.Clear();
            Gpu.Clear();
            CpuMain.Clear();
            CpuRender.Clear();
            Dropped = Repeated = Frames = 0;
            RuntimeCounts = false;
            started = Time.unscaledTime;
        }

        public void Add(in Sample s, bool counts, int dropped, int repeated)
        {
            Frames++;
            Interval.Add(s.Interval);
            Gpu.Add(s.Gpu);
            CpuMain.Add(s.CpuMain);
            CpuRender.Add(s.CpuRender);
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
        public readonly FrameSummary CpuMain;
        public readonly FrameSummary CpuRender;
        public readonly int Dropped;
        public readonly int Repeated;
        public readonly float Seconds;
        public readonly int Frames;

        /// <summary>The rate is the headset's: only then may anything act on <see cref="BudgetMs"/>.</summary>
        public readonly bool Trusted;

        public Report(Window w, float hz, bool trusted)
        {
            Hz = hz;
            Trusted = trusted;
            Frames = w.Frames;
            BudgetMs = FrameBudget.BudgetMs(hz);
            Interval = w.Interval.Summarize(BudgetMs * FrameBudget.DroppedFactor);
            Gpu = w.Gpu.Summarize(0f);
            CpuMain = w.CpuMain.Summarize(0f);
            CpuRender = w.CpuRender.Summarize(0f);
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
            string cpu = CpuMain.Empty
                ? "n/a"
                : FormattableString.Invariant($"{CpuMain.P95:0.00}/{CpuRender.P95:0.00} ms");
            string runtime =
                Dropped < 0 ? "n/a" : FormattableString.Invariant($"{Dropped}/{Repeated}");
            // A long log window at a high rate outgrows the ring: say which frames the percentiles cover.
            string covers =
                Interval.Count < Frames
                    ? FormattableString.Invariant($" (last {Interval.Count} of {Frames} frames)")
                    : "";
            return FormattableString.Invariant(
                $"refresh {refresh}, frame p50/p95/p99{covers} {Interval.P50:0.00}/{Interval.P95:0.00}/{Interval.P99:0.00} ms, over 1.5x budget {Interval.Over}/{Interval.Count}, GPU p50/p95 {gpu}, CPU main/render p95 {cpu}, runtime dropped/repeated {runtime}, render scale {renderScale:0.00}, {Seconds:0.0} s"
            );
        }
    }

    public static readonly Window Log = new();
    public static readonly Window Step = new();
    public static readonly Window Adapt = new();

    private static readonly RefreshTracker Refresh = new();
    private static readonly RefreshLogGate LogGate = new();

    /// <summary>The refresh rate for budgets (<see cref="RefreshTracker"/>); 0 when no source gives a real one.</summary>
    public static float RefreshHz => Refresh.Hz;

    /// <summary>Where <see cref="RefreshHz"/> came from.</summary>
    public static RefreshSource Source => Refresh.Source;

    /// <summary>The rate is the headset's (not the desktop monitor's): budgets may drive adaptive quality, dynamic
    /// resolution and the self-test's verdict.</summary>
    public static bool Trusted => Refresh.Trusted;

    private static float displayHz;

    /// <summary>
    /// Sources in order: the display subsystem, <c>XRDevice.refreshRate</c>
    /// (https://docs.unity3d.com/6000.3/Documentation/ScriptReference/XR.XRDevice-refreshRate.html), the session's
    /// rate while the runtime reports a throttled fraction of it, then the desktop monitor for reports only
    /// (https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Screen-currentResolution.html). Logged when the
    /// chosen rate changes.
    /// </summary>
    private static void PickRefresh()
    {
        float device = FrameBudget.Sane(displayHz) ? 0f : DeviceHz();
        // The monitor is read only when no headset rate is known (an internal call, not needed every frame).
        float screen =
            FrameBudget.Sane(displayHz) || FrameBudget.Sane(device) || Refresh.Trusted
                ? 0f
                : (float)Screen.currentResolution.refreshRateRatio.value;
        float hz = Refresh.Update(displayHz, device, screen);
        if (!LogGate.Due(hz, Refresh.Source, Time.unscaledTime))
            return;
        Plugin.Log.LogInfo(
            FormattableString.Invariant(
                $"[render.fixes] refresh {hz:0.#} Hz from {Refresh.Source}{(Refresh.Trusted ? "" : " (not the headset: nothing adapts to it)")} (display reported {displayHz:0.#} Hz, device {device:0.#} Hz, screen {screen:0.#} Hz)"
            )
        );
    }

#pragma warning disable CS0618 // Obsolete in favour of the display subsystem; it is only the backup source here.
    private static float DeviceHz() => UnityEngine.XR.XRDevice.refreshRate;
#pragma warning restore CS0618

    private static readonly UnityEngine.FrameTiming[] Timings = new UnityEngine.FrameTiming[1];

    public static Report Summarize(Window w) => new(w, RefreshHz, Trusted);

    public static void ResetAll()
    {
        Refresh.Reset();
        Log.Reset();
        Step.Reset();
        Adapt.Reset();
    }

    /// <summary>Once per frame while VR renders.</summary>
    public static void Tick()
    {
        var d = XRRuntimeInfo.Display;
        float interval = Time.unscaledDeltaTime * 1000f;
        float ftmGpu = 0f;
        float cpuMain = 0f;
        float cpuRender = 0f;
        // CPU thread times tell a CPU-bound frame from a GPU-bound one when the runtime halves the rate.
        if (FrameTimingManager.IsFeatureEnabled())
        {
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, Timings) > 0)
            {
                ftmGpu = (float)Timings[0].gpuFrameTime;
                cpuMain = (float)Timings[0].cpuMainThreadFrameTime;
                cpuRender = (float)Timings[0].cpuRenderThreadFrameTime;
            }
        }
        var sample = new Sample(interval, GpuMs(d, ftmGpu), cpuMain, cpuRender);
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
        Log.Add(sample, counts, dropped, repeated);
        Step.Add(sample, counts, dropped, repeated);
        Adapt.Add(sample, counts, dropped, repeated);
    }

    /// <summary>App GPU time (ms), or 0: the display provider's, else <c>FrameTimingManager</c>'s, which needs the
    /// "Frame Timing Stats" player setting a shipped game may lack.</summary>
    private static float GpuMs(UnityEngine.XR.XRDisplaySubsystem d, float frameTimingGpu)
    {
        // Providers report seconds or milliseconds; no real GPU frame takes under half a millisecond.
        if (d != null && d.TryGetAppGPUTimeLastFrame(out float g) && g > 0f)
            return g < 0.5f ? g * 1000f : g;
        return frameTimingGpu;
    }
}
