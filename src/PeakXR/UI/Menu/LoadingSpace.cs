using PeakXR.UI.Common;
using PeakXR.UI.World;
using PeakXR.VR;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace PeakXR.UI.Menu;

/// <summary>
/// A small 3D room shown while a level loads: dark void, gradient walls, a fading floor grid, the
/// game's loading screen as a floating panel and a progress bar. It has its own head-tracked camera,
/// far below the world and on top of every other camera, so tracking stays alive even while the old
/// scene is torn down and the new one has no camera yet. Everything is built at runtime from UI
/// graphics (no game assets, no URP shaders that might be stripped).
/// </summary>
[DefaultExecutionOrder(30500)]
internal sealed class LoadingSpace : MonoBehaviour, ICanvasPlacement
{
    private static readonly Vector3 Center = new(0f, -10000f, 0f);
    private static readonly Color Void = new(0.02f, 0.025f, 0.035f, 1f);
    private static readonly Color Horizon = new(0.11f, 0.12f, 0.17f, 1f);
    private static readonly Color Accent = new(0.95f, 0.78f, 0.36f, 1f);
    private const float PanelDistance = 3f;
    private const float PanelWidth = 2.4f;
    private const float EyeHeight = 1.6f;

    private GameObject space;
    private Transform origin;
    private Camera cam;
    private RectTransform progressFill;
    private Transform progressBar;
    private Quaternion facing = Quaternion.identity;
    private MainCamera hiddenMain;
    private Camera hiddenCam;
    private int hiddenMask;

    public bool Interactive => false;

    public bool Shown { get; private set; }

    public static LoadingSpace Create()
    {
        var go = new GameObject("PeakXR_LoadingSpace");
        DontDestroyOnLoad(go);
        var loading = go.AddComponent<LoadingSpace>();
        loading.Build();
        return loading;
    }

    public void Show()
    {
        // Face the panel the way the player is looking now, so nobody has to turn around.
        var yaw = VRInput.HeadTracked
            ? (VRInput.HeadRotation * Vector3.forward).HeadingDegrees()
            : 0f;
        facing = Quaternion.Euler(0f, yaw, 0f);
        PlaceProgress();
        TrackHead();
        space.SetActive(true);
        Shown = true;
        HideGameCamera();
    }

    public void Hide()
    {
        space.SetActive(false);
        Shown = false;
        RestoreGameCamera();
    }

    /// <summary>The game's camera (old scene, then the new one) keeps rendering under the room until the load
    /// ends, extra wait included (A/LoadingScreenHandler.cs:132). Cull everything so it costs almost nothing.
    /// Not <c>enabled = false</c>: game code reads <c>Camera.main</c> every frame without a null check
    /// (Zorro DistanceDisablerHandler.Update, SmallShadowHandler.Update).</summary>
    private void HideGameCamera()
    {
        var main = MainCamera.instance;
        if (main == hiddenMain)
            return;
        RestoreGameCamera();
        hiddenMain = main;
        hiddenCam = main != null ? main.GetComponent<Camera>() : null;
        if (hiddenCam == null)
            return;
        hiddenMask = hiddenCam.cullingMask;
        hiddenCam.cullingMask = 0;
    }

    private void RestoreGameCamera()
    {
        if (hiddenCam != null)
            hiddenCam.cullingMask = hiddenMask;
        hiddenMain = null;
        hiddenCam = null;
    }

    public PlacementResult Place(
        Canvas canvas,
        out Vector3 position,
        out Quaternion rotation,
        out float widthMeters
    )
    {
        PanelPose(out position, out rotation);
        widthMeters = PanelWidth;
        return Shown ? PlacementResult.Placed : PlacementResult.Hidden;
    }

    private void PanelPose(out Vector3 position, out Quaternion rotation)
    {
        position =
            origin.position + Vector3.up * EyeHeight + facing * new Vector3(0f, 0f, PanelDistance);
        rotation = facing;
    }

    private void OnEnable() => Application.onBeforeRender += TrackHead;

    private void OnDisable() => Application.onBeforeRender -= TrackHead;

    private void LateUpdate()
    {
        if (!Shown)
            return;
        TrackHead();
        UpdateProgress();
        // Runs after RigDriver (order 600) has bound and copied a new scene's camera.
        HideGameCamera();
    }

    private void TrackHead()
    {
        if (!VRInput.HeadTracked || cam == null)
            return;
        cam.transform.SetLocalPositionAndRotation(VRInput.HeadPosition, VRInput.HeadRotation);
    }

    /// <summary>Real progress when Photon drives the load, otherwise a sliding segment.</summary>
    private void UpdateProgress()
    {
        var progress = PhotonNetwork.LevelLoadingProgress;
        if (progress > 0f && progress < 1f)
        {
            progressFill.anchorMin = Vector2.zero;
            progressFill.anchorMax = new Vector2(progress, 1f);
        }
        else
        {
            var start = Mathf.PingPong(Time.unscaledTime * 0.6f, 0.8f);
            progressFill.anchorMin = new Vector2(start, 0f);
            progressFill.anchorMax = new Vector2(start + 0.2f, 1f);
        }
    }

