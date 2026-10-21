using System.Collections;
using System.Collections.Generic;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakXR.Sandbox;

/// <summary>
/// Screenshots aimed at their subject. Nobody wears the headset during a run, so the HMD looks wherever it
/// started and a wheel, label or hand below or beside the view never reaches the picture. <see cref="ShotAt"/>
/// turns the rendered view toward a point for the frames of one capture only: the head transform (and with it
/// the head-locked vignette and fade) and the camera are turned between <c>beginContextRendering</c> and
/// <c>endContextRendering</c> and put back right after, so tracking, hands, game logic and step checks never see
/// the turned head.
/// </summary>
internal sealed partial class SelfTestRunner
{
    Vector3 aimFrom;
    Quaternion aimRotation;
    int aimUntilFrame = -1;
    bool aimHooked;
    bool aimApplied;
    Transform aimedHead;
    Camera aimedCam;
    Vector3 savedHeadPosition;
    Quaternion savedHeadRotation;
    Vector3 savedCamPosition;
    Quaternion savedCamRotation;

    /// <summary>
    /// <see cref="Shot"/> with the view aimed at <paramref name="target"/> (world space). With
    /// <paramref name="distance"/> above 0 the view also backs off along the eye-to-target line until the target
    /// is that far away, for subjects too close to the eye to frame.
    /// </summary>
    IEnumerator ShotAt(string name, Vector3 target, float distance = 0f)
    {
        var cam = ViewCamera();
        if (cam != null)
        {
            var eye = cam.transform.position;
            var dir = target - eye;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : cam.transform.forward;
            aimFrom = distance > 0f ? target - dir * distance : eye;
            // Up stays world up so the picture is level; straight down falls back to the view's own up.
            aimRotation = Quaternion.LookRotation(
                dir,
                Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.99f ? cam.transform.up : Vector3.up
            );
            // Shot captures at the end of this frame and waits one more; cover both.
            aimUntilFrame = Time.frameCount + 1;
            if (!aimHooked)
            {
                aimHooked = true;
                RenderPipelineManager.beginContextRendering += AimBegin;
                RenderPipelineManager.endContextRendering += AimEnd;
            }
        }
        yield return Shot(name);
        aimUntilFrame = -1;
    }

    /// <summary>Between the scripted hands, a little ahead: frames both hands and what they hold.</summary>
    static Vector3 HandsMid()
    {
        var rig = VRRig.Instance;
        if (rig == null || rig.LeftHand == null || rig.RightHand == null)
            return Vector3.zero;
        return 0.5f * (rig.LeftHand.position + rig.RightHand.position);
    }

    /// <summary>The visual centre of a UI element (a RectTransform's pivot can sit on its edge).</summary>
    static Vector3 CentreOf(Transform t)
    {
        if (t is RectTransform rt)
            return rt.TransformPoint(rt.rect.center);
        return t.position;
    }

    /// <summary>The headset's camera; null in flat play, where menus are screen overlays and shots stay unaimed.</summary>
    static Camera ViewCamera()
    {
        var rig = VRRig.Instance;
        return VRSession.Active && rig != null ? rig.HeadCamera : null;
    }

    void AimBegin(ScriptableRenderContext context, List<Camera> cameras)
    {
        if (Time.frameCount > aimUntilFrame || aimApplied)
            return;
        var cam = ViewCamera();
        if (cam == null)
            return;
        aimApplied = true;
        aimedCam = cam;
        savedCamPosition = cam.transform.position;
        savedCamRotation = cam.transform.rotation;
        var rig = VRRig.Instance;
        aimedHead = VRSession.Active && rig != null ? rig.Head : null;
        if (aimedHead != null)
        {
            aimedHead.GetLocalPositionAndRotation(out savedHeadPosition, out savedHeadRotation);
            aimedHead.SetPositionAndRotation(aimFrom, aimRotation);
        }
        cam.transform.SetPositionAndRotation(aimFrom, aimRotation);
    }

    void AimEnd(ScriptableRenderContext context, List<Camera> cameras)
    {
        if (!aimApplied)
            return;
        aimApplied = false;
        if (aimedHead != null)
            aimedHead.SetLocalPositionAndRotation(savedHeadPosition, savedHeadRotation);
        if (aimedCam != null)
            aimedCam.transform.SetPositionAndRotation(savedCamPosition, savedCamRotation);
        aimedHead = null;
        aimedCam = null;
    }
}
