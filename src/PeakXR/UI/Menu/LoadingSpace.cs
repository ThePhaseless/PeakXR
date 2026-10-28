using System.Collections.Generic;
using PeakXR.Core;
using PeakXR.UI.Common;
using PeakXR.UI.World;
using PeakXR.VR;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Zorro.Core;

namespace PeakXR.UI.Menu;

/// <summary>
/// A small 3D room shown while a level loads and during the boot splash: void, gradient walls, a fading
/// floor grid, the game's screen floating in it and a progress bar. The screen's flat backing card is hidden
/// (<see cref="ScreenBacking"/>) so its art floats in layers at slightly different depths, and the room takes
/// its colours from that backing (<see cref="LoadingPalette"/>), so every loading screen gets a fitting room.
/// It has its own head-tracked camera, far below the world and on top of every other camera, so tracking
/// stays alive even while the old scene is torn down and the new one has no camera yet. Everything is built at
/// runtime from UI graphics (no game assets, no URP shaders that might be stripped).
/// </summary>
[DefaultExecutionOrder(30500)]
internal sealed class LoadingSpace : MonoBehaviour, ICanvasPlacement
{
    private static readonly Vector3 Center = new(0f, -10000f, 0f);
    private const float PanelDistance = 3f;
    private const float PanelWidth = 2.4f;
    private const float EyeHeight = 1.6f;

    // Wall top = horizon x this; matches LoadingPalette's void so walls melt into the clear colour.
    private const float WallTop = 0.2f;

    // Floor cells relative to the line colour (cells come out at about the horizon colour).
    private const float GridCell = 0.23f;

    private const float BlendSeconds = 1.2f;

    // Floor half size, past the walls (25 m out): a smaller floor leaves a gap under the walls where the dark
    // void shows as a black band just below the horizon.
    private const float FloorHalf = 30f;

    // World-space UI sorts by order, then by distance: the floor's centre is nearer the eye than anything on it,
    // so without a lower order it is drawn over the progress bar and skip ring.
    private const int RoomOrder = DrawOrder.LoadingRoom;

    // Gap between the screen's top-level layers, back to front, and the most any screen spreads.
    private const float LayerStep = 0.06f;
    private const float MaxLayerSpread = 0.36f;

    // Skip ring: faint while the splash can be skipped, filling while a button is held.
    private const float SkipRingSize = 0.12f;

    // Most a loading screen's text may grow: past this it would cover the art it labels.
    private const float MaxTextGrowth = 3f;

    private GameObject space;
    private Transform origin;
    private Camera cam;
    private RectTransform progressFill;
    private Transform progressBar;
    private Image progressImage;
    private Transform skipRing;
    private Image skipBack;
    private Image skipFill;
    private readonly RawImage[] walls = new RawImage[4];
    private RawImage grid;
    private RawImage horizonFade;
    private Quaternion facing = Quaternion.identity;

    // The head's floor point when the room opened: the panel is centred on the player, wherever they stand.
    private Vector3 anchor;
    private MainCamera hiddenMain;
    private Camera hiddenCam;
    private int hiddenMask;
    private Camera splashCam;
    private int splashMask;

    // The current screen: hidden backing, inked dark art, lifted layers. Undone in Release.
    private readonly ScreenBacking backing = new();
    private readonly List<Rgba> fills = new();
    private readonly List<Graphic> kept = new();
    private readonly List<Graphic> inked = new();
    private readonly List<Color> inkedColors = new();
    private readonly List<Transform> layers = new();
    private readonly List<float> layerZ = new();
    private readonly List<Transform> grown = new();
    private readonly List<Vector3> grownScale = new();
    private readonly HashSet<int> seen = new();
    private static readonly Vector3[] Corners = new Vector3[4];

    // Where the screen floats: scaled and lifted so its art fits the view and stays above the floor.
    private LoadingLayout layout = new(PanelWidth / 1920f, EyeHeight, EyeHeight);
    private float canvasWidth = 1920f;
    private Rgba? destination;
    private Rgba? airportKey;