    private void Build()
    {
        space = new GameObject("Space");
        space.transform.SetParent(transform, false);
        space.transform.position = Center;
        origin = space.transform;

        var camGo = new GameObject("LoadingCamera");
        camGo.transform.SetParent(origin, false);
        camGo.transform.localPosition = new Vector3(0f, EyeHeight, 0f);
        cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Void;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 200f;
        cam.depth = 100f;
        cam.stereoTargetEye = StereoTargetEyeMask.Both;
        var data = cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = false;
        data.renderShadows = false;
        data.antialiasing = AntialiasingMode.None;
        // Renderer 1 (URP_RenderTexture) has no full-screen features, so the game's fog pass cannot grey out the room.
        if (
            UniversalRenderPipeline.asset != null
            && UniversalRenderPipeline.asset.rendererDataList.Length > 1
        )
            data.SetRenderer(1);
        FadeSphere.Attach(camGo.transform);

        BuildFloor();
        BuildWalls();
        BuildProgress();
        space.SetActive(false);
    }

    private void BuildFloor()
    {
        var floor = WorldCanvas("Floor", new Vector2(4000f, 4000f), 0.01f);
        floor.transform.localPosition = Vector3.zero;
        floor.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        var grid = Image<RawImage>(floor.transform, "Grid");
        grid.texture = GridTexture();
        grid.uvRect = new Rect(0f, 0f, 40f, 40f);
        Stretch(grid.rectTransform);

        var fade = Image<RawImage>(floor.transform, "HorizonFade");
        fade.texture = RadialFadeTexture();
        Stretch(fade.rectTransform);
    }

    private void BuildWalls()
    {
        var gradient = VerticalGradientTexture();
        for (int i = 0; i < 4; i++)
        {
            var rotation = Quaternion.Euler(0f, 90f * i, 0f);
            var wall = WorldCanvas("Wall" + i, new Vector2(5000f, 3000f), 0.01f);
            wall.transform.SetLocalPositionAndRotation(
                rotation * new Vector3(0f, 15f, 25f),
                rotation
            );
            var image = Image<RawImage>(wall.transform, "Sky");
            image.texture = gradient;
            Stretch(image.rectTransform);
        }
    }

    private void BuildProgress()
    {
        var bar = WorldCanvas("Progress", new Vector2(800f, 14f), 0.001f);
        progressBar = bar.transform;
        var back = Image<Image>(bar.transform, "Back");
        back.color = new Color(1f, 1f, 1f, 0.12f);
        Stretch(back.rectTransform);
        var fill = Image<Image>(bar.transform, "Fill");
        fill.color = Accent;
        progressFill = fill.rectTransform;
        progressFill.offsetMin = progressFill.offsetMax = Vector2.zero;
    }

    private void PlaceProgress()
    {
        PanelPose(out var panel, out var rotation);
        var below = PanelWidth * 9f / 16f * 0.5f + 0.12f;
        progressBar.SetPositionAndRotation(
            panel + Vector3.down * below - rotation * new Vector3(0f, 0f, 0.05f),
            rotation
        );
    }

    private Canvas WorldCanvas(string name, Vector2 size, float metersPerPixel)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(origin, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rect = (RectTransform)go.transform;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one * metersPerPixel;
        return canvas;
    }

    private static T Image<T>(Transform parent, string name)
        where T : Graphic
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var graphic = go.AddComponent<T>();
        graphic.raycastTarget = false;
        return graphic;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static Texture2D GridTexture()
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true)
        {
            wrapMode = TextureWrapMode.Repeat,
        };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            var line = x < 2 || y < 2;
            pixels[y * size + x] = line
                ? new Color32(140, 160, 210, 150)
                : new Color32(30, 36, 52, 255);
        }
        tex.SetPixels32(pixels);
        tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
        tex.anisoLevel = 8;
        return tex;
    }

    /// <summary>Clear in the middle, the void colour at the rim: the floor melts into the horizon.</summary>
    private static Texture2D RadialFadeTexture()
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
        {
            wrapMode = TextureWrapMode.Clamp,
        };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            var dx = (x + 0.5f) / size * 2f - 1f;
            var dy = (y + 0.5f) / size * 2f - 1f;
            var t = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.15f, 0.95f, Mathf.Sqrt(dx * dx + dy * dy))
            );
            pixels[y * size + x] = new Color(Horizon.r, Horizon.g, Horizon.b, t);
        }
        tex.SetPixels(pixels);
        tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        return tex;
    }

    private static Texture2D VerticalGradientTexture()
    {
        const int height = 64;
        var tex = new Texture2D(1, height, TextureFormat.RGBA32, mipChain: false)
        {
            wrapMode = TextureWrapMode.Clamp,
        };
        for (int y = 0; y < height; y++)
            tex.SetPixel(0, y, Color.Lerp(Horizon, Void, Mathf.Pow(y / (height - 1f), 0.6f)));
        tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        return tex;
    }
}
