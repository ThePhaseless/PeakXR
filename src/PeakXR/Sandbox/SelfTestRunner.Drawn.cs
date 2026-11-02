using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using PeakXR.Core;
using PeakXR.Render;
using PeakXR.UI.Common;
using PeakXR.UI.World;
using PeakXR.VR;
using PeakXR.XR;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace PeakXR.Sandbox;

/// <summary>
/// Whether a world-space panel really reaches the eye, from the eye's own render target (issue #131). The placement and
/// legibility steps compute where a panel would show, so they passed through a whole release in which no canvas was drawn
/// in the headset. Here the panel leaves the eye camera's mask for a frame: it is drawn exactly when that
/// changes the image more than one more frame of the moving scene does (Core <see cref="DrawnPixels"/>). Runs offline.
/// </summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>Frames a readback may take before the check says the eye could not be read.</summary>
    private const float EyeReadSeconds = 3f;

    /// <summary>The drawn fade above which the fade sphere shows (FadeSphere.Apply), so a frame is not the scene's own.</summary>
    private const float FadedAbove = 0.001f;

    /// <summary>vr.02c: the panel on show at the title, the connection popup offline or the title menu with its buttons
    /// otherwise, is in both eyes' pixels.</summary>
    IEnumerator TitleDrawnCheck()
    {
        if (SelfTestFeature.Hold)
        {
            stepSkip =
                "hold mode: a person can see the menu; this check needs a still head with the panel in view";
            yield break;
        }
        Canvas root = null;
        yield return WaitFor(
            () =>
            {
                root =
                    ConnectFailedPopup() != null
                        ? RootCanvasOf(Zorro.UI.Modal.Modal.Instance)
                        : RootCanvasOf(FindAnyObjectByType<MainMenuPageHandler>());
                return root != null
                    && root.renderMode == RenderMode.WorldSpace
                    && WorldCanvases.IsConverted(root);
            },
            10f
        );
        if (root == null || root.renderMode != RenderMode.WorldSpace)
        {
            Fail("no world-space title panel to look for");
            yield break;
        }
        // Online the panel is the full menu: read it once the intro has shown the buttons, past the depth fade.
        var stage = UI.Menu.TitleStage.Instance;
        if (ConnectFailedPopup() == null && stage != null && stage.Active)
        {
            yield return TitleButtonsUp(stage.Eye, TitleReachSetting + 0.25f);
            if (!stepOk)
                yield break;
            if (ConnectFailedPopup() != null)
                root = RootCanvasOf(Zorro.UI.Modal.Modal.Instance);
            yield return Wait(0.5f);
        }
        yield return PanelInEyes(root, "vr-02c-title");
    }

    /// <summary>vr.137: over a gap in the panel on show at the title, where no dot lands, the beam reaches that panel
    /// instead of stopping at its miss length in the air. Sweeps both hands over the panel's rect for such a spot.</summary>
    IEnumerator BeamOnPanelCheck()
    {
        if (SelfTestFeature.Hold)
        {
            stepSkip = "hold mode: the hands are a person's";
            yield break;
        }
        var root =
            ConnectFailedPopup() != null
                ? RootCanvasOf(Zorro.UI.Modal.Modal.Instance)
                : RootCanvasOf(FindAnyObjectByType<MainMenuPageHandler>());
        if (root == null || !WorldCanvases.IsConverted(root))
        {
            Fail("no converted title panel to aim at");
            yield break;
        }
        var rig = VRRig.Instance;
        var rect = (RectTransform)root.transform;
        var box = rect.rect;
        // At the title the override is IdleHands; only ApplyHands emits the script below.
        BeginHands();
        HeadFrame(out var head, out var forward, out var right);
        int aimed = 0;
        try
        {
            for (int x = 1; x < 8 && aimed == 0; x++)
            for (int y = 1; y < 6 && aimed == 0; y++)
            {
                var target = rect.TransformPoint(
                    new Vector3(box.xMin + box.width * x / 8f, box.yMin + box.height * y / 6f, 0f)
                );
                for (int i = 0; i < 2; i++)
                {
                    bool left = i == 0;
                    var p =
                        head
                        + (left ? -right : right) * 0.15f
                        + Vector3.down * 0.2f
                        + forward * 0.2f;
                    Script(left).Position = p;
                    Script(left).Rotation = HandLook(
                        rig.Origin.InverseTransformDirection(target - rig.Origin.TransformPoint(p)),
                        Vector3.up
                    );
                }
                yield return Wait(0.1f);
                var aim = rig.Aim(LaserPointer.OverLeft);
                float panel = Vector3.Distance(aim.position, target);
                if (
                    !LaserPointer.Showing
                    || LaserPointer.DotShown
                    || panel < LaserLook.MissLength + 0.2f
                )
                    continue;
                aimed++;
                float beam = Vector3.Distance(aim.position, LaserPointer.BeamEnd);
                if (beam < LaserLook.MissLength + 0.1f)
                    Fail(
                        F(
                            $"the beam stops at {beam:0.00} m over a gap in the panel {panel:0.00} m away"
                        )
                    );
                else
                    Note(
                        F(
                            $"over a gap the beam runs {beam:0.00} m to the panel {panel:0.00} m away"
                        )
                    );
            }
        }
        finally
        {
            EndHands();
        }
        if (stepOk && aimed == 0)
            stepSkip = "no spot on the panel past the miss length without a dot";
    }

    /// <summary>Fails the step unless <paramref name="root"/> shows in every eye. Reads each eye with the panel on,
    /// on again (what the scene changes by itself) and off the camera's mask; leaves
    /// <paramref name="shot"/> as the left eye's PNG, panel on, so a person can look at what the eye got.</summary>
    IEnumerator PanelInEyes(Canvas root, string shot)
    {
        var rig = VRRig.Instance;
        var display = XRRuntimeInfo.Display;
        var cam = rig != null ? rig.HeadCamera : null;
        if (cam == null || display == null || display.GetRenderPassCount() == 0)
        {
            Fail("no head camera or XR display to read an eye from");
            yield break;
        }
        int layer = FreeLayer(cam);
        if (layer < 0)
        {
            Fail($"no unused layer to hide the panel on (mask {cam.cullingMask})");
            yield break;
        }
        yield return WaitFor(() => ViewClear, 5f);
        yield return PanelSettled(root);
        if (!stepOk)
            yield break;

        int passes = display.GetRenderPassCount();
        var on = new EyeImages(passes);
        var again = new EyeImages(passes);
        var off = new EyeImages(passes);
        yield return ReadEyes(cam, display, on);
        // Hiding the panel below spends one more frame; the same here, so both windows hold as much scene motion.
        yield return null;
        yield return ReadEyes(cam, display, again);
        int mask = cam.cullingMask;
        // The game's canvases share the Default layer with the world, so the panel steps onto an unused one for this.
        var parts = root.GetComponentsInChildren<Transform>(true);
        var layers = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            layers[i] = parts[i].gameObject.layer;
            parts[i].gameObject.layer = layer;
        }
        // Guard's Unwind only disposes a cut-off step, so the restore must sit in finally.
        try
        {
            cam.cullingMask = mask & ~(1 << layer);
            // A frame already under way may still hold the old mask's image.
            yield return null;
            yield return ReadEyes(cam, display, off);
        }
        finally
        {
            if (cam != null)
                cam.cullingMask = mask;
            for (int i = 0; i < parts.Length; i++)
                if (parts[i] != null)
                    parts[i].gameObject.layer = layers[i];
        }

        var notes = new List<string>();
        var fails = new List<string>();
        for (int eye = 0; eye < passes; eye++)
        {
            string label =
                passes == 1 ? "both eyes"
                : eye == 0 ? "left eye"
                : "right eye";
            string problem = on.Problem ?? again.Problem ?? off.Problem;
            if (problem != null)
            {
                fails.Add($"{label}: {problem}");
                continue;
            }
            float noise = DrawnPixels.ChangedShare(on.Pixels[eye], again.Pixels[eye]);
            float signal = DrawnPixels.ChangedShare(again.Pixels[eye], off.Pixels[eye]);
            string line = FormattableString.Invariant(
                $"{label} {on.Width}x{on.Height}: hiding the panel changed {signal * 100f:0.0} %, a frame {noise * 100f:0.0} %"
            );
            notes.Add(line);
            if (!DrawnPixels.PanelDrawn(noise, signal))
                fails.Add($"'{root.name}' is not drawn in the {label}");
        }
        if (on.Problem == null)
            SaveEye(on, shot);
        string note = string.Join("; ", notes);
        if (fails.Count > 0)
            Fail(string.Join("; ", fails));
        stepNote = string.IsNullOrEmpty(stepNote) ? note : stepNote + "; " + note;
    }

    /// <summary>Waits until <paramref name="root"/> stops changing. A modal fades in by Lerp (Zorro.UI.Modal/Modal.cs:80)
    /// and its plate is added once the fade lifts the content past 1 % (<see cref="BackplateView.Counts"/>), so reads taken
    /// as it opens hold the panel's own change in the noise (#131). This watches the canvas, never the pixels, so an
    /// undrawn panel still settles and fails at the reads.</summary>
    IEnumerator PanelSettled(Canvas root)
    {
        var steady = new SteadyFrames();
        var graphics = new List<UnityEngine.UI.Graphic>();
        float end = Now + DrawnPixels.SettleSeconds;
        while (steady.Feed(PanelLook(root, graphics)) < DrawnPixels.SettledFrames)
        {
            if (Now > end)
            {
                Fail(
                    $"'{root.name}' still changing after {DrawnPixels.SettleSeconds:0} s, {graphics.Count} graphics"
                );
                yield break;
            }
            yield return null;
        }
    }

    /// <summary>What is on a panel: each drawn graphic and its alpha to a sixteenth, fine enough for a fade and coarse
    /// enough that its last slow steps end. A plate's own colour is left out: <see cref="BackdropProbe"/> eases it for
    /// as long as the scene behind it moves.</summary>
    static int PanelLook(Canvas root, List<UnityEngine.UI.Graphic> graphics)
    {
        root.GetComponentsInChildren(false, graphics);
        int look = 17;
        foreach (var g in graphics)
        {
            if (!g.isActiveAndEnabled)
                continue;
            float alpha = g.canvasRenderer.GetAlpha() * g.canvasRenderer.GetInheritedAlpha();
            look = unchecked(look * 31 + g.GetInstanceID());
            look = unchecked(look * 31 + Mathf.RoundToInt(alpha * 16f));
        }
        return look;
    }

    /// <summary>A layer no project setting names that the camera draws, or -1.</summary>
    static int FreeLayer(Camera cam)
    {
        for (int i = 31; i > 7; i--)
            if (string.IsNullOrEmpty(LayerMask.LayerToName(i)) && (cam.cullingMask & (1 << i)) != 0)
                return i;
        return -1;
    }

    private sealed class EyeImages
    {
        public readonly byte[][] Pixels;
        public readonly Matrix4x4[] Views;
        public readonly Matrix4x4[] Projections;
        public readonly Rect[] Viewports;
        public int Width;
        public int Height;
        public int Pending;
        public string Problem;

        public EyeImages(int passes)
        {
            Pixels = new byte[passes][];
            Views = new Matrix4x4[passes];
            Projections = new Matrix4x4[passes];
            Viewports = new Rect[passes];
        }
    }

    /// <summary>One frame of every eye target, read when the render context ends: endCameraRendering fires after the
    /// first eye pass, with the second eye's target still holding an older frame. A frame under a fade is skipped: its
    /// blink would read as a change in whatever two reads compare (a red on #5 was one), and
    /// <see cref="EyeImages.Problem"/> names a fade that outlasts <paramref name="seconds"/>.</summary>
    static IEnumerator ReadEyes(
        Camera cam,
        UnityEngine.XR.XRDisplaySubsystem display,
        EyeImages into,
        float seconds = EyeReadSeconds
    )
    {
        bool started = false;
        float faded = 0f;
        Action<ScriptableRenderContext, List<Camera>> hook = null;
        hook = (_, cameras) =>
        {
            if (!cameras.Contains(cam))
                return;
            if (FadeView.Alpha > FadedAbove)
            {
                faded = FadeView.Alpha;
                return;
            }
            RenderPipelineManager.endContextRendering -= hook;
            started = true;
            for (int i = 0; i < into.Pixels.Length; i++)
            {
                int eye = i;
                var record = EyeMatrixFeature.Eyes[eye & 1];
                into.Views[eye] = record.View;
                into.Projections[eye] = record.Proj;
                into.Viewports[eye] = record.Viewport;
                var target = display.GetRenderTextureForRenderPass(eye);
                if (target == null)
                {
                    into.Problem = $"render pass {eye} has no render texture";
                    continue;
                }
                into.Width = target.width;
                into.Height = target.height;
                into.Pending++;
                AsyncGPUReadback.Request(
                    target,
                    0,
                    TextureFormat.RGBA32,
                    request =>
                    {
                        into.Pending--;
                        if (request.hasError)
                            into.Problem =
                                $"eye target {target.width}x{target.height} {target.graphicsFormat}, AA {target.antiAliasing}: readback failed";
                        else
                            into.Pixels[eye] = request
                                .GetData<byte>(Mathf.Min(eye, request.layerCount - 1))
                                .ToArray();
                    }
                );
            }
        };
        RenderPipelineManager.endContextRendering += hook;
        try
        {
            float deadline = Time.unscaledTime + seconds;
            while ((!started || into.Pending > 0) && Time.unscaledTime < deadline)
                yield return null;
        }
        finally
        {
            RenderPipelineManager.endContextRendering -= hook;
        }
        if (!started || into.Pending > 0)
            into.Problem ??=
                started ? "eye readback never came back"
                : faded > 0f
                    ? F($"every frame for {seconds:0.0} s was inside a fade (drawn {faded:0.00})")
                : "the head camera did not render";
    }

    void SaveEye(EyeImages eyes, string shot, int eye = 0)
    {
        if (eyes.Pixels[eye] == null)
            return;
        var png = ImageConversion.EncodeArrayToPNG(
            eyes.Pixels[eye],
            GraphicsFormat.R8G8B8A8_UNorm,
            (uint)eyes.Width,
            (uint)eyes.Height
        );
        File.WriteAllBytes(Path.Combine(shotDir, SelfTestReport.SafeName(shot) + ".png"), png);
    }
}
