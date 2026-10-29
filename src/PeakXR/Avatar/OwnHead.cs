using System.Collections.Generic;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakXR.Avatar;

/// <summary>
/// Keeps the local VR player's head, face and hats out of the eyes (<see cref="AvatarPatches.HideBody"/>). Each is
/// hidden by what it draws: one that casts a shadow is drawn shadow-only, so its shadow stays; one that casts none is
/// not rendered at all. The head mesh is the second kind (HeadMesh, ShadowCastingMode.Off in the Character prefab; its
/// shadow is the separate HeadMesh Shadow, CustomizationRefs.headShadow), so a shadow-only switch, which took only
/// renderers that cast, never hid it and left it to the shader's ghost. Every game camera other than the eye camera
/// draws them all again for its own render (mirrors, Mirror.cs:7, and the airport photobooth,
/// AirportPhotoboothKiosk.cs:11, :13) and the eye state returns when that render ends. Each renderer gets back exactly
/// the state it had before it was hidden, and nothing is touched unless the head is hidden, so flat play keeps the
/// game's own state. Render time only: the network and other players never see a change.
/// </summary>
internal static class OwnHead
{
    // Eye-mounted cameras (an overlay on the eye, a camera on the head) sit within 10 cm of the eye camera.
    private const float EyeRadiusSqr = 0.1f * 0.1f;

    private static readonly List<Renderer> hidden = new();
    private static readonly List<ShadowCastingMode> authored = new();

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

    /// <summary>
    /// Hides <paramref name="hide"/>'s head (HideTheBody.cs:10), face (:14, inactive parts too, so a third eye or a
    /// blind card switched on later is covered) and hats (CustomizationRefs.playerHats) from the eyes, or gives every
    /// renderer it hid back its own state. Runs on a toggle, never per frame.
    /// </summary>
    internal static void Set(HideTheBody hide, bool hideHead)
    {
        if (!hideHead)
        {
            Apply(eye: false);
            hidden.Clear();
            authored.Clear();
            return;
        }
        Add(hide.headRend);
        if (hide.face != null)
            foreach (var r in hide.face.GetComponentsInChildren<Renderer>(true))
                Add(r);
        if (hide.refs != null && hide.refs.playerHats != null)
            foreach (var r in hide.refs.playerHats)
                Add(r);
        Apply(eye: true);
    }

    private static void Add(Renderer r)
    {
        // A renderer the game draws shadow-only already shows the eyes nothing.
        if (r == null || r.shadowCastingMode == ShadowCastingMode.ShadowsOnly || hidden.Contains(r))
            return;
        hidden.Add(r);
        authored.Add(r.shadowCastingMode);
    }

    /// <summary>Whether <paramref name="r"/> is drawn into a camera's image right now (not its shadow alone).</summary>
    internal static bool Drawn(Renderer r) =>
        !r.forceRenderingOff && r.shadowCastingMode != ShadowCastingMode.ShadowsOnly;

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
        Apply(eye: !show);
        if (show)
            LastShownTo = cam;
    }

    private static void End(ScriptableRenderContext context, Camera cam)
    {
        if (hidden.Count > 0)
            Apply(eye: true);
    }

    /// <summary>The eye camera's state (<paramref name="eye"/>) or each renderer's own.</summary>
    private static void Apply(bool eye)
    {
        for (int i = 0; i < hidden.Count; i++)
        {
            var r = hidden[i];
            if (r == null)
                continue;
            var own = authored[i];
            bool off = eye && own == ShadowCastingMode.Off;
            var mode = eye && !off ? ShadowCastingMode.ShadowsOnly : own;
            if (r.shadowCastingMode != mode)
                r.shadowCastingMode = mode;
            if (r.forceRenderingOff != off)
                r.forceRenderingOff = off;
        }
    }
}
