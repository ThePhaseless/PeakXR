using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using PeakXR.Core;
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

    /// <summary>vr.02c: the panel on show at the title, the connection popup offline or the title menu otherwise, is
    /// in both eyes' pixels.</summary>
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
        yield return PanelInEyes(root, "vr-02c-title");
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
        public int Width;
        public int Height;
        public int Pending;
        public string Problem;

        public EyeImages(int passes) => Pixels = new byte[passes][];
    }

    /// <summary>One frame of every eye target, read when the render context ends: endCameraRendering fires after the
    /// first eye pass, with the second eye's target still holding an older frame.</summary>
    static IEnumerator ReadEyes(
        Camera cam,
        UnityEngine.XR.XRDisplaySubsystem display,
        EyeImages into
    )
    {
        bool started = false;
        Action<ScriptableRenderContext, List<Camera>> hook = null;
        hook = (_, cameras) =>
        {
            if (!cameras.Contains(cam))
                return;
            RenderPipelineManager.endContextRendering -= hook;
            started = true;
            for (int i = 0; i < into.Pixels.Length; i++)
            {
                int eye = i;
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
            float deadline = Time.unscaledTime + EyeReadSeconds;
            while ((!started || into.Pending > 0) && Time.unscaledTime < deadline)
                yield return null;
        }
        finally
        {
            RenderPipelineManager.endContextRendering -= hook;
        }
        if (!started || into.Pending > 0)
            into.Problem ??= started
                ? "eye readback never came back"
                : "the head camera did not render";
    }

    void SaveEye(EyeImages eyes, string shot)
    {
        if (eyes.Pixels[0] == null)
            return;
        var png = ImageConversion.EncodeArrayToPNG(
            eyes.Pixels[0],
            GraphicsFormat.R8G8B8A8_UNorm,
            (uint)eyes.Width,
            (uint)eyes.Height
        );
        File.WriteAllBytes(Path.Combine(shotDir, SelfTestReport.SafeName(shot) + ".png"), png);
    }
}
