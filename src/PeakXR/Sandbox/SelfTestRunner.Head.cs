using System;
using System.Collections;
using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakXR.Sandbox;

/// <summary>
/// <c>vr.247-own-head-mirror</c>: the own head is hidden only from the eyes (<see cref="OwnHead"/>). A probe camera
/// 1.5 m in front of the face, rendered once the way a mirror or the photobooth renders (Mirror.cs:7,
/// AirportPhotoboothKiosk.cs:111), must draw the head renderers (shadow mode On during its render), the eye camera
/// must keep them shadow-only, and they must be back to shadow-only once the probe's render ends.
/// </summary>
internal sealed partial class SelfTestRunner
{
    IEnumerator OwnHeadCheck()
    {
        var fails = new List<string>();
        var notes = new List<string>();
        var rig = VRRig.Instance;
        if (OwnHead.Hidden.Count == 0)
        {
            bool expected =
                string.Equals(
                    AvatarConfig.Instance.OwnBody,
                    AvatarConfig.ShowArms,
                    StringComparison.Ordinal
                ) && Rig.RigDriver.ViewOnBody;
            if (expected)
                Fail("own head not hidden from the eyes in OwnBody Arms with the view on the body");
            else
                stepNote = "own head not hidden (OwnBody or view): nothing to judge";
            yield break;
        }
        var head = OwnHead.Hidden[0];
        if (rig == null || rig.HeadCamera == null || head == null)
        {
            Fail("no head camera or head renderer");
            yield break;
        }

        var probeMode = (ShadowCastingMode)(-1);
        var eyeMode = (ShadowCastingMode)(-1);
        var go = new GameObject("PeakXR.SelfTest.HeadProbe");
        var probe = go.AddComponent<Camera>();
        probe.enabled = false;
        probe.stereoTargetEye = StereoTargetEyeMask.None;
        var texture = new RenderTexture(64, 64, 24);
        probe.targetTexture = texture;
        var face = head.bounds.center;
        var forward = Vector3.ProjectOnPlane(rig.HeadCamera.transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 1e-4f)
            forward = Vector3.forward;
        go.transform.position = face + forward.normalized * 1.5f * rig.Scale;
        go.transform.LookAt(face);

        Action<ScriptableRenderContext, Camera> record = (_, cam) =>
        {
            if (cam == probe)
                probeMode = head.shadowCastingMode;
            else if (cam == rig.HeadCamera)
                eyeMode = head.shadowCastingMode;
        };
        // Added after OwnHead's handler, so it sees the mode OwnHead set for that camera.
        RenderPipelineManager.beginCameraRendering += record;
        try
        {
            probe.Render();
            var after = head.shadowCastingMode;
            notes.Add(F($"probe saw {probeMode}, after its render {after}"));
            if (probeMode != ShadowCastingMode.On)
                fails.Add(F($"head drawn {probeMode} for a mirror-like camera, want On"));
            if (after != ShadowCastingMode.ShadowsOnly)
                fails.Add(F($"head left {after} after the probe render, want ShadowsOnly"));
        }
        finally
        {
            UnityEngine.Object.Destroy(go);
            texture.Release();
            UnityEngine.Object.Destroy(texture);
        }
        yield return WaitFor(() => (int)eyeMode >= 0, 2f);
        RenderPipelineManager.beginCameraRendering -= record;
        notes.Add(F($"eye saw {eyeMode}, {OwnHead.Hidden.Count} head renderers"));
        if (eyeMode != ShadowCastingMode.ShadowsOnly)
            fails.Add(F($"head drawn {eyeMode} for the eye camera, want ShadowsOnly"));
        Conclude(fails, notes);
    }
}
