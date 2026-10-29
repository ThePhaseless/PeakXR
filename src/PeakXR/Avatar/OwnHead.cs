using System.Collections.Generic;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakXR.Avatar;

/// <summary>
/// The local VR player's head, face and hats are kept out of the eyes by drawing them shadow-only
/// (<see cref="AvatarPatches.HideBody"/>). That mode holds for every camera, so mirrors (Mirror.mirrorCamera,
/// Mirror.cs:7) and the airport photobooth (AirportPhotoboothKiosk.cs:11, :13) showed a headless body. Each game
/// camera other than the eye camera draws them again for its own render and puts them back when it ends, so only
/// the eyes skip the head. Render time only: the network and other players never see a change.
/// </summary>
internal static class OwnHead
{
    // Eye-mounted cameras (an overlay on the eye, a camera on the head) sit within 10 cm of the eye camera.
    private const float EyeRadiusSqr = 0.1f * 0.1f;

    private static readonly List<Renderer> hidden = new();

    /// <summary>The camera that drew the head in the last game-camera render, for the self-test.</summary>
    internal static Camera LastShownTo { get; private set; }

    /// <summary>The head renderers the eyes skip right now (empty while the head is shown or in flat play).</summary>
    internal static IReadOnlyList<Renderer> Hidden => hidden;

    public static void Install()
    {
        RenderPipelineManager.beginCameraRendering -= Begin;
        RenderPipelineManager.endCameraRendering -= End;
        RenderPipelineManager.beginCameraRendering += Begin;
        RenderPipelineManager.endCameraRendering += End;
    }

    /// <summary>Called by <see cref="AvatarPatches.HideBody"/> for each renderer it made shadow-only.</summary>
    internal static void Track(Renderer r)
    {
        if (!hidden.Contains(r))
            hidden.Add(r);
    }

    /// <summary>Called when the head is drawn for every camera again.</summary>
    internal static void Clear() => hidden.Clear();

    /// <summary>Whether <paramref name="cam"/> sees from the player's eyes: the rig's head camera or one mounted
    /// on it.</summary>
    internal static bool IsEye(Camera cam)
    {
        var rig = VRRig.Instance;
        var eye = rig != null ? rig.HeadCamera : null;
        if (eye == null)
            return false;
        return cam == eye
            || (cam.transform.position - eye.transform.position).sqrMagnitude
                < EyeRadiusSqr * rig.Scale * rig.Scale;
    }

    private static void Begin(ScriptableRenderContext context, Camera cam)
    {
        if (hidden.Count == 0 || cam == null)
            return;
        bool show = cam.cameraType == CameraType.Game && !IsEye(cam);
        Set(show ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly);
        if (show)
            LastShownTo = cam;
    }

    private static void End(ScriptableRenderContext context, Camera cam)
    {
        if (hidden.Count > 0)
            Set(ShadowCastingMode.ShadowsOnly);
    }

    private static void Set(ShadowCastingMode mode)
    {
        for (int i = 0; i < hidden.Count; i++)
        {
            var r = hidden[i];
            if (r != null && r.shadowCastingMode != mode)
                r.shadowCastingMode = mode;
        }
    }
}
