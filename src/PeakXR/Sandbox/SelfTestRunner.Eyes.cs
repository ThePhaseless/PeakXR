using System.Collections;
using System.Collections.Generic;
using PeakXR.Core;
using PeakXR.Render;
using PeakXR.UI.Status;
using PeakXR.UI.World;
using PeakXR.VR;
using PeakXR.XR;
using UnityEngine;
using UnityEngine.XR;

namespace PeakXR.Sandbox;

/// <summary>
/// Whether anything differs between the eyes that should not (issue #5), from each eye's own pixels (<see cref="ReadEyes"/>)
/// on the level. <c>vr.480</c> measures the head-locked overlays: the Peripheral hide, a control that shows it could fail,
/// and the stereo offset of a real quad.
/// </summary>
internal sealed partial class SelfTestRunner
{
    private const int EyeLumaStep = 4;

    private static readonly int PoisonIntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int PoisonEdgeId = Shader.PropertyToID("_Edge");
    private static readonly int PoisonEdgeAddId = Shader.PropertyToID("_EdgeAdd");

    private EyeImages flashOff;
    private EyeImages flashOn;

    /// <summary><see cref="VRStep"/> that runs <see cref="OnRestore"/>'s undos, which plain VRStep leaves alone; without
    /// the scripted hands <see cref="Restoring"/> also starts. A timeout reaches the finally through
    /// <see cref="Unwind"/>.</summary>
    IEnumerator EyeStep(string id, float timeout, IEnumerator body)
    {
        try
        {
            yield return VRStep(id, timeout, body);
        }
        finally
        {
            var restore = bodyRestore;
            bodyRestore = null;
            restore?.Invoke();
        }
    }

    /// <summary>Holds the world still for comparing reads. <see cref="Wait"/> and <see cref="ReadEyes"/> run on real time.</summary>
    void FreezeWorld()
    {
        float before = Time.timeScale;
        Time.timeScale = 0f;
        OnRestore(() => Time.timeScale = before);
    }

