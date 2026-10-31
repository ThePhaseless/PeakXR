using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PeakXR.Rig;

/// <summary>
/// Tells the frame's headset render apart from the other render contexts URP runs: every manual
/// <c>Camera.Render</c> (guidebook pages, hero title card, mirage, photobooth) is a context of its own, and
/// per-context hooks (desktop mirror, held-item scale) only need the one that draws the eyes.
/// </summary>
internal static class HeadsetContext
{
    /// <summary>True when <paramref name="cameras"/> holds a camera that renders to the headset
    /// (<see cref="ToHeadset"/>). Allocation-free.</summary>
    public static bool Renders(List<Camera> cameras)
    {
        for (int i = 0; i < cameras.Count; i++)
            if (ToHeadset(cameras[i]))
                return true;
        return false;
    }

    /// <summary>URP's own rule for drawing a camera into the eyes: <c>UniversalRenderPipeline.RenderCameraStack</c>
    /// passes <c>allowXRRendering</c> (true with no additional data, URP UniversalRenderPipeline.cs:794) to
    /// <c>XRLayout.AddCamera</c>, which adds the eye passes for a Game or VR camera with no target texture (Core RP
    /// XRLayout.cs:14). URP never reads <c>stereoTargetEye</c>. Allocation-free.</summary>
    public static bool ToHeadset(Camera cam) =>
        cam != null
        && cam.targetTexture == null
        && cam.cameraType is CameraType.Game or CameraType.VR
        && (
            !cam.TryGetComponent<UniversalAdditionalCameraData>(out var data)
            || data.allowXRRendering
        );
}
