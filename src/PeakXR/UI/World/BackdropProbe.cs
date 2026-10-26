using PeakXR.Core;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PeakXR.UI.World;

/// <summary>
/// Measures what is behind each menu plate as the eye sees it, so the plate is only as dark as the background needs
/// (<c>UI.MenuBackplateAdaptive</c>, <see cref="BackdropContrast"/>): full over sunlit snow or sky, light over a dark
/// interior. A hidden 32x32 camera at the eye, framed on the plate and starting just short of it, renders the scene
/// behind it (no hands, laser, fade sphere or menu, no shadows or AA) once every <see cref="ProbeSeconds"/>, one
/// plate per probe; an async GPU readback brings the pixels back without a stall. The plate then eases toward the
/// measured opacity every frame: quickly when it must darken, slowly when it may lighten. Nothing allocates per frame.
/// </summary>
internal sealed class BackdropProbe
{
    private const int Pixels = 32;
    private const float ProbeSeconds = 0.5f;

    /// <summary>The probe's near plane, as a share of the eye-to-plate distance: everything in front of the plate
    /// (hands, laser, the menu itself) stays out of the measure.</summary>
    private const float NearShare = 0.9f;

    /// <summary>Frames to wait for the probe's render or readback before giving up on it.</summary>
    private const int RenderFrames = 3;
    private const int ReadbackFrames = 30;

    /// <summary>Unity's built-in UI layer.</summary>
    private const int UiLayer = 5;

    private readonly BackdropContrast.Meter meter = new();
    private readonly Vector3[] corners = new Vector3[4];
    private readonly System.Action<AsyncGPUReadbackRequest> onReadback;
    private Camera cam;
    private RenderTexture texture;
    private TrackedCanvas rendering,
        reading;
    private int startedFrame;
    private int next;
    private float probeAt;

    public BackdropProbe() => onReadback = OnReadback;

    /// <summary>Eases every shown plate toward its measured opacity, and starts the next probe when due.</summary>
    public void Tick(VRRig rig, UIWorldSettings cfg, float dt)
    {
        var list = WorldCanvases.Tracked;
        bool adaptive = cfg.MenuBackplateAdaptive.Value;
        var max = cfg.MenuBackplateOpacity.Value;
        for (int i = 0; i < list.Count; i++)
        {
            var t = list[i];
            if (!adaptive || t.Plate == null || !t.Plate.gameObject.activeInHierarchy)
            {
                // A plate that shows again starts at the full setting and lightens once measured: never too thin.
                t.PlateAlpha = t.PlateTarget = -1f;
                continue;
            }
            if (t.PlateTarget < 0f)
                continue;
            var from = t.PlateAlpha < 0f ? max : t.PlateAlpha;
            t.PlateAlpha = BackdropContrast.Approach(from, Mathf.Min(t.PlateTarget, max), dt);
            // Whole 8-bit steps, so the colour settles and stops dirtying the canvas.
            BackplateView.SetAlpha(t, Mathf.Round(BackplateView.Alpha(t, cfg) * 255f) / 255f);
        }

        if (rendering != null && Time.frameCount - startedFrame > RenderFrames)
        {
            // The camera never rendered (scene change, pipeline skipped it): try again later.
            cam.enabled = false;
            rendering = null;
        }
        if (reading != null && Time.frameCount - startedFrame > ReadbackFrames)
            reading = null;
        if (!adaptive || rendering != null || reading != null || Time.unscaledTime < probeAt)
            return;
        for (int n = 0; n < list.Count; n++)
        {
            next = (next + 1) % list.Count;
            var t = list[next];
            if (t.Plate != null && t.Plate.gameObject.activeInHierarchy && Frame(t, rig))
            {
                probeAt = Time.unscaledTime + ProbeSeconds;
                return;
            }
        }
    }

    public void Release()
    {
        RenderPipelineManager.endCameraRendering -= OnEndCamera;
        if (cam != null)
            Object.Destroy(cam.gameObject);
        if (texture != null)
            Object.Destroy(texture);
        cam = null;
        texture = null;
        rendering = reading = null;
    }

