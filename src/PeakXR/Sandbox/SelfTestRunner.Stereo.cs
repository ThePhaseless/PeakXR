using System;
using System.Collections;
using System.Collections.Generic;
using PeakXR.Render;
using PeakXR.Rig;
using PeakXR.VR;
using PeakXR.XR;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PeakXR.Sandbox;

/// <summary>
/// Stereo agreement and frame-time evidence (issues #5 and #6), standing still in the level after <c>vr.231</c>:
/// <c>vr.277</c> both MultiPass eyes cull with the same parameters (so the shadow cascades match), <c>vr.278</c> both
/// eyes dither with the same noise, <c>vr.470</c> both eyes fit the same sun shadow cascades, <c>vr.471</c> a cutscene
/// camera stays out of the headset while the rig's fallback camera renders (#58), <c>vr.279</c> the per-second perf
/// line measures frames, and nothing of ours paces or caps a shown frame.
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

    /// <summary>vr.470: over 1 s, every frame whose two eye passes fitted the sun's shadow cascades fitted the same ones
    /// in both (culling spheres and world-to-shadow matrices, <see cref="RenderPatches.EyeShadowCascades"/>): what the
    /// shared culling of vr.277 is for. A per-eye screenshot diff cannot tell a shadow apart from the eyes' parallax; this
    /// compares the shadow state itself. SKIP outside MultiPass, with Render.SharedEyeCulling off, or when no frame fitted
    /// cascades in both eyes (no sun shadows here).</summary>
    IEnumerator EyeShadowCheck()
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
        var match = RenderPatches.EyeShadowCascades.Match;
        int stereo0 = match.StereoFrames;
        int matched0 = match.MatchedFrames;
        yield return Wait(1f);
        int stereo = match.StereoFrames - stereo0;
        int matched = match.MatchedFrames - matched0;
        if (stereo == 0)
        {
            stepSkip =
                "no frame fitted sun shadow cascades in both eyes (no main light shadows here)";
            yield break;
        }
        stepNote = $"{matched} of {stereo} frames fitted the same sun shadow cascades in both eyes";
        if (matched < stereo)
            Fail(
                $"{stereo - matched} of {stereo} frames fitted each eye's sun shadow cascades apart"
            );
    }

    /// <summary>
    /// vr.471 (#58): a scene camera that draws nothing stands in for a cutscene camera; the game camera's object is
    /// switched off as the end cutscene does (PeakHandler.cs:77), so the rig's fallback camera takes the headset. URP
    /// calls beginCameraRendering once per XR pass (UniversalRenderPipeline.RenderCameraStack), so a camera drawn into
    /// both MultiPass eyes renders twice a frame and a mono one once. Before: the test camera renders 2 passes a frame.
    /// During the fallback: 1 pass, allowXRRendering off and <see cref="HeadsetContext.ToHeadset"/> false. After the
    /// game camera is back: 2 passes again, its flag and eye mask restored. SKIP outside MultiPass.
    /// </summary>
    IEnumerator FallbackExclusiveCheck()
    {
        var d = XRRuntimeInfo.Display;
        if (d == null || d.GetRenderPassCount() != 2)
        {
            stepSkip =
                $"needs MultiPass to count eye passes ({(d == null ? 0 : d.GetRenderPassCount())} render passes)";
            yield break;
        }
        var main = MainCamera.instance;
        var rig = VRRig.Instance;
        if (main == null || rig == null || !main.gameObject.activeInHierarchy)
        {
            Fail("no live game camera or rig");
            yield break;
        }
        var fails = new List<string>();
        var notes = new List<string>();
        // Created in the active scene (the level, outside DontDestroyOnLoad) with no target texture: a screen camera like a cutscene's.
        var go = new GameObject("PeakXR.SelfTest.CutsceneCamera");
        var cam = go.AddComponent<Camera>();
        cam.cullingMask = 0;
        cam.clearFlags = CameraClearFlags.Nothing;
        cam.depth = -100f;
        var data = cam.GetUniversalAdditionalCameraData();
        int frame = -1;
        int inFrame = 0;
        int most = 0;
        Action<ScriptableRenderContext, Camera> count = (_, c) =>
        {
            if (c != cam)
                return;
            if (Time.frameCount != frame)
            {
                frame = Time.frameCount;
                inFrame = 0;
            }
            inFrame++;
            most = Math.Max(most, inFrame);
        };
        RenderPipelineManager.beginCameraRendering += count;
        try
        {
            yield return Wait(0.3f);
            notes.Add($"before: {most} passes a frame");
            if (most != 2)
                fails.Add(
                    $"the test camera rendered {most} passes a frame before the fallback, want 2 (both eyes)"
                );

            main.gameObject.SetActive(false);
            yield return WaitFor(
                () => rig.HeadCamera != null && !rig.HeadCamera.TryGetComponent<MainCamera>(out _),
                1f
            );
            if (rig.HeadCamera == null || rig.HeadCamera.TryGetComponent<MainCamera>(out _))
                fails.Add(
                    "the rig's fallback camera did not take the headset with the game camera off"
                );
            most = 0;
            yield return Wait(0.3f);
            notes.Add($"fallback: {most} passes a frame, allowXRRendering {data.allowXRRendering}");
            if (most != 1)
                fails.Add(
                    $"the test camera rendered {most} passes a frame during the fallback, want 1 (mono, not the eyes)"
                );
            if (data.allowXRRendering || HeadsetContext.ToHeadset(cam))
                fails.Add("the test camera still allowed XR rendering during the fallback");
            if (!HeadsetContext.ToHeadset(rig.HeadCamera))
                fails.Add(
                    $"the fallback camera '{(rig.HeadCamera != null ? rig.HeadCamera.name : "none")}' does not render to the headset"
                );

            main.gameObject.SetActive(true);
            yield return WaitFor(
                () => rig.HeadCamera != null && rig.HeadCamera.TryGetComponent<MainCamera>(out _),
                1f
            );
            most = 0;
            yield return Wait(0.3f);
            notes.Add($"after: {most} passes a frame");
            if (
                most != 2
                || !data.allowXRRendering
                || cam.stereoTargetEye != StereoTargetEyeMask.Both
            )
                fails.Add(
                    $"after the fallback the test camera rendered {most} passes a frame, allowXRRendering {data.allowXRRendering}, eyes {cam.stereoTargetEye}; want 2, true, Both"
                );
        }
        finally
        {
            RenderPipelineManager.beginCameraRendering -= count;
            if (main != null && !main.gameObject.activeSelf)
                main.gameObject.SetActive(true);
            UnityEngine.Object.Destroy(go);
        }
        Conclude(fails, notes);
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
