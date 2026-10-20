using PeakXR.UI.World;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PeakXR.UI.RunFlow;

/// <summary>
/// End cutscene and credits on a world-locked 16:9 screen instead of the headset. The win cutscene
/// turns MainCamera off (PeakHandler.cs:77) and plays on scene cameras that move on their own; strapped
/// to the HMD that is forced camera motion. The active cutscene camera is redirected into a texture
/// owned here and shown on a screen placed once in front of the player. Its target texture and eye
/// mask are restored when it changes or the cutscene ends. No per-frame allocation.
/// </summary>
internal sealed class CinemaScreen
{
    private const int TextureWidth = 1920;
    private const int TextureHeight = 1080;
    private const float Distance = 3f;
    private const float ReplaceDistance = 8f;
    private const float ScanInterval = 0.5f;
    private const float LostRescanInterval = 0.1f;

    private readonly RunFlowSettings cfg;
    private Camera[] buffer = new Camera[32];
    private RenderTexture texture;
    private GameObject screen;
    private Renderer screenRenderer;
    private Material material;
    private Camera current;
    private RenderTexture savedTarget;
    private StereoTargetEyeMask savedEye;
    private float lastScan = float.NegativeInfinity;
    private bool placed;

    public CinemaScreen(RunFlowSettings cfg) => this.cfg = cfg;

    /// <summary>True once a cutscene camera feeds the screen; stays true (last frame shown) between
    /// camera cuts until <see cref="Stop"/>.</summary>
    public bool Showing { get; private set; }

    public void Enable() => RenderPipelineManager.beginCameraRendering += OnBeginCamera;

    public void Disable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        Stop();
    }

    /// <summary>Call every frame while a cutscene or the credits run in VR.</summary>
    public void Tick(VRRig rig)
    {
        if (rig == null || rig.Head == null)
            return;
        var now = Time.unscaledTime;
        var lost = current == null || !current.isActiveAndEnabled;
        if (now - lastScan >= ScanInterval || (lost && now - lastScan >= LostRescanInterval))
        {
            lastScan = now;
            var best = FindCutsceneCamera(rig);
            if (best != null && best != current)
                Redirect(best);
        }
        if (Showing)
            Place(rig.Head);
    }

    /// <summary>Hand the camera back, hide the screen and free the texture.</summary>
    public void Stop()
    {
        Restore();
        Showing = false;
        placed = false;
        lastScan = float.NegativeInfinity;
        if (screen != null)
            screen.SetActive(false);
        if (texture != null)
        {
            if (material != null)
                material.mainTexture = null;
            texture.Release();
            Object.Destroy(texture);
            texture = null;
        }
    }

    private Camera FindCutsceneCamera(VRRig rig)
    {
        if (Camera.allCamerasCount > buffer.Length)
            buffer = new Camera[Camera.allCamerasCount * 2];
        var count = Camera.GetAllCameras(buffer);
        Camera best = null;
        for (int i = 0; i < count; i++)
        {
            var cam = buffer[i];
            buffer[i] = null;
            if (!IsCandidate(cam, rig))
                continue;
            if (best == null || cam.depth > best.depth)
                best = cam;
        }
        return best;
    }

    private bool IsCandidate(Camera cam, VRRig rig)
    {
        if (cam == null || !cam.isActiveAndEnabled || cam.cameraType != CameraType.Game)
            return false;
        if (cam == current)
            return true;
        if (
            cam == rig.HeadCamera
            || cam == LaserPointer.EventCamera
            || cam.targetTexture != null
            || cam.TryGetComponent<MainCamera>(out _)
        )
            return false;
        return !cam.TryGetComponent<UniversalAdditionalCameraData>(out var data)
            || data.renderType != CameraRenderType.Overlay;
    }

    private void Redirect(Camera cam)
    {
        Restore();
        EnsureScreen();
        current = cam;
        savedTarget = cam.targetTexture;
        savedEye = cam.stereoTargetEye;
        // A camera with a target texture and no eye mask renders mono, never to the headset.
        cam.stereoTargetEye = StereoTargetEyeMask.None;
        cam.targetTexture = texture;
        Showing = true;
        Plugin.Log.LogInfo($"[ui.runflow] cutscene camera '{cam.name}' shown on the cinema screen");
    }

    private void Restore()
    {
        if (current != null && current.targetTexture == texture)
        {
            current.targetTexture = savedTarget;
            current.stereoTargetEye = savedEye;
        }
        current = null;
        savedTarget = null;
    }

    private void EnsureScreen()
    {
        if (texture == null)
        {
            texture = new RenderTexture(TextureWidth, TextureHeight, 24)
            {
                name = "PeakXR_CinemaScreen",
            };
            texture.Create();
        }
        if (screen == null)
        {
            screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = "PeakXR_CinemaScreen";
            Object.Destroy(screen.GetComponent<Collider>());
            Object.DontDestroyOnLoad(screen);
            // Built-in shaders that every player build includes, unlike URP's Unlit.
            var shader = Shader.Find("UI/Default");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            material = new Material(shader) { renderQueue = 3000 };
            // UI/Default reads its depth test from here; keep the screen behind nearer geometry.
            material.SetInt("unity_GUIZTestMode", (int)CompareFunction.LessEqual);
            screenRenderer = screen.GetComponent<Renderer>();
            screenRenderer.sharedMaterial = material;
            screenRenderer.shadowCastingMode = ShadowCastingMode.Off;
            screenRenderer.receiveShadows = false;
        }
        material.mainTexture = texture;
        screen.SetActive(true);
    }

    /// <summary>World-locked: placed once in front of the head, again only after a big move.</summary>
    private void Place(Transform head)
    {
        var t = screen.transform;
        if (placed && (t.position - head.position).sqrMagnitude < ReplaceDistance * ReplaceDistance)
            return;
        var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (forward.sqrMagnitude < 1e-4f)
            forward = Vector3.ProjectOnPlane(head.up, Vector3.up);
        if (forward.sqrMagnitude < 1e-4f)
            forward = Vector3.forward;
        forward.Normalize();
        var width = cfg.CutsceneScreenWidth.Value;
        t.SetPositionAndRotation(
            head.position + forward * Distance,
            Quaternion.LookRotation(forward, Vector3.up)
        );
        t.localScale = new Vector3(width, width * TextureHeight / TextureWidth, 1f);
        placed = true;
    }

    /// <summary>The cutscene camera must not film its own screen (feedback loop).</summary>
    private void OnBeginCamera(ScriptableRenderContext context, Camera cam)
    {
        if (screenRenderer != null)
            screenRenderer.enabled = cam != current;
    }
}