    private LoadingPalette fromPalette = LoadingPalette.Default;
    private LoadingPalette toPalette = LoadingPalette.Default;
    private LoadingPalette palette = LoadingPalette.Default;
    private float blendStart;
    private bool blending;

    public bool Interactive => false;

    public bool Shown { get; private set; }

    /// <summary>A loading room is up: ui.runflow holds its beach wake-up fade until it is gone.</summary>
    public static bool RoomUp { get; private set; }

    public static LoadingSpace Create()
    {
        var go = new GameObject("PeakXR_LoadingSpace");
        DontDestroyOnLoad(go);
        var loading = go.AddComponent<LoadingSpace>();
        loading.Build();
        return loading;
    }

    /// <summary>Shows the room. <paramref name="sceneCamera"/>: a scene camera that is not the game's
    /// <see cref="MainCamera"/> (the splash camera) to cull while the room covers it.</summary>
    public void Show(Camera sceneCamera = null)
    {
        // Face the panel the way the player is looking now, so nobody has to turn around.
        var yaw = VRInput.HeadTracked
            ? (VRInput.HeadRotation * Vector3.forward).HeadingDegrees()
            : 0f;
        facing = Quaternion.Euler(0f, yaw, 0f);
        var head = VRInput.HeadTracked ? VRInput.HeadPosition : Vector3.zero;
        anchor = new Vector3(head.x, 0f, head.z);
        PlaceProgress();
        progressBar.gameObject.SetActive(sceneCamera == null);
        skipRing.gameObject.SetActive(sceneCamera != null);
        SetSkipProgress(0f);
        TrackHead();
        // Shown from black: take the screen's colours at once, no visible blend.
        blending = false;
        ApplyPalette(toPalette);
        space.SetActive(true);
        Shown = RoomUp = true;
        HideGameCamera();
        if (sceneCamera != splashCam)
        {
            CullSplash(sceneCamera);
            // Adopt the screen again now that the splash camera's clear colour is known.
            Release();
        }
    }

    public void Hide()
    {
        space.SetActive(false);
        Shown = RoomUp = false;
        RestoreGameCamera();
        CullSplash(null);
    }

    /// <summary>Puts the current screen back as the game made it (VR stopped); screens are adopted again on
    /// their next placement.</summary>
    public void Release()
    {
        Undo();
        seen.Clear();
    }

