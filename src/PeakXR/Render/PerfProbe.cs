using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using BepInEx.Logging;
using PeakXR.Core;
using PeakXR.VR;
using PeakXR.XR;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace PeakXR.Render;

/// <summary>
/// A per-second line that shows where a VR frame's time goes (issue #6: about 55 fps with CPU and GPU under 80 %, so
/// the frame waits on something). The game build has no Frame Timing Stats (every logged window reads "CPU main/render
/// n/a"), so the main thread is timed here: markers around the PlayerLoop systems where it can only wait split each
/// frame into busy time and waits. TimeUpdate.WaitForLastPresentationAndUpdateTime is where the main thread waits for
/// the previous frame's present (render thread, runtime and compositor pacing), PostLateUpdate.PresentAfterDraw is the
/// present, PostLateUpdate.FinishFrameRendering is culling, render recording and submission (twice in MultiPass), and
/// EarlyUpdate.XRUpdate is the runtime's frame begin. Next to that: the runtime's GPU and compositor times, its
/// XR_META_performance_metrics counters where Unity's OpenXR provider registers them, draw-call counters, the frame cap,
/// VSync and the desktop monitor's rate (a present locked to the monitor would cap the headset), idle pacing, and a
/// verdict (Core <see cref="PerfLine"/>). Per frame it only reads timestamps; the line is built in a reused
/// StringBuilder without boxing, and logs through its own source so the bug report's 500-line ring keeps other lines.
/// </summary>
internal static class PerfProbe
{
    private static readonly double TickMs = 1000.0 / Stopwatch.Frequency;
    private static readonly StringBuilder Line = new(640);

    private static ManualLogSource log;
    private static bool installed;
    private static string missing;

    private static long waitStart;
    private static long frameStart;
    private static long renderStart;
    private static long presentStart;
    private static long xrStart;
    private static long renderTicks;
    private static long presentTicks;
    private static long xrTicks;

    private static long windowStart;
    private static int frames;
    private static double sumFrame;
    private static double sumBusy;
    private static double sumRender;
    private static double sumWait;
    private static double sumPresent;
    private static double sumXr;
    private static double sumGpu;
    private static double sumCompositor;
    private static double sumRenderThread;
    private static int gpuFrames;
    private static int compositorFrames;
    private static int renderThreadFrames;

    private static ProfilerRecorder draws;
    private static ProfilerRecorder batches;
    private static ProfilerRecorder setPass;

    /// <summary>The last window's frame count, mean frame and main-thread busy time (ms) and line, for vr.279.</summary>
    internal static int WindowFrames { get; private set; }
    internal static float WindowFrameMs { get; private set; }
    internal static float WindowBusyMs { get; private set; }
    internal static string LastLine { get; private set; }

    /// <summary>Frames measured since VR started (vr.279 checks the markers run).</summary>
    internal static int MeasuredFrames { get; private set; }

    internal static bool Installed => installed;

