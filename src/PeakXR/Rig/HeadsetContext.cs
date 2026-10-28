using System.Collections.Generic;
using UnityEngine;

namespace PeakXR.Rig;

/// <summary>
/// Tells the frame's headset render apart from the other render contexts URP runs: every manual
/// <c>Camera.Render</c> (guidebook pages, hero title card, mirage, photobooth) is a context of its own, and
/// per-context hooks (desktop mirror, held-item scale) only need the one that draws the eyes.
/// </summary>
internal static class HeadsetContext
{
    /// <summary>True when <paramref name="cameras"/> holds a camera that renders to the headset: a stereo eye mask
    /// and no target texture. Allocation-free.</summary>
    public static bool Renders(List<Camera> cameras)
    {
        for (int i = 0; i < cameras.Count; i++)
        {
            var cam = cameras[i];
            if (
                cam != null
                && cam.stereoTargetEye != StereoTargetEyeMask.None
                && cam.targetTexture == null
            )
                return true;
        }
        return false;
    }
}