    /// <summary>Frames the probe on <paramref name="t"/>'s plate from the eye and lets it render this frame.</summary>
    private bool Frame(TrackedCanvas t, VRRig rig)
    {
        if (rig.Head == null)
            return false;
        t.Plate.rectTransform.GetWorldCorners(corners);
        var center = (corners[0] + corners[2]) * 0.5f;
        float width = Vector3.Distance(corners[0], corners[3]);
        float height = Vector3.Distance(corners[0], corners[1]);
        var eye = rig.Head.position;
        var to = center - eye;
        float distance = to.magnitude;
        if (distance < 0.2f || width < 1e-3f || height < 1e-3f)
            return false;
        if (cam == null && !Create())
            return false;

        Camera main = null;
        if (MainCamera.instance != null)
            MainCamera.instance.TryGetComponent(out main);
        int mask = main != null ? main.cullingMask : ~0;
        cam.cullingMask = mask & ~(1 << UiLayer) & ~(1 << t.Canvas.gameObject.layer);
        if (main != null)
        {
            cam.clearFlags = main.clearFlags;
            cam.backgroundColor = main.backgroundColor;
            cam.farClipPlane = main.farClipPlane;
        }
        cam.nearClipPlane = distance * NearShare;
        cam.fieldOfView = 2f * Mathf.Atan(height * 0.5f / distance) * Mathf.Rad2Deg;
        cam.aspect = width / height;
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(to, Vector3.up));
        cam.enabled = true;
        rendering = t;
        startedFrame = Time.frameCount;
        return true;
    }

    private bool Create()
    {
        if (!SystemInfo.supportsAsyncGPUReadback)
            return false;
        texture = new RenderTexture(
            Pixels,
            Pixels,
            24,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.sRGB
        )
        {
            name = "PeakXR.BackdropProbe",
        };
        texture.Create();
        var go = new GameObject("PeakXR.BackdropProbe.Camera");
        Object.DontDestroyOnLoad(go);
        cam = go.AddComponent<Camera>();
        cam.enabled = false;
        cam.stereoTargetEye = StereoTargetEyeMask.None;
        cam.targetTexture = texture;
        // As the binocular lens: the pipeline's default renderer keeps the game's fog; shadows and AA are not worth a
        // 32 px measure. Post-processing follows the main camera, so the probe sees the colours the eye does.
        var data = cam.GetUniversalAdditionalCameraData();
        data.renderType = CameraRenderType.Base;
        data.allowXRRendering = false;
        data.volumeTrigger = go.transform;
        data.renderShadows = false;
        data.antialiasing = AntialiasingMode.None;
        if (
            MainCamera.instance != null
            && MainCamera.instance.TryGetComponent(out UniversalAdditionalCameraData mainData)
        )
        {
            data.renderPostProcessing = mainData.renderPostProcessing;
            data.volumeLayerMask = mainData.volumeLayerMask;
        }
        RenderPipelineManager.endCameraRendering -= OnEndCamera;
        RenderPipelineManager.endCameraRendering += OnEndCamera;
        return true;
    }

    private void OnEndCamera(ScriptableRenderContext context, Camera camera)
    {
        if (camera != cam || rendering == null)
            return;
        cam.enabled = false;
        reading = rendering;
        rendering = null;
        AsyncGPUReadback.Request(texture, 0, TextureFormat.RGBA32, onReadback);
    }

    private void OnReadback(AsyncGPUReadbackRequest request)
    {
        var t = reading;
        reading = null;
        if (t == null || request.hasError)
            return;
        var data = request.GetData<byte>();
        meter.Clear();
        for (int i = 0; i + 2 < data.Length; i += 4)
            meter.Add(data[i], data[i + 1], data[i + 2]);
        // Unclamped by the setting here; BackplateView.Alpha caps it when drawn.
        t.PlateTarget = BackdropContrast.NeededOpacity(meter.Bright(), 1f);
    }
}
