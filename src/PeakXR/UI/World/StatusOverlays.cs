using System.Collections.Generic;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.UI.World;

/// <summary>
/// Status-effect quads (<see cref="CameraQuad"/>) size themselves to the mono camera frustum, which is
/// wrong for each eye. In VR they become real head-locked quads just past the near plane, big enough to
/// cover any headset FOV, so both eyes see them correctly. Re-posed right before rendering so they do not
/// lag behind head motion.
/// </summary>
internal static class StatusOverlays
{
    private const float Cover = 6f;
    private static readonly List<Transform> placed = new();

    public static void Place(Transform quad, VRRig rig, StatusOverlayMode mode)
    {
        if (mode == StatusOverlayMode.Off)
        {
            quad.localScale = Vector3.zero;
            return;
        }
        Pose(quad, rig);
        placed.Add(quad);
    }

    public static void Repose(VRRig rig)
    {
        foreach (var quad in placed)
            if (quad != null)
                Pose(quad, rig);
        placed.Clear();
    }

    private static void Pose(Transform quad, VRRig rig)
    {
        var head = rig.Head;
        var near = rig.HeadCamera != null ? rig.HeadCamera.nearClipPlane : 0.05f;
        var distance = near + 0.03f;
        quad.SetPositionAndRotation(head.position + head.forward * distance, head.rotation);
        quad.localScale = new Vector3(distance * Cover, distance * Cover, 1f);
    }
}
