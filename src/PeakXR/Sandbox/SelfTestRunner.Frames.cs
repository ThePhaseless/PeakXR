using System;
using System.Collections;
using PeakXR.Core;
using PeakXR.Render;
using PeakXR.VR;

namespace PeakXR.Sandbox;

/// <summary>Frame timing in the self-test: one stats line per step, a run summary and <c>vr.231-frame-budget</c>.</summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>Seconds <c>vr.231</c> stands still and measures.</summary>
    const float FrameBudgetSeconds = 5f;

    int frameSteps;
    int frameStepsOver;
    float worstFrameRatio;
    string worstFrameStep;

    /// <summary>Logs the step's frame stats (its window reset when the step began) and keeps the worst p95 for the
    /// summary. Steps without VR or without samples log nothing.</summary>
    void LogStepFrames(string id)
    {
        if (!VRSession.Active)
            return;
        var r = FrameClock.Summarize(FrameClock.Step);
        if (r.Interval.Empty)
            return;
        Plugin.Log.LogInfo($"[selftest] frames {id}: {r.Line(RenderDriver.CurrentScale)}");
        if (r.BudgetMs <= 0f || !r.Trusted)
            return;
        frameSteps++;
        float ratio = r.Interval.P95 / r.BudgetMs;
        if (ratio > FrameBudget.SelfTestMargin)
            frameStepsOver++;
        if (ratio > worstFrameRatio)
        {
            worstFrameRatio = ratio;
            worstFrameStep = id;
        }
    }

    string FramesSummary() =>
        frameSteps == 0
            ? "frames: no timing (no VR, or the runtime reported no refresh rate)"
            : FormattableString.Invariant(
                $"frames: {frameStepsOver} of {frameSteps} steps had a p95 frame over {FrameBudget.SelfTestMargin:0.0}x budget; worst {worstFrameRatio:0.00}x in {worstFrameStep}"
            );

    /// <summary>vr.231: standing still in the level, the p95 frame interval over <see cref="FrameBudgetSeconds"/> against
    /// the display's budget times <see cref="FrameBudget.SelfTestMargin"/>. Over budget fails, or only reports (PASS with
    /// the numbers) with <c>-peakxr-perf-lenient</c>; SKIP without a sane refresh rate.</summary>
    IEnumerator FrameBudgetCheck()
    {
        // Let the previous step's spawns and screenshot settle before measuring.
        yield return Wait(1f);
        FrameClock.Step.Reset();
        yield return Wait(FrameBudgetSeconds);
        var r = FrameClock.Summarize(FrameClock.Step);
        var note = r.Line(RenderDriver.CurrentScale);
        // The desktop monitor's rate is no budget for the headset: that SKIPs too.
        switch (FrameBudget.Judge(r.Interval, r.Trusted ? r.Hz : 0f))
        {
            case FrameVerdict.Skip:
                stepSkip = "no timing: " + note;
                break;
            // A machine too slow for the headset by itself launches with -peakxr-perf-lenient to keep the numbers.
            case FrameVerdict.Fail when PerfLenient:
                stepNote = FormattableString.Invariant(
                    $"over budget, report only ({PerfLenientFlag}): p95 {r.Interval.P95:0.00} ms vs {r.BudgetMs:0.00} ms; {note}; {AdaptiveNote()}"
                );
                break;
            case FrameVerdict.Fail:
                Fail(
                    FormattableString.Invariant(
                        $"p95 frame {r.Interval.P95:0.00} ms over {FrameBudget.SelfTestMargin:0.0}x the {r.BudgetMs:0.00} ms budget"
                    )
                );
                stepNote = note;
                break;
            default:
                stepNote = note + "; " + AdaptiveNote();
                break;
        }
    }

    const string PerfLenientFlag = "-peakxr-perf-lenient";

    static bool PerfLenient =>
        Array.IndexOf(Environment.GetCommandLineArgs(), PerfLenientFlag) >= 0;

    static string AdaptiveNote() =>
        FormattableString.Invariant(
            $"adaptive quality {(RenderFixesFeature.AdaptiveQuality.Value ? "on" : "off")}, {RenderDriver.AdaptiveSteps} step(s) applied, render scale {RenderDriver.CurrentScale:0.00}"
        );
}