    static IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++)
            yield return null;
    }

    /// <summary>The head camera and display when two eye passes render; otherwise the step is skipped or failed.</summary>
    bool EyesReady(out Camera cam, out XRDisplaySubsystem display)
    {
        display = XRRuntimeInfo.Display;
        var rig = VRRig.Instance;
        cam = rig != null ? rig.HeadCamera : null;
        if (display == null || display.GetRenderPassCount() != 2)
        {
            stepSkip =
                $"not MultiPass ({(display == null ? 0 : display.GetRenderPassCount())} render passes)";
            return false;
        }
        if (cam == null)
        {
            Fail("no head camera to read the eyes from");
            return false;
        }
        return true;
    }

    /// <summary>Each eye's image boxed down for comparing.</summary>
    static float[] EyeLuma(EyeImages image, int eye) =>
        EyeImageMatch.Luma(image.Pixels[eye], image.Width, image.Height, EyeLumaStep);

    /// <summary>vr.480: see the class comment; the phases are in docs/testing.md.</summary>
    IEnumerator EyeOverlaysStep()
    {
        if (!EyesReady(out var cam, out var display))
            yield break;
        if (!UIWorldRuntime.Ready)
        {
            Fail("UIWorldRuntime.Ready is false: ui.world does not pose the overlays");
            yield break;
        }
        if (UIStatusFeature.Intensity == null)
        {
            Fail("ui.status is not running: no Peripheral hide to measure");
            yield break;
        }
        var fx = GUIManager.instance != null ? GUIManager.instance.poisonSVFX : null;
        if (fx == null || fx.renderer == null)
        {
            Fail("GUIManager.poisonSVFX has no renderer");
            yield break;
        }

        var material = fx.renderer.material;
        bool wasActive = fx.gameObject.activeSelf;
        float intensity = material.GetFloat(PoisonIntensityId);
        float edge = material.GetFloat(PoisonEdgeId);
        float edgeAdd = material.GetFloat(PoisonEdgeAddId);
        var overlays = UIWorldFeature.Settings.StatusOverlays;
        var mode = overlays.Value;
        float rim = UIStatusFeature.Intensity.Value;
        bool locked = RenderFixesFeature.EyeLockedOverlays.Value;
        OnRestore(() =>
        {
            flashOff = null;
            flashOn = null;
            overlays.Value = mode;
            UIStatusFeature.Intensity.Value = rim;
            RenderFixesFeature.EyeLockedOverlays.Value = locked;
            if (fx != null)
                fx.gameObject.SetActive(wasActive);
            if (material != null)
            {
                material.SetFloat(PoisonIntensityId, intensity);
                material.SetFloat(PoisonEdgeId, edge);
                material.SetFloat(PoisonEdgeAddId, edgeAdd);
            }
        });
        FreezeWorld();
        yield return Frames(3);

        var first = new EyeImages(2);
        var second = new EyeImages(2);
        yield return ReadEyes(cam, display, first);
        yield return ReadEyes(cam, display, second);
        string problem = first.Problem ?? second.Problem;
        if (problem != null)
        {
            Fail("noise read: " + problem);
            yield break;
        }
        var floor = new float[2];
        for (int eye = 0; eye < 2; eye++)
            floor[eye] = Mathf.Max(
                0.5f,
                2f * EyeImageMatch.Rms(EyeLuma(first, eye), EyeLuma(second, eye))
            );
        var notes = new List<string> { F($"floor L {floor[0]:0.00} R {floor[1]:0.00} of 255") };
        var fails = new List<string>();

        // A hide that works leaves nothing to see; the poison quad covers the view at alpha 1, or this proves nothing
        // (at its own _EdgeAdd it has alpha 0 everywhere in view). The rim would draw the same status at the view's edge.
        UIStatusFeature.Intensity.Value = 0f;
        material.SetFloat(PoisonIntensityId, 1f);
        material.SetFloat(PoisonEdgeAddId, 1f);
        var settings = new[] { false, true };
        foreach (bool eyeLocked in settings)
        {
            string label = eyeLocked ? "on" : "off";
            yield return PoisonOffAndOn(cam, display, fx, StatusOverlayMode.Peripheral, eyeLocked);
            if (!FlashVerdict($"P1 {label}", true, floor, notes, fails))
                SaveFlash($"vr-480-p1-{label}");
            yield return PoisonOffAndOn(cam, display, fx, StatusOverlayMode.HeadLocked, eyeLocked);
            if (!FlashVerdict($"P2 {label}", false, floor, notes, fails))
                SaveFlash($"vr-480-p2-{label}");
        }

        // Alpha 0 inside r = 1/12 of the quad (0.5 tan) and 1 outside: a hard-edged hole at the quad's centre. Its stereo
        // offset is the overlay's. The stock quad is the only kind that can answer: a shader with DOTS variants is what
        // the resident drawer can hold, which would keep the quad at one pose for both eyes.
        material.SetFloat(PoisonEdgeId, 200f);
        material.SetFloat(PoisonEdgeAddId, -200f / 12f);
        foreach (bool eyeLocked in settings)
        {
            yield return PoisonOffAndOn(cam, display, fx, StatusOverlayMode.HeadLocked, eyeLocked);
            string label = eyeLocked ? "on" : "off";
            if (!HoleVerdict(cam, eyeLocked, floor, notes, fails))
                SaveFlash($"vr-480-p4-{label}");
        }

        stepNote = string.Join("; ", notes);
        if (fails.Count > 0)
            Fail(string.Join("; ", fails));
    }

    /// <summary>Reads both eyes with the poison quad off and then on, into <see cref="flashOff"/> and
    /// <see cref="flashOn"/>.</summary>
    IEnumerator PoisonOffAndOn(
        Camera cam,
        XRDisplaySubsystem display,
        ScreenVFX fx,
        StatusOverlayMode mode,
        bool eyeLocked
    )
    {
        UIWorldFeature.Settings.StatusOverlays.Value = mode;
        RenderFixesFeature.EyeLockedOverlays.Value = eyeLocked;
        fx.gameObject.SetActive(false);
        yield return Frames(3);
        flashOff = new EyeImages(2);
        yield return ReadEyes(cam, display, flashOff);
        fx.gameObject.SetActive(true);
        yield return Frames(3);
        flashOn = new EyeImages(2);
        yield return ReadEyes(cam, display, flashOn);
    }

    /// <summary>P1 (<paramref name="hidden"/>): each eye stays within the noise floor. P2: each eye shows the quad clearly.
    /// False on a red.</summary>
    bool FlashVerdict(
        string phase,
        bool hidden,
        float[] floor,
        List<string> notes,
        List<string> fails
    )
    {
        string problem = flashOff.Problem ?? flashOn.Problem;
        if (problem != null)
        {
            fails.Add($"{phase}: {problem}");
            return false;
        }
        float left = EyeImageMatch.Rms(EyeLuma(flashOff, 0), EyeLuma(flashOn, 0));
        float right = EyeImageMatch.Rms(EyeLuma(flashOff, 1), EyeLuma(flashOn, 1));
        notes.Add(F($"{phase} L {left:0.00} R {right:0.00}"));
        bool ok = hidden
            ? left <= floor[0] && right <= floor[1]
            : left >= Mathf.Max(10f, 3f * floor[0]) && right >= Mathf.Max(10f, 3f * floor[1]);
        if (!ok)
            fails.Add(
                hidden
                    ? F(
                        $"{phase}: the hidden flash still reached the eyes (L {left:0.00} R {right:0.00})"
                    )
                    : F(
                        $"{phase}: the HeadLocked flash barely shows (L {left:0.00} R {right:0.00}): phase P1 proves nothing"
                    )
            );
        return ok;
    }

    /// <summary>P4: the hole's centre, in head-frame tangents, per eye. Off it differs by about the eyes' stereo on a quad
    /// at D, and on it must not differ. False on a red.</summary>
    bool HoleVerdict(
        Camera cam,
        bool eyeLocked,
        float[] floor,
        List<string> notes,
        List<string> fails
    )
    {
        string phase = eyeLocked ? "P4 on" : "P4 off";
        string problem = flashOff.Problem ?? flashOn.Problem;
        if (problem != null)
        {
            fails.Add($"{phase}: {problem}");
            return false;
        }
        var centre = new float[2];
        for (int eye = 0; eye < 2; eye++)
        {
            var view = flashOn.Views[eye];
            var viewport = flashOn.Viewports[eye];
            var (left, right, bottom, top) = StereoCull.Tangents(
                flashOn.Projections[eye].m00,
                flashOn.Projections[eye].m02,
                flashOn.Projections[eye].m11,
                flashOn.Projections[eye].m12
            );
            // The head's axis in this eye. The hole's chord is centred on any row through it, so a row a few pixels off
            // or a flipped readback changes nothing.
            Vector3 axis = view.MultiplyVector(cam.transform.forward);
            float axisX = axis.x / -axis.z;
            float axisY = axis.y / -axis.z;
            int column = Mathf.Clamp(
                Mathf.RoundToInt(viewport.x + (axisX - left) / (right - left) * viewport.width),
                0,
                flashOn.Width - 1
            );
            int row = Mathf.Clamp(
                Mathf.RoundToInt(viewport.y + (axisY - bottom) / (top - bottom) * viewport.height),
                0,
                flashOn.Height - 1
            );
            var (leftEdge, rightEdge) = EyeImageMatch.HoleEdges(
                flashOff.Pixels[eye],
                flashOn.Pixels[eye],
                flashOn.Width,
                row,
                column,
                Mathf.CeilToInt(Mathf.Max(8f, 3f * floor[eye]))
            );
            if (leftEdge < 0 || rightEdge < 0)
            {
                fails.Add(
                    $"{phase}: no hole in the {(eye == 0 ? "left" : "right")} eye (edges {leftEdge}, {rightEdge} from column {column}, row {row})"
                );
                return false;
            }
            float a = HeadTangent(cam, view, viewport, left, right, axisY, leftEdge);
            float b = HeadTangent(cam, view, viewport, left, right, axisY, rightEdge);
            centre[eye] = (a + b) * 0.5f;
            notes.Add(
                F(
                    $"{phase} {(eye == 0 ? "L" : "R")} hole columns {leftEdge}-{rightEdge}, centre {centre[eye]:0.000} tan"
                )
            );
        }
        float offset = Mathf.Abs(centre[0] - centre[1]);
        float separation = Vector3.Distance(
            flashOn.Views[0].inverse.GetColumn(3),
            flashOn.Views[1].inverse.GetColumn(3)
        );
        float bound = 0.6f * separation / (cam.nearClipPlane + 0.03f);
        notes.Add(
            F($"{phase} |cL - cR| {offset:0.000} tan, eyes {separation * 1000f:0.0} mm apart")
        );
        bool ok = eyeLocked ? offset <= 0.01f : offset >= bound;
        if (!ok)
            fails.Add(
                eyeLocked
                    ? F(
                        $"{phase}: the quad's pattern still sits {offset:0.000} tan apart between the eyes"
                    )
                    : F(
                        $"{phase}: {offset:0.000} tan apart, today's quad should show at least {bound:0.000}"
                    )
            );
        return ok;
    }

    /// <summary>A pixel column of one eye, as a tangent in the head's frame.</summary>
    private static float HeadTangent(
        Camera cam,
        Matrix4x4 view,
        Rect viewport,
        float left,
        float right,
        float eyeY,
        int column
    )
    {
        float eyeX = left + (column + 0.5f - viewport.x) / viewport.width * (right - left);
        Vector3 world = view.inverse.MultiplyVector(new Vector3(eyeX, eyeY, -1f));
        Vector3 head = cam.transform.InverseTransformDirection(world);
        return head.x / head.z;
    }

    void SaveFlash(string shot)
    {
        if (flashOn.Problem != null)
            return;
        SaveEye(flashOn, shot + "-left", 0);
        SaveEye(flashOn, shot + "-right", 1);
    }
}