    private void Undo()
    {
        backing.Restore();
        for (int i = 0; i < inked.Count; i++)
            if (inked[i] != null)
                inked[i].color = inkedColors[i];
        inked.Clear();
        inkedColors.Clear();
        for (int i = 0; i < layers.Count; i++)
            if (layers[i] != null)
            {
                var p = layers[i].localPosition;
                layers[i].localPosition = new Vector3(p.x, p.y, layerZ[i]);
            }
        layers.Clear();
        layerZ.Clear();
        for (int i = 0; i < grown.Count; i++)
            if (grown[i] != null)
                grown[i].localScale = grownScale[i];
        grown.Clear();
        grownScale.Clear();
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

    /// <summary>The splash camera draws screen-sized particle bursts sized for a 60 degree flat view
    /// (Pretitle Hole/BG/Sharps, 5-10 m ahead); in the headset their round edges show. The room covers it.</summary>
    private void CullSplash(Camera sceneCamera)
    {
        if (splashCam == sceneCamera)
            return;
        if (splashCam != null)
            splashCam.cullingMask = splashMask;
        splashCam = sceneCamera;
        if (splashCam == null)
            return;
        splashMask = splashCam.cullingMask;
        splashCam.cullingMask = 0;
    }

    public PlacementResult Place(
        Canvas canvas,
        out Vector3 position,
        out Quaternion rotation,
        out float widthMeters
    )
    {
        if (seen.Add(canvas.GetInstanceID()))
            Adopt(canvas);
        PanelPose(out position, out rotation);
        widthMeters = canvasWidth * layout.MetersPerUnit;
        return Shown ? PlacementResult.Placed : PlacementResult.Hidden;
    }

    /// <summary>A new screen: hide its backing, ink art drawn for a light backing, spread its layers in depth
    /// and blend the room towards its colours. Once per canvas, never per frame.</summary>
    private void Adopt(Canvas canvas)
    {
        Undo();
        fills.Clear();
        kept.Clear();
        // The splash shows its camera's clear colour wherever its art is transparent.
        if (splashCam != null && splashCam.clearFlags == CameraClearFlags.SolidColor)
        {
            var bg = splashCam.backgroundColor;
            fills.Add(new Rgba(bg.r, bg.g, bg.b));
        }
        backing.Scan(canvas, fills, kept);
        var target = LoadingPalette.For(fills, destination);
        InkDarkArt(target);
        // Fit once to know the scale, grow the text against it, then fit again so the grown text is inside the
        // bounds (the second fit may shrink a little: the text then lands just under the minimum, never over the art).
        Fit((RectTransform)canvas.transform);
        if (GrowText(canvas.transform))
            Fit((RectTransform)canvas.transform);
        Spread(canvas);
        PlaceProgress();
        Plugin.Log.LogInfo(
            $"[ui.menu] loading room for '{canvas.transform.root.name}': hid {backing.HiddenCount} fill(s), "
                + $"inked {inked.Count}, {layers.Count} layer(s), {canvasWidth * layout.MetersPerUnit:0.00} m wide, "
                + $"art {layout.ContentBottom:0.00} m above the floor, palette horizon {target.Horizon} accent {target.Accent}"
        );
        BlendTo(target);
    }

    /// <summary>The fit shrinks the whole screen to a comfortable view, which leaves its text too small to read
    /// (the plane screen's ring spells LOADING... five times round the globe by design, LoadingScreenAnimation.cs:29,
    /// and reveals it with progress, :51). Each text grows about its own pivot to
    /// <see cref="ReadableText.MinDegrees"/>; a ring (CircularTextMeshPro) stays a ring round the same centre.</summary>
    private bool GrowText(Transform root)
    {
        var rootScale = Mathf.Abs(root.lossyScale.y);
        if (rootScale < 1e-9f)
            return false;
        foreach (var g in kept)
        {
            if (g is not TMPro.TMP_Text text)
                continue;
            var t = text.transform;
            var units = text.fontSize * Mathf.Abs(t.lossyScale.y) / rootScale;
            var k = ReadableText.Grow(units * layout.MetersPerUnit, PanelDistance, MaxTextGrowth);
            if (k <= 1.01f)
                continue;
            grown.Add(t);
            grownScale.Add(t.localScale);
            t.localScale *= k;
            Plugin.Log.LogInfo($"[ui.menu] loading text '{t.name}' grown x{k:0.00}");
        }
        return grown.Count > 0;
    }

    private void InkDarkArt(LoadingPalette target)
    {
        // Only if a backing was hidden: dark art on an untouched screen still has its backdrop.
        if (backing.HiddenCount == 0)
            return;
        foreach (var g in kept)
        {
            var c = g.color;
            if (!LoadingPalette.NeedsInk(new Rgba(c.r, c.g, c.b, c.a)))
                continue;
            inked.Add(g);
            inkedColors.Add(c);
            g.color = new Color(target.Ink.R, target.Ink.G, target.Ink.B, c.a);
        }
    }

    /// <summary>Pushes the screen's top-level layers apart, earlier siblings further back, so the background
    /// animation sits behind the globe and the plane in stereo. Draw order is unchanged (hierarchy order).</summary>
    private void Spread(Canvas canvas)
    {
        if (backing.HiddenCount == 0)
            return;
        var root = canvas.transform;
        var unitsPerMeter = 1f / layout.MetersPerUnit;
        for (int i = 0; i < root.childCount; i++)
        {
            var child = root.GetChild(i);
            var g = child.GetComponentInChildren<Graphic>(true);
            if (g == null || !kept.Contains(g))
                continue;
            layers.Add(child);
            layerZ.Add(child.localPosition.z);
        }
        var step =
            layers.Count > 1 ? Mathf.Min(LayerStep, MaxLayerSpread / (layers.Count - 1)) : 0f;
        for (int i = 0; i < layers.Count; i++)
        {
            var p = layers[i].localPosition;
            var back = (layers.Count - 1 - i) * step * unitsPerMeter;
            layers[i].localPosition = new Vector3(p.x, p.y, layerZ[i] + back);
        }
    }

    private void BlendTo(LoadingPalette target)
    {
        toPalette = target;
        if (!Shown)
            return;
        fromPalette = palette;
        blendStart = Time.unscaledTime;
        blending = true;
    }

    private void ApplyPalette(LoadingPalette p)
    {
        palette = p;
        cam.backgroundColor = ToColor(p.Void);
        var horizon = ToColor(p.Horizon);
        for (int i = 0; i < walls.Length; i++)
            walls[i].color = horizon;
        horizonFade.color = horizon;
        grid.color = ToColor(p.GridLine);
        progressImage.color = ToColor(p.Accent);
        skipFill.color = ToColor(p.Accent);
        var ink = ToColor(p.Ink);
        for (int i = 0; i < inked.Count; i++)
        {
            var g = inked[i];
            if (g != null)
                g.color = new Color(ink.r, ink.g, ink.b, g.color.a);
        }
    }

    private static Color ToColor(Rgba c) => new(c.R, c.G, c.B, 1f);

    private void PanelPose(out Vector3 position, out Quaternion rotation)
    {
        position =
            origin.position
            + anchor
            + Vector3.up * layout.CenterHeight
            + facing * new Vector3(layout.CenterX, 0f, PanelDistance);
        rotation = facing;
    }

    private void OnEnable() => Application.onBeforeRender += TrackHead;

    private void OnDestroy() => RoomUp = false;

    private void OnDisable() => Application.onBeforeRender -= TrackHead;

    private void LateUpdate()
    {
        if (!Shown)
            return;
        TrackHead();
        UpdateProgress();
        backing.Reassert();
        if (blending)
        {
            var t = (Time.unscaledTime - blendStart) / BlendSeconds;
            blending = t < 1f;
            ApplyPalette(LoadingPalette.Lerp(fromPalette, toPalette, Mathf.SmoothStep(0f, 1f, t)));
        }
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
        BuildSkipRing();
        ApplyPalette(LoadingPalette.Default);
        space.SetActive(false);
    }

    private void BuildFloor()
    {
        var floor = WorldCanvas("Floor", new Vector2(FloorHalf * 200f, FloorHalf * 200f), 0.01f);
        floor.sortingOrder = RoomOrder;
        floor.transform.localPosition = Vector3.zero;
        floor.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        grid = Image<RawImage>(floor.transform, "Grid");
        grid.texture = GridTexture();
        // One cell per metre.
        grid.uvRect = new Rect(0f, 0f, FloorHalf * 2f, FloorHalf * 2f);
        Stretch(grid.rectTransform);

        horizonFade = Image<RawImage>(floor.transform, "HorizonFade");
        horizonFade.texture = RadialFadeTexture();
        Stretch(horizonFade.rectTransform);
    }

    private void BuildWalls()
    {
        var gradient = VerticalGradientTexture();
        for (int i = 0; i < 4; i++)
        {
            var rotation = Quaternion.Euler(0f, 90f * i, 0f);
            var wall = WorldCanvas("Wall" + i, new Vector2(5000f, 3000f), 0.01f);
            wall.sortingOrder = RoomOrder;
            wall.transform.SetLocalPositionAndRotation(
                rotation * new Vector3(0f, 15f, 25f),
                rotation
            );
            walls[i] = Image<RawImage>(wall.transform, "Sky");
            walls[i].texture = gradient;
            Stretch(walls[i].rectTransform);
        }
    }

    private void BuildProgress()
    {
        var bar = WorldCanvas("Progress", new Vector2(800f, 24f), 0.001f);
        progressBar = bar.transform;
        var back = Image<Image>(bar.transform, "Back");
        back.color = new Color(1f, 1f, 1f, 0.12f);
        Stretch(back.rectTransform);
        progressImage = Image<Image>(bar.transform, "Fill");
        progressFill = progressImage.rectTransform;
        progressFill.offsetMin = progressFill.offsetMax = Vector2.zero;
    }

    /// <summary>Progress bar and skip ring just under the art, a little in front of it.</summary>
    private void PlaceProgress()
    {
        var height = Mathf.Max(layout.ContentBottom - 0.1f, 0.15f);
        var position =
            origin.position
            + anchor
            + Vector3.up * height
            + facing * new Vector3(0f, 0f, PanelDistance - 0.05f);
        progressBar.SetPositionAndRotation(position, facing);
        skipRing.SetPositionAndRotation(position, facing);
    }

    /// <summary>Hold progress of the splash skip, 0..1.</summary>
    public void SetSkipProgress(float progress)
    {
        skipFill.fillAmount = progress;
        // Breathes while idle so it reads as something to act on; no motion, only alpha.
        var idle = 0.3f + 0.15f * Mathf.Sin(Time.unscaledTime * 3f);
        skipBack.color = new Color(1f, 1f, 1f, progress > 0f ? 0.45f : idle);
    }

    /// <summary>
    /// The scene this load is heading to (LoadingScreenHandler.LoadSceneProcess, A/LoadingScreenHandler.cs:132).
    /// A grey screen (black or white) takes its colour: an expedition takes the current ascent's colour
    /// (AscentData.ascents[currentAscent + 2].color, as A/AscentUI.cs:17 and A/EndScreen.cs:626 index it), the
    /// airport the plane screen's travel wash. Null clears it.
    /// </summary>
    public void SetDestination(string scene)
    {
        destination = DestinationKey(scene);
        if (backing.Canvas != null)
            BlendTo(LoadingPalette.For(fills, destination));
    }

    /// <summary>Forgets the destination without touching the room (it is fading out or gone).</summary>
    public void ClearDestination() => destination = null;

    private Rgba? DestinationKey(string scene)
    {
        if (string.IsNullOrEmpty(scene) || scene is "Title" or "Pretitle")
            return null;
        if (scene == "Airport")
            return airportKey ??= AirportKey();
        var data = SingletonAsset<AscentData>.Instance;
        var ascents = data != null ? data.ascents : null;
        if (ascents == null || ascents.Count == 0)
            return null;
        var c = ascents[Mathf.Clamp(Ascents.currentAscent + 2, 0, ascents.Count - 1)].color;
        return new Rgba(c.r, c.g, c.b, c.a);
    }

    /// <summary>The plane screen's own plain full-canvas washes (prefab LoadingScreen: Black, Color, Screen,
    /// Fade), read from the prefab (A/LoadingScreenHandler.cs:20) without instantiating it.</summary>
    private static Rgba AirportKey()
    {
        var handler = RetrievableResourceSingleton<LoadingScreenHandler>.Instance;
        var prefab = handler != null ? handler.loadingScreenPrefabPlane : null;
        var washes = new List<Rgba>();
        if (prefab != null)
            foreach (var image in prefab.GetComponentsInChildren<Image>(true))
                if (
                    image.sprite == null
                    && image.rectTransform.anchorMin == Vector2.zero
                    && image.rectTransform.anchorMax == Vector2.one
                )
                    washes.Add(
                        new Rgba(image.color.r, image.color.g, image.color.b, image.color.a)
                    );
        return LoadingPalette.TryPickKey(washes, out var key) ? key : default;
    }

    /// <summary>Scales and lifts the panel so the kept art (not the canvas rect) fits the comfortable view
    /// and stays above the floor; the plane screen's globe and grid hang far below the flat screen edge.</summary>
    private void Fit(RectTransform root)
    {
        canvasWidth = root.rect.width > 0f ? root.rect.width : 1920f;
        float minX = float.MaxValue,
            minY = float.MaxValue,
            maxX = float.MinValue,
            maxY = float.MinValue;
        foreach (var g in kept)
        {
            var rt = g.rectTransform;
            var r = Drawn(g, rt.rect, rt.pivot);
            Corners[0] = new Vector3(r.xMin, r.yMin);
            Corners[1] = new Vector3(r.xMin, r.yMax);
            Corners[2] = new Vector3(r.xMax, r.yMin);
            Corners[3] = new Vector3(r.xMax, r.yMax);
            for (int i = 0; i < 4; i++)
            {
                Vector2 p = root.InverseTransformPoint(rt.TransformPoint(Corners[i]));
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }
        }
        if (minX > maxX)
            minX = maxX = minY = maxY = 0f;
        layout = LoadingLayout.Fit(
            minX,
            maxX,
            minY,
            maxY,
            canvasWidth,
            PanelWidth,
            PanelDistance,
            EyeHeight
        );
    }

    /// <summary>The part of the rect an aspect-preserving image really draws (the plane screen's grid rect is
    /// 3439x8188 but draws a 3439 disc).</summary>
    private static Rect Drawn(Graphic g, Rect r, Vector2 pivot)
    {
        if (
            g is not Image { preserveAspect: true, sprite: { } sprite }
            || sprite.rect.height <= 0f
            || r.height <= 0f
        )
            return r;
        var aspect = sprite.rect.width / sprite.rect.height;
        if (aspect > r.width / r.height)
        {
            var h = r.width / aspect;
            return new Rect(r.x, r.y + (r.height - h) * pivot.y, r.width, h);
        }
        var w = r.height * aspect;
        return new Rect(r.x + (r.width - w) * pivot.x, r.y, w, r.height);
    }

    private void BuildSkipRing()
    {
        var ring = WorldCanvas("SkipRing", new Vector2(128f, 128f), SkipRingSize / 128f);
        skipRing = ring.transform;
        var sprite = RingSprite();
        skipBack = Image<Image>(ring.transform, "Back");
        skipBack.sprite = sprite;
        Stretch(skipBack.rectTransform);
        skipFill = Image<Image>(ring.transform, "Fill");
        skipFill.sprite = sprite;
        skipFill.type = UnityEngine.UI.Image.Type.Filled;
        skipFill.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360;
        skipFill.fillOrigin = (int)UnityEngine.UI.Image.Origin360.Top;
        skipFill.fillClockwise = true;
        Stretch(skipFill.rectTransform);
        ring.gameObject.SetActive(false);
    }

    /// <summary>A soft white ring, the hold-to-skip cue.</summary>
    private static Sprite RingSprite()
    {
        const int size = 64;
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
            var d = Mathf.Sqrt(dx * dx + dy * dy);
            var a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.8f) / 0.14f);
            pixels[y * size + x] = new Color(1f, 1f, 1f, a * a * (3f - 2f * a));
        }
        tex.SetPixels(pixels);
        tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
    }

    private Canvas WorldCanvas(string name, Vector2 size, float metersPerPixel)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(origin, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = PeakXR.Core.DrawOrder.WorldUi;
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

    // The textures are greyscale; the palette tints them through the graphic colour, so a new room costs
    // a few colour writes and no texture rebuild.

    private static Texture2D GridTexture()
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true)
        {
            wrapMode = TextureWrapMode.Repeat,
        };
        var pixels = new Color32[size * size];
        var cell = (byte)(GridCell * 255f);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            var line = x < 2 || y < 2;
            pixels[y * size + x] = line
                ? new Color32(255, 255, 255, 150)
                : new Color32(cell, cell, cell, 255);
        }
        tex.SetPixels32(pixels);
        tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
        tex.anisoLevel = 8;
        return tex;
    }

    /// <summary>Clear in the middle, the horizon colour at the rim: the floor melts into the horizon.</summary>
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
            pixels[y * size + x] = new Color(1f, 1f, 1f, t);
        }
        tex.SetPixels(pixels);
        tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        return tex;
    }

    /// <summary>Horizon brightness at the bottom, the void's at the top.</summary>
    private static Texture2D VerticalGradientTexture()
    {
        const int height = 64;
        var tex = new Texture2D(1, height, TextureFormat.RGBA32, mipChain: false)
        {
            wrapMode = TextureWrapMode.Clamp,
        };
        for (int y = 0; y < height; y++)
        {
            var v = Mathf.Lerp(1f, WallTop, Mathf.Pow(y / (height - 1f), 0.6f));
            tex.SetPixel(0, y, new Color(v, v, v, 1f));
        }
        tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        return tex;
    }
}
