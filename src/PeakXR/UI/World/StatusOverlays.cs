using System;
using System.Collections.Generic;
using HarmonyLib;
using PeakXR.Core;
using PeakXR.Render;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.UI.World;

/// <summary>
/// Status-effect quads (<see cref="CameraQuad"/>) size themselves to the mono camera frustum, which is
/// wrong for each eye. In VR they become real head-locked quads just past the near plane, big enough to
/// cover any headset FOV. Re-posed right before rendering so they do not lag behind head motion. That pose is centred
/// on the head, 6 cm away on the default near plane, so a pattern read from the quad's UV sits 0.063 / 0.06 = 1.05 tan
/// apart between the eyes (measured 1.05 in vr.480, issue #5), so
/// <see cref="EyeMatrixFeature"/> then moves them ahead of each eye before that eye's cull (Core
/// <see cref="EyeOverlayFit"/>). The size stays, so density and strength do; with no XR pass or
/// <c>Render.EyeLockedOverlays</c> off, the head-centred pose is the picture.
/// </summary>
internal static class StatusOverlays
{
    private const float Cover = 6f;
    private static readonly List<Transform> placed = new();

    /// <summary>Quads seen while the setting is on, with their renderers, so a frame needs no <c>GetComponent</c>. A null
    /// renderer stays in here and is skipped.</summary>
    private static readonly Dictionary<Transform, Renderer> optedOut = new();

    private static int placedFrame = -1;
    private static Action<Renderer, bool> allowGpuDriven;
    private static bool setterResolved;

    public static void Place(Transform quad, VRRig rig, StatusOverlayMode mode)
    {
        if (mode == StatusOverlayMode.Off)
        {
            quad.localScale = Vector3.zero;
            return;
        }
        if (placedFrame != Time.frameCount)
        {
            placed.Clear();
            placedFrame = Time.frameCount;
        }
        OptOutOfResidentDrawer(quad);
        Pose(quad, rig);
        placed.Add(quad);
    }

    public static void Repose(VRRig rig)
    {
        if (placedFrame != Time.frameCount)
            return;
        foreach (var quad in placed)
            if (quad != null)
                Pose(quad, rig);
    }

    /// <summary>Moves this frame's quads ahead of the eye that is about to cull, parallel to the head.</summary>
    public static void PoseForEye(Matrix4x4 view, Camera cam)
    {
        if (placedFrame != Time.frameCount || cam == null)
            return;
        Vector3 eye = view.inverse.GetColumn(3);
        var head = cam.transform.rotation;
        var p = EyeOverlayFit.Position(
            new System.Numerics.Vector3(eye.x, eye.y, eye.z),
            new System.Numerics.Quaternion(head.x, head.y, head.z, head.w),
            cam.nearClipPlane + 0.03f
        );
        var position = new Vector3(p.X, p.Y, p.Z);
        foreach (var quad in placed)
            if (quad != null)
                quad.SetPositionAndRotation(position, head);
    }

    private static void Pose(Transform quad, VRRig rig)
    {
        var head = rig.Head;
        var near = rig.HeadCamera != null ? rig.HeadCamera.nearClipPlane : 0.05f;
        var distance = near + 0.03f;
        quad.SetPositionAndRotation(head.position + head.forward * distance, head.rotation);
        quad.localScale = new Vector3(distance * Cover, distance * Cover, 1f);
    }

    /// <summary>The GPU Resident Drawer takes a renderer's transform once a frame (GPUResidentDrawer.cs:458, :583-586), so
    /// a quad it holds would keep one pose for both eyes. URP opts renderers out the same way
    /// (DisallowGPUDrivenRendering.cs:57). With the setting off they go back, so off is today's picture.</summary>
    private static void OptOutOfResidentDrawer(Transform quad)
    {
        // ?.: render.fixes can roll back alone, and a throw here would take ui.world with it through PatchGuard.
        if (RenderFixesFeature.EyeLockedOverlays?.Value != true)
        {
            if (optedOut.Count > 0)
                OptBackIn();
            return;
        }
        if (optedOut.ContainsKey(quad))
            return;
        var renderer = quad.GetComponent<Renderer>();
        optedOut[quad] = renderer;
        if (renderer != null)
            AllowGpuDriven()?.Invoke(renderer, false);
    }

    private static void OptBackIn()
    {
        var set = AllowGpuDriven();
        if (set != null)
            foreach (var renderer in optedOut.Values)
                if (renderer != null)
                    set(renderer, true);
        optedOut.Clear();
    }

    /// <summary>The internal <c>Renderer.allowGPUDrivenRendering</c> setter, or null with one log line: self-test vr.480's
    /// P4 then shows whether the quads still hold one pose.</summary>
    private static Action<Renderer, bool> AllowGpuDriven()
    {
        if (setterResolved)
            return allowGpuDriven;
        setterResolved = true;
        try
        {
            var setter = AccessTools.PropertySetter(typeof(Renderer), "allowGPUDrivenRendering");
            if (setter != null)
                allowGpuDriven = AccessTools.MethodDelegate<Action<Renderer, bool>>(setter);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[ui.world] {e.GetType().Name}: {e.Message}");
        }
        if (allowGpuDriven == null)
            Plugin.Log.LogWarning(
                "[ui.world] Renderer.allowGPUDrivenRendering is not there to set: head-locked overlays stay in the resident drawer"
            );
        return allowGpuDriven;
    }
}
