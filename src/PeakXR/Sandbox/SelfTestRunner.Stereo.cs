using System;
using System.Collections;
using PeakXR.Render;
using PeakXR.XR;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>
/// Stereo agreement and frame-time evidence (issues #5 and #6), standing still in the level after <c>vr.231</c>:
/// <c>vr.277</c> both MultiPass eyes cull with the same parameters (so the shadow cascades match), <c>vr.278</c> both
/// eyes dither with the same noise, <c>vr.279</c> the per-second perf line measures frames, and nothing of ours paces
/// or caps a shown frame.
/// </summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>vr.277: over 1 s, every frame that rendered two eye passes gave both the same culling parameters.
    /// SKIP in single pass (one cull already); FAIL when the shared culling is off, broken or missed frames.</summary>
    IEnumerator SharedCullingCheck()
    {
        var d = XRRuntimeInfo.Display;
        if (d == null || d.GetRenderPassCount() != 2)
        {
            stepSkip = $"not MultiPass ({(d == null ? 0 : d.GetRenderPassCount())} render passes)";
            yield break;
        }
        if (!RenderFixesFeature.SharedEyeCulling.Value)
        {
            stepSkip = "Render.SharedEyeCulling is off";
            yield break;
        }
        int frames0 = EyeCulling.MultiPassFrames;
        int matched0 = EyeCulling.MatchedFrames;
        yield return Wait(1f);
        int frames = EyeCulling.MultiPassFrames - frames0;
        int matched = EyeCulling.MatchedFrames - matched0;
        stepNote = $"{matched} of {frames} MultiPass frames culled both eyes alike";
        if (!EyeCulling.Active || frames == 0)
            Fail(
                $"shared eye culling not running: {EyeCulling.Problem ?? "no MultiPass frame reached the hook"}"
            );
        else if (matched < frames)
            Fail(
                $"{frames - matched} of {frames} frames culled the eyes apart ({EyeCulling.Problem ?? "no reason"})"
            );
    }

    /// <summary>vr.278: over 1 s, every frame whose post passes dithered (one per eye) picked the same blue-noise texture
    /// in each. SKIP when nothing dithered (post-processing or dithering off on the head camera).</summary>
    IEnumerator EyeDitherCheck()
    {
        int stereo0 = RenderPatches.DitherSameInBothEyes.StereoFrames;
        int matched0 = RenderPatches.DitherSameInBothEyes.MatchedFrames;
        yield return Wait(1f);
        int stereo = RenderPatches.DitherSameInBothEyes.StereoFrames - stereo0;
        int matched = RenderPatches.DitherSameInBothEyes.MatchedFrames - matched0;
        if (stereo == 0)
        {
            stepSkip = "no frame dithered in both eyes (dithering or post-processing off)";
            yield break;
        }
        stepNote = $"{matched} of {stereo} frames dithered both eyes alike";
        if (matched < stereo)
            Fail($"{stereo - matched} of {stereo} frames dithered each eye with other noise");
    }

    /// <summary>vr.279: the perf line ran (PlayerLoop markers measure frames, main-thread busy time fits in the frame),
    /// the frame is not capped or vsynced by us, and idle pacing is off while the headset shows the game.</summary>
    IEnumerator PerfProbeCheck()
    {
        if (!(RenderFixesFeature.PerfLogSeconds.Value > 0f))
        {
            stepSkip = "Render.PerfLogSeconds is 0";
            yield break;
        }
        int frames0 = PerfProbe.MeasuredFrames;
        yield return Wait(Mathf.Max(1.5f, RenderFixesFeature.PerfLogSeconds.Value + 0.5f));
        int frames = PerfProbe.MeasuredFrames - frames0;
        stepNote = PerfProbe.LastLine ?? "no perf line yet";
        if (!PerfProbe.Installed || frames == 0)
            Fail("the perf markers measured no frame");
        else if (
            !(PerfProbe.WindowBusyMs >= 0f)
            || PerfProbe.WindowBusyMs > PerfProbe.WindowFrameMs * 1.01f
        )
            Fail(
                FormattableString.Invariant(
                    $"main busy {PerfProbe.WindowBusyMs:0.00} ms does not fit the {PerfProbe.WindowFrameMs:0.00} ms frame"
                )
            );
        else if (RenderDriver.IdlePacing)
            Fail("idle pacing runs while the headset shows the game");
        else if (Application.targetFrameRate > 0 || QualitySettings.vSyncCount != 0)
            Fail(
                $"a frame cap or VSync is on in VR (targetFrameRate {Application.targetFrameRate}, vSyncCount {QualitySettings.vSyncCount})"
            );
    }
}
