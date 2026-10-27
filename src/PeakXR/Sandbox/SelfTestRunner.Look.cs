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

    /// <summary>Both scripted hands hang at the sides, a little behind the eye with the fingers down, so neither
    /// they nor what they hold reach into a shot of a panel; the caller ends with <see cref="EndHands"/>. Run 6 hung
    /// them straight below the eye with the fingers ahead, and the shot's wide view still caught the fingers and
    /// the held passport over the lowest rows.</summary>
    IEnumerator HandsAside()
    {
        BeginHands();
        HeadFrame(out var head, out var forward, out var right);
        var down = HandLook(Vector3.down, forward);
        scriptLeft.Position = head - right * 0.3f - forward * 0.25f + Vector3.down * 0.8f;
        scriptRight.Position = head + right * 0.3f - forward * 0.25f + Vector3.down * 0.8f;
        scriptLeft.Rotation = scriptRight.Rotation = down;
        yield return Wait(0.5f);
    }

    /// <summary>
    /// <see cref="Shot"/> with the view aimed at <paramref name="target"/> (world space). With
    /// <paramref name="distance"/> above 0 the view also backs off along the eye-to-target line until the target
    /// is that far away, for subjects too close to the eye to frame.
    /// </summary>
    IEnumerator ShotAt(string name, Vector3 target, float distance = 0f) =>
        ShotAt(name, target, distance, hideBody: false);

    /// <summary>As above; with <paramref name="hideBody"/> the local player's body and the item in hand are not drawn
    /// in the shot, for a picture of a panel. Runs 6 and 7 scripted the hands aside (<see cref="HandsAside"/>), yet
    /// the avatar's hands and the held passport stayed over the panel's lower rows with a menu open.</summary>
    IEnumerator ShotAt(string name, Vector3 target, float distance, bool hideBody)
    {
        if (hideBody)
            HideBody(true);
        try
        {
            var inner = AimedShot(name, target, distance);
            while (inner.MoveNext())
                yield return inner.Current;
        }
        finally
        {
            if (hideBody)
                HideBody(false);
        }
    }

    readonly List<Renderer> hiddenBody = new();
    readonly List<bool> hiddenWas = new();

    /// <summary>Turns the local character's renderers and its held item's off for a shot (<c>forceRenderingOff</c>,
    /// which nothing else in PeakXR drives), or back to how they were.</summary>
    void HideBody(bool hide)
    {
        if (!hide)
        {
            for (int i = 0; i < hiddenBody.Count; i++)
                if (hiddenBody[i] != null)
                    hiddenBody[i].forceRenderingOff = hiddenWas[i];
            hiddenBody.Clear();
            hiddenWas.Clear();
            return;
        }
        var c = Character.localCharacter;
        if (c == null)
            return;
        void Add(Component root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                hiddenBody.Add(r);
                hiddenWas.Add(r.forceRenderingOff);
                r.forceRenderingOff = true;
            }
        }
        Add(c);
        if (c.data.currentItem != null)
            Add(c.data.currentItem);
    }

    /// <summary>A shot of everything in <paramref name="points"/>: aimed at their middle and, when they span more than
    /// the view, backed off along the eye's line until they fit with a margin. The body is hidden.</summary>
    IEnumerator ShotFraming(string name, List<Vector3> points)
    {
        var cam = ViewCamera();
        if (cam == null || points.Count == 0)
        {
            yield return Shot(name);
            yield break;
        }
        var b = new Bounds(points[0], Vector3.zero);
        foreach (var p in points)
            b.Encapsulate(p);
        var centre = b.center;
        var eye = cam.transform.position;
        var look = Quaternion.LookRotation(centre - eye, Vector3.up);
        var right = look * Vector3.right;
        var up = look * Vector3.up;
        float halfX = 0f,
            halfY = 0f;
        foreach (var p in points)
        {
            halfX = Mathf.Max(halfX, Mathf.Abs(Vector3.Dot(p - centre, right)));
            halfY = Mathf.Max(halfY, Mathf.Abs(Vector3.Dot(p - centre, up)));
        }
        // 85% of the view each way, so the edges keep a margin.
        float tanY = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.85f;
        float tanX = tanY * Mathf.Max(cam.aspect, 0.1f);
        float need = Mathf.Max(halfX / tanX, halfY / tanY);
        float distance = Vector3.Distance(eye, centre);
        yield return ShotAt(name, centre, need > distance ? need : 0f, hideBody: true);
    }

    /// <summary>Corners of every graphic drawn under <paramref name="root"/>, into <paramref name="into"/>: what a
    /// framed shot must show. Hidden, faded or culled graphics and full-canvas fills are left out.</summary>
    static void VisibleCorners(RectTransform root, List<Vector3> into)
    {
        var corners = new Vector3[4];
        var size = root.rect.size;
        foreach (var g in root.GetComponentsInChildren<UnityEngine.UI.Graphic>())
        {
            var cr = g.canvasRenderer;
            if (
                !g.isActiveAndEnabled
                || cr.cull
                || g.color.a * cr.GetAlpha() * cr.GetInheritedAlpha() < 0.05f
            )
                continue;
            var rt = g.rectTransform;
            if (
                rt != root
                && rt.rect.width * rt.lossyScale.x >= size.x * root.lossyScale.x * 0.9f
                && rt.rect.height * rt.lossyScale.y >= size.y * root.lossyScale.y * 0.9f
            )
                continue;
            rt.GetWorldCorners(corners);
            into.AddRange(corners);
        }
    }

    IEnumerator AimedShot(string name, Vector3 target, float distance)
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