    /// <summary>Puts the markers into the PlayerLoop, once, at the first VR start. They return at once in flat play.</summary>
    internal static void Install()
    {
        if (installed)
            return;
        installed = true;
        try
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            var lost = new List<string>();
            Wrap(
                ref loop,
                typeof(TimeUpdate.WaitForLastPresentationAndUpdateTime),
                BeforeWait,
                AfterWait,
                lost
            );
            Wrap(
                ref loop,
                typeof(PostLateUpdate.FinishFrameRendering),
                BeforeRender,
                AfterRender,
                lost
            );
            Wrap(
                ref loop,
                typeof(PostLateUpdate.PresentAfterDraw),
                BeforePresent,
                AfterPresent,
                lost
            );
            Wrap(ref loop, typeof(EarlyUpdate.XRUpdate), BeforeXr, AfterXr, lost);
            PlayerLoop.SetPlayerLoop(loop);
            missing = lost.Count == 0 ? null : string.Join(", ", lost);
            log = BepInEx.Logging.Logger.CreateLogSource("PeakXR.Perf");
            draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            Plugin.Log.LogInfo(
                $"[render.fixes] perf line every {RenderFixesFeature.PerfLogSeconds.Value:0.#} s (log source PeakXR.Perf); "
                    + (missing == null ? "all frame markers in place" : "no marker for " + missing)
                    + $"; draw counters {(draws.Valid ? "on" : "not in this build")}"
            );
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning(
                $"[render.fixes] perf line off: PlayerLoop markers failed ({e.GetType().Name}: {e.Message})"
            );
        }
    }

    /// <summary>Inserts <paramref name="before"/> and <paramref name="after"/> around the PlayerLoop system of type
    /// <paramref name="target"/>, wherever it sits.</summary>
    private static void Wrap(
        ref PlayerLoopSystem loop,
        Type target,
        PlayerLoopSystem.UpdateFunction before,
        PlayerLoopSystem.UpdateFunction after,
        List<string> lost
    )
    {
        var phases = loop.subSystemList;
        for (int p = 0; phases != null && p < phases.Length; p++)
        {
            var systems = phases[p].subSystemList;
            int at = systems == null ? -1 : Array.FindIndex(systems, s => s.type == target);
            if (at < 0)
                continue;
            var list = new List<PlayerLoopSystem>(systems);
            list.Insert(
                at + 1,
                new PlayerLoopSystem { type = typeof(PerfProbe), updateDelegate = after }
            );
            list.Insert(
                at,
                new PlayerLoopSystem { type = typeof(PerfProbe), updateDelegate = before }
            );
            phases[p].subSystemList = list.ToArray();
            return;
        }
        lost.Add(target.Name);
    }

    private static long Now => Stopwatch.GetTimestamp();

    private static void BeforeWait() => waitStart = Now;

    private static void BeforeRender() => renderStart = Now;

    private static void AfterRender() => renderTicks += Now - renderStart;

    private static void BeforePresent() => presentStart = Now;

    private static void AfterPresent() => presentTicks += Now - presentStart;

    private static void BeforeXr() => xrStart = Now;

    private static void AfterXr() => xrTicks += Now - xrStart;

    /// <summary>The main thread starts a frame's work: the one before ends here, the wait just done included.</summary>
    private static void AfterWait()
    {
        long now = Now;
        bool on = VRSession.Active && RenderFixesFeature.PerfLogSeconds.Value > 0f;
        if (on && frameStart != 0)
        {
            double frame = (now - frameStart) * TickMs;
            double wait = (now - waitStart) * TickMs;
            double present = presentTicks * TickMs;
            double xr = xrTicks * TickMs;
            frames++;
            MeasuredFrames++;
            sumFrame += frame;
            sumWait += wait;
            sumPresent += present;
            sumXr += xr;
            sumRender += renderTicks * TickMs;
            sumBusy += Math.Max(0.0, frame - wait - present - xr);
            AddRuntimeTimes();
            if (windowStart == 0)
                windowStart = frameStart;
            if ((now - windowStart) * TickMs >= RenderFixesFeature.PerfLogSeconds.Value * 1000.0)
                Emit((now - windowStart) * TickMs);
        }
        else if (!on)
            ResetWindow();
        frameStart = now;
        renderTicks = presentTicks = xrTicks = 0;
    }

    private static void AddRuntimeTimes()
    {
        if (FrameClock.LastGpuMs > 0f)
        {
            sumGpu += FrameClock.LastGpuMs;
            gpuFrames++;
        }
        if (FrameClock.LastRenderThreadMs > 0f)
        {
            sumRenderThread += FrameClock.LastRenderThreadMs;
            renderThreadFrames++;
        }
        var d = XRRuntimeInfo.Display;
        if (d != null && d.TryGetCompositorGPUTimeLastFrame(out float c) && c > 0f)
        {
            // Providers report seconds or milliseconds; no compositor frame takes under half a millisecond.
            sumCompositor += c < 0.5f ? c * 1000f : c;
            compositorFrames++;
        }
    }

    private static void ResetWindow()
    {
        windowStart = 0;
        frames = gpuFrames = compositorFrames = renderThreadFrames = 0;
        sumFrame = sumBusy = sumRender = sumWait = sumPresent = sumXr = 0.0;
        sumGpu = sumCompositor = sumRenderThread = 0.0;
    }

    private static float Mean(double sum, int n) => n > 0 ? (float)(sum / n) : -1f;

    private static void Emit(double windowMs)
    {
        float frame = Mean(sumFrame, frames);
        float busy = Mean(sumBusy, frames);
        float gpu = Mean(sumGpu, gpuFrames);
        WindowFrames = frames;
        WindowFrameMs = frame;
        WindowBusyMs = busy;
        float hz = FrameClock.Trusted ? FrameClock.RefreshHz : 0f;
        float budget = FrameBudget.BudgetMs(hz);
        var d = XRRuntimeInfo.Display;
        int passes = d != null && d.running ? d.GetRenderPassCount() : 0;

        var sb = Line.Clear();
        sb.Append("[render.fixes] perf: ");
        PerfLine.Append(sb, (float)(frames * 1000.0 / windowMs), 1).Append(" fps of ");
        PerfLine.Append(sb, hz > 0f ? hz : -1f, 0).Append(" Hz (budget ");
        PerfLine.Append(sb, budget > 0f ? budget : -1f, 2).Append(" ms); ");
        sb.Append(
            passes == 2 ? "MultiPass"
            : passes == 1 ? "single pass"
            : "no XR passes"
        );
        sb.Append(", eyes cull ").Append(EyeCulling.Active ? "as one" : "apart");
        sb.Append("; frame ");
        PerfLine.Append(sb, frame, 2).Append(" ms = main busy ");
        PerfLine.Append(sb, busy, 2).Append(" (render submit ");
        PerfLine.Append(sb, Mean(sumRender, frames), 2).Append(", XR update ");
        PerfLine.Append(sb, Mean(sumXr, frames), 2).Append(") + compositor wait ");
        PerfLine.Append(sb, Mean(sumWait, frames), 2).Append(" + present ");
        PerfLine.Append(sb, Mean(sumPresent, frames), 2).Append("; render thread ");
        PerfLine.Append(sb, Mean(sumRenderThread, renderThreadFrames), 2).Append(" ms; GPU ");
        PerfLine.Append(sb, gpu, 2).Append(" ms, compositor GPU ");
        PerfLine.Append(sb, Mean(sumCompositor, compositorFrames), 2).Append(" ms");
        AppendStat(sb, d, "perfmetrics.appcputime", "; runtime app CPU ", " ms", 2);
        AppendStat(sb, d, "perfmetrics.appgputime", ", app GPU ", " ms", 2);
        AppendStat(sb, d, "perfmetrics.compositorcputime", ", compositor CPU ", " ms", 2);
        AppendStat(sb, d, "perfmetrics.gpuutil", ", GPU use ", " %", 0);
        sb.Append("; draws ");
        PerfLine.Append(sb, draws.Valid ? draws.LastValue : -1, 0).Append(", batches ");
        PerfLine.Append(sb, batches.Valid ? batches.LastValue : -1, 0).Append(", set-pass ");
        PerfLine.Append(sb, setPass.Valid ? setPass.LastValue : -1, 0);
        sb.Append("; cap ").Append(Application.targetFrameRate > 0 ? "on " : "off ");
        PerfLine.Append(sb, Math.Max(0, Application.targetFrameRate), 0).Append(", vsync ");
        PerfLine.Append(sb, QualitySettings.vSyncCount, 0).Append(", desktop ");
        PerfLine
            .Append(sb, (float)Screen.currentResolution.refreshRateRatio.value, 0)
            .Append(" Hz, idle pacing ");
        sb.Append(RenderDriver.IdlePacing ? "on" : "off").Append(", render scale ");
        PerfLine.Append(sb, RenderDriver.CurrentScale, 2).Append("; bound: ");
        sb.Append(PerfLine.Name(PerfLine.Judge(frame, busy, gpu, budget)));
        if (missing != null)
            sb.Append(" (no marker for ").Append(missing).Append(')');
        LastLine = sb.ToString();
        log.LogInfo(LastLine);
        ResetWindow();
    }

    /// <summary>One runtime stat by name (Unity's OpenXR provider registers XR_META_performance_metrics counters
    /// under these when the runtime offers the extension; OpenXR package MetaPerformanceMetricsTest.cs:41-54).</summary>
    private static void AppendStat(
        StringBuilder sb,
        UnityEngine.XR.XRDisplaySubsystem d,
        string stat,
        string label,
        string unit,
        int decimals
    )
    {
        if (d == null || !UnityEngine.XR.Provider.XRStats.TryGetStat(d, stat, out float v))
            return;
        sb.Append(label);
        PerfLine.Append(sb, v, decimals).Append(unit);
    }
}
