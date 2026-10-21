using System.Collections.Generic;
using PeakXR.Core;
using PeakXR.UI.Common;
using UnityEngine;
using UnityEngine.UI;
using Zorro.UI;
using Zorro.UI.Modal;

namespace PeakXR.UI.World;

internal sealed class TrackedCanvas
{
    public Canvas Canvas;
    public RectTransform Rect;
    public int Id;
    public CanvasKind Kind;
    public ICanvasPlacement Placement;
    public MenuWindow[] Windows;
    public Modal Modal;
    public UIPageHandler Pages;
    public Vector2 Size;

    public bool Converted;
    public RenderMode OriginalMode;
    public Camera OriginalCamera;
    public Vector3 OriginalScale;

    public bool IsOpen;
    public bool WasOpen;

    /// <summary>Menus: where the content centre sits. Other kinds: the canvas position.</summary>
    public Vector3 Position;

    /// <summary>Menus: size factor from a wall that pulled the panel nearer, so it keeps its angular size.</summary>
    public float Scale = 1f;
    public Vector2 ContentSize;
    public Vector2 ContentCenter;
    public float NextMeasure;
    public Quaternion Rotation;

    /// <summary>Menus: eye point, heading and stacking offset the panel was opened with; the wall test keeps them
    /// when the content changes, so the panel only slides along its sight lines.</summary>
    public Vector3 Eye;
    public float Yaw;
    public float Stack;

    /// <summary>Menus: opened, page changed or rows refilled; the walls are tested again until the page settles.
    /// Nothing else (a dropdown list opening) moves a placed panel, so it never shifts under the laser.</summary>
    public bool Retest;

    /// <summary>Menus: brought closer by a wall test since the last log line.</summary>
    public bool Moved;

    /// <summary>Menus lose their flat-screen backdrops in VR (<see cref="BackdropStrip"/>).</summary>
    public bool Strip;

    /// <summary>False while a page may still be scaling in: measured every frame until the size holds.</summary>
    public bool Settled = true;
    public float SettleUntil;
    public bool StripWasOpen;
    public UIPage LastPage;
    public bool Rescan;

    /// <summary>Rescan again from this frame on (-1: none): content refilled this frame is laid out next to the cells
    /// it replaces until their deferred Destroy, so only next frame's layout is the one drawn.</summary>
    public int RescanFrame = -1;
    public int MeasuredFrame = -1;
    public readonly List<HiddenBackdrop> Hidden = new();

    /// <summary>Dimmers that were off at scan time: hidden once they switch on.</summary>
    public readonly List<Graphic> PendingDimmers = new();
    public readonly List<TMPro.TMP_Text> HaloTexts = new();
    public readonly List<Material> HaloOriginals = new();
}

/// <summary>
/// Finds every screen-space canvas the game shows, converts it to a world-space panel while VR is
/// running and places it every frame by kind. Detection is generic: canvases are picked up when their
/// <see cref="CanvasScaler"/> enables, when a <see cref="MenuWindow"/> opens, on scene load and by a slow
/// rescan, so canvases added in future game versions convert too.
/// </summary>
internal static class WorldCanvases
{
    private static readonly List<TrackedCanvas> tracked = new();
    private static readonly HashSet<int> known = new();
    private static readonly List<Canvas> pending = new();
    private static readonly HashSet<int> convertedRoots = new();
    private static readonly List<Graphic> graphics = new();
    private static readonly Vector3[] corners = new Vector3[4];
    private static bool converting;
    private static int forcedFrame = -1;

    // Re-measure an open menu this often, so a page switch (pause -> settings) resizes it.
    private const float MeasureSeconds = 0.5f;

    // Longest a page may take to scale in before its size counts as settled (ScaleOnEnable default 0.25 s).
    private const float SettleSeconds = 0.6f;

    public static bool IsConverted(Canvas root) => convertedRoots.Contains(root.GetInstanceID());

    /// <summary>How many backdrops are hidden on a converted root canvas, and how many of those GraphicRaycaster
    /// would now skip (self-test).</summary>
    public static int HiddenBackdrops(Canvas root, out int unraycastable)
    {
        unraycastable = 0;
        var id = root.GetInstanceID();
        foreach (var t in tracked)
            if (t.Id == id)
            {
                unraycastable = BackdropStrip.Unraycastable(t);
                // Graphics destroyed with their rows (a tab switch) stay listed until the next scan prunes them.
                int live = 0;
                foreach (var h in t.Hidden)
                    if (h.Graphic != null)
                        live++;
                return live;
            }
        return 0;
    }

    /// <summary>Where an open menu's content centre sits (self-test). False when the canvas is not a placed menu.</summary>
    public static bool TryMenuCentre(Canvas root, out Vector3 centre)
    {
        centre = default;
        var id = root.GetInstanceID();
        foreach (var t in tracked)
            if (t.Id == id && t.WasOpen)
            {
                centre = t.Position;
                return true;
            }
        return false;
    }

    public static void Track(Canvas canvas)
    {
        if (canvas != null)
            pending.Add(canvas);
    }

    /// <summary>A window opened on this canvas: place it in front of the head again.</summary>
    public static void Reopen(Canvas canvas)
    {
        if (canvas == null)
            return;
        Track(canvas);
        var id = canvas.rootCanvas.GetInstanceID();
        foreach (var t in tracked)
            if (t.Id == id)
            {
                t.WasOpen = false;
                t.Rescan = true;
            }
    }

    /// <summary>The game spawned new content into an open page without changing the page (a settings tab fills its
    /// rows): hide its backdrops and halo its text before the rows fade in. Scanned this frame and again the next:
    /// ShowSettings destroys the old rows with a deferred Destroy (SharedSettingsMenu.cs:46-49), so this frame the
    /// layout stacks the new rows below the old ones, partly off the canvas, where the scan cannot measure them.</summary>
    public static void Restrip(Component inside)
    {
        if (inside == null)
            return;
        var canvas = inside.GetComponentInParent<Canvas>();
        if (canvas == null)
            return;
        var id = canvas.rootCanvas.GetInstanceID();
        foreach (var t in tracked)
            if (t.Id == id && t.Strip)
            {
                t.Rescan = true;
                t.RescanFrame = Time.frameCount + 1;
            }
    }

    public static void Scan(bool includeInactive)
    {
        pending.AddRange(
            Object.FindObjectsByType<Canvas>(
                includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                FindObjectsSortMode.None
            )
        );
    }

    /// <summary>Forget canvases that died with the old scene so their ids can be pruned.</summary>
    public static void OnSceneLoaded()
    {
        known.Clear();
        for (int i = tracked.Count - 1; i >= 0; i--)
        {
            if (tracked[i].Canvas == null)
            {
                convertedRoots.Remove(tracked[i].Id);
                tracked.RemoveAt(i);
            }
            else
                known.Add(tracked[i].Id);
        }
    }

    public static void ProcessPending()
    {
        for (int i = 0; i < pending.Count; i++)
        {
            var canvas = pending[i];
            if (canvas == null)
                continue;
            var root = canvas.isRootCanvas ? canvas : canvas.rootCanvas;
            if (!known.Add(root.GetInstanceID()) || !ShouldConvert(root))
                continue;
            Add(root);
        }
        pending.Clear();
    }

    private static bool ShouldConvert(Canvas root)
    {
        switch (root.renderMode)
        {
            case RenderMode.ScreenSpaceOverlay:
                return true;
            case RenderMode.ScreenSpaceCamera:
                // Canvases drawn into a render texture (hero card, photobooth, guidebook) are already 3D.
                return root.worldCamera == null || root.worldCamera.targetTexture == null;
            default:
                return false;
        }
    }

    private static void Add(Canvas root)
    {
        var windows = new List<MenuWindow>(root.GetComponentsInChildren<MenuWindow>(true));
        windows.AddRange(root.GetComponentsInParent<MenuWindow>(true));
        var hasSelectables = root.GetComponentsInChildren<Selectable>(true).Length > 0;
        var kind = CanvasRules.Classify(root, hasSelectables, windows.Count > 0, out var placement);

        var rect = (RectTransform)root.transform;
        var scaler = root.GetComponent<CanvasScaler>();
        var size =
            scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize
                ? scaler.referenceResolution
                : rect.sizeDelta;
        if (size.x < 1f || size.y < 1f)
            size = new Vector2(1920f, 1080f);

        var t = new TrackedCanvas
        {
            Canvas = root,
            Rect = rect,
            Id = root.GetInstanceID(),
            Kind = kind,
            Placement = placement,
            Windows = windows.ToArray(),
            Modal =
                root.GetComponentInParent<Modal>(true) ?? root.GetComponentInChildren<Modal>(true),
            Pages =
                root.GetComponentInParent<UIPageHandler>(true)
                ?? root.GetComponentInChildren<UIPageHandler>(true),
            Size = size,
            Strip = kind == CanvasKind.Menu || CanvasRules.StripsBackdrops(placement),
        };
        tracked.Add(t);
        Plugin.Log.LogDebug(
            $"[ui.world] canvas '{root.name}' ({root.renderMode}, sort {root.sortingOrder}) -> {kind}"
        );
        if (converting)
            Convert(t);
    }

    public static void ConvertAll()
    {
        converting = true;
        foreach (var t in tracked)
            Convert(t);
    }

    public static void RevertAll()
    {
        converting = false;
        foreach (var t in tracked)
            Revert(t);
    }

    private static void Convert(TrackedCanvas t)
    {
        if (t.Converted || t.Canvas == null || t.Kind is CanvasKind.Hidden or CanvasKind.Fade)
            return;
        var canvas = t.Canvas;
        t.OriginalMode = canvas.renderMode;
        t.OriginalCamera = canvas.worldCamera;
        t.OriginalScale = t.Rect.localScale;
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = LaserPointer.EventCamera;
        t.Rect.sizeDelta = t.Size;
        t.Converted = true;
        t.WasOpen = false;
        convertedRoots.Add(t.Id);
        if (t.Strip)
        {
            t.StripWasOpen = false;
            Strip(t);
            t.NextMeasure = 0f;
        }
        if (t.Kind == CanvasKind.Names)
            Apply(t, Vector3.zero, Quaternion.identity, 0.001f * t.Size.x);
    }

    private static void Revert(TrackedCanvas t)
    {
        convertedRoots.Remove(t.Id);
        if (!t.Converted || t.Canvas == null)
            return;
        t.Canvas.renderMode = t.OriginalMode;
        t.Canvas.worldCamera = t.OriginalCamera;
        t.Rect.localScale = t.OriginalScale;
        BackdropStrip.Restore(t, panelsOnly: false);
        t.Converted = false;
        if (t.Kind == CanvasKind.Names)
            ResetNameTags(t.Canvas);
    }

    /// <summary>PlayerNamePatch rotates and scales every tag; vanilla UpdateName writes only the position
    /// (A/UIPlayerNames.cs:57), so flat play would keep the VR pose.</summary>
    private static void ResetNameTags(Canvas canvas)
    {
        // shortcut: assumes the prefab tags use identity rotation and unit scale; save the originals if that changes.
        var names =
            canvas.GetComponentInParent<UIPlayerNames>(true)
            ?? canvas.GetComponentInChildren<UIPlayerNames>(true);
        if (names == null)
            return;
        foreach (var tag in names.playerNameText)
            if (tag != null)
            {
                tag.transform.localRotation = Quaternion.identity;
                tag.transform.localScale = Vector3.one;
            }
    }

    /// <summary>Places every converted canvas for this frame. Returns whether an interactive panel is shown.</summary>
    public static bool Update(Transform head, HudLayout hud, UIWorldSettings cfg)
    {
        int openMenus = 0;
        for (int i = tracked.Count - 1; i >= 0; i--)
        {
            var t = tracked[i];
            if (t.Canvas == null)
            {
                convertedRoots.Remove(t.Id);
                tracked.RemoveAt(i);
                continue;
            }
            t.IsOpen = t.Converted && IsOpen(t);
            if (t.IsOpen && t.WasOpen && t.Kind is CanvasKind.Menu or CanvasKind.Custom)
                openMenus++;
        }

        bool interactive = false;
        foreach (var t in tracked)
        {
            if (!t.Converted)
                continue;
            if (t.Strip)
                WatchPages(t);
            switch (t.Kind)
            {
                case CanvasKind.Hud:
                    Apply(t, hud.Position, hud.Rotation, hud.MetersPerPixel * t.Size.x);
                    break;
                case CanvasKind.Info:
                    // A little closer than the HUD so the two never z-fight.
                    Apply(
                        t,
                        hud.Position - hud.Rotation * new Vector3(0f, 0f, 0.02f),
                        hud.Rotation,
                        hud.MetersPerPixel * t.Size.x
                    );
                    break;
                case CanvasKind.Custom:
                    var result = t.Placement.Place(
                        t.Canvas,
                        out var pos,
                        out var rot,
                        out var width
                    );
                    if (result == PlacementResult.Placed)
                    {
                        Apply(t, pos, rot, width);
                        interactive |= t.IsOpen && t.Placement.Interactive;
                    }
                    else if (result == PlacementResult.Hidden)
                        t.Rect.localScale = Vector3.zero;
                    else
                        interactive |=
                            PlaceMenu(t, head, cfg, ref openMenus) && t.Placement.Interactive;
                    break;
                case CanvasKind.Menu:
                    interactive |= PlaceMenu(t, head, cfg, ref openMenus);
                    break;
            }
            // Placed custom menus (title, boarding pass) are not measured by PlaceMenu.
            if (t.Strip && t.IsOpen)
                MeasureIfDue(t);
        }
        return interactive;
    }

    /// <summary>
    /// A menu opened, a window popped up or the page changed: hide any new backdrop before this frame renders
    /// and re-measure every frame until the page stops scaling in (ScaleOnEnable).
    /// </summary>
    private static void WatchPages(TrackedCanvas t)
    {
        var page = t.Pages != null ? t.Pages.currentPage : null;
        var opened = t.IsOpen && !t.StripWasOpen;
        t.StripWasOpen = t.IsOpen;
        if (t.RescanFrame >= 0 && Time.frameCount >= t.RescanFrame)
        {
            t.RescanFrame = -1;
            t.Rescan = true;
        }
        if (!opened && !t.Rescan && page == t.LastPage)
            return;
        t.LastPage = page;
        // Hidden placements scale the canvas to nothing: try again once it is placed.
        t.Rescan = Degenerate(t);
        if (!t.IsOpen || t.Rescan)
            return;
        Strip(t);
        t.NextMeasure = 0f;
        t.Retest = true;
        t.Settled = false;
        t.SettleUntil = Time.unscaledTime + SettleSeconds;
    }

    private static bool IsOpen(TrackedCanvas t)
    {
        if (!t.Canvas.isActiveAndEnabled)
            return false;
        if (t.Modal != null && !t.Modal.Visible)
            return false;
        // A page handler (pause menu) is shown while it is active; its windows are popups such as the
        // quit confirm (PauseMenuMainPage.cs:32), closed most of the time. Zorro OverlayUIHandler.IsOpen agrees.
        if (t.Pages != null)
            return t.Pages.isActiveAndEnabled;
        if (t.Windows.Length == 0)
            return true;
        foreach (var w in t.Windows)
            if (w != null && w.isOpen)
                return true;
        return false;
    }

    /// <summary>World-locked menu: spawned in front of the head when it opens, re-spawned if the
    /// player ends up far away from it (teleport, respawn), and tested against walls again whenever what it draws
    /// changes size (pause page to settings, a longer settings tab).</summary>
    private static bool PlaceMenu(
        TrackedCanvas t,
        Transform head,
        UIWorldSettings cfg,
        ref int openMenus
    )
    {
        if (!t.IsOpen)
        {
            t.WasOpen = false;
            return false;
        }

        if (!t.WasOpen || (t.Position - head.position).sqrMagnitude > 16f)
        {
            // The walls are tested against what the menu draws, so measure it first, even when WatchPages already
            // measured this frame (MeasureIfDue runs once a frame).
            if (!Degenerate(t))
            {
                MeasureContent(t);
                t.MeasuredFrame = Time.frameCount;
            }
            t.Eye = head.position;
            // Stack newer menus slightly closer, after the wall clamp, so two menus pulled to one wall never z-fight.
            t.Stack = 0.04f * openMenus;
            openMenus++;
            var yaw = PanelMath.YawOf(head.forward.ToNumerics(), head.up.ToNumerics());
            // With a wall at your face, turn toward the first heading within 45 degrees that has room.
            for (int i = 0; i < OpenYawSteps.Length; i++)
            {
                openDepths[i] = PanelDepth(t, yaw + OpenYawSteps[i], cfg);
                if (openDepths[i] - PanelMath.WallGap < PanelMath.NearestMenuDistance)
                    continue;
                for (int j = i + 1; j < OpenYawSteps.Length; j++)
                    openDepths[j] = 0f;
                break;
            }
            var pick = PanelMath.PickOpenYaw(openDepths);
            t.Yaw = yaw + OpenYawSteps[pick];
            ClearWalls(t, cfg, OpenYawSteps[pick]);
            t.Retest = true;
            t.WasOpen = true;
            t.NextMeasure = 0f;
        }

        MeasureIfDue(t);
        // A page that just opened, switched or refilled is tested while it scales in, and once more when it settles.
        if (t.Retest)
        {
            t.Retest = !t.Settled;
            ClearWalls(t, cfg, null);
        }

        // Sized by what the menu draws, not the whole 1920x1080 canvas: a small menu (pause) grows to a
        // readable size, full-screen ones keep MenuWidth. Its content centre stays where it spawned.
        var mpp = t.Scale * MenuMpp(t, cfg);
        var position = t.Position - t.Rotation * (Vector3)(t.ContentCenter * mpp);
        Apply(t, position, t.Rotation, mpp * t.Size.x);
        return true;
    }

    private static float MenuMpp(TrackedCanvas t, UIWorldSettings cfg) =>
        PanelMath.MenuMetersPerPixel(
            t.ContentSize.x,
            t.ContentSize.y,
            cfg.MenuWidth.Value,
            cfg.MenuDistance.Value
        );

    /// <summary>
    /// Places the menu along <see cref="TrackedCanvas.Yaw"/> as far as <c>UI.MenuDistance</c> allows: when a wall,
    /// floor or prop is nearer than any part of the panel, or stands between that part and the eye, the panel comes
    /// toward the eye along the same sight lines (same angular size) to just in front of it. A retest after opening
    /// (<paramref name="turned"/> null) only ever brings it closer, never back out. Logs each opening and, once the
    /// page settles, any later move, with what limited it.
    /// </summary>
    private static void ClearWalls(TrackedCanvas t, UIWorldSettings cfg, float? turned)
    {
        var wanted = cfg.MenuDistance.Value;
        var depth = PanelDepth(t, t.Yaw, cfg);
        var clear = PanelMath.ClearDistance(wanted, depth);
        if (turned == null)
            clear = Mathf.Min(clear, t.Scale * wanted);
        var scale = clear / wanted;
        bool moved = turned != null || (t.Scale - scale) * wanted >= 0.005f;
        t.Moved = turned == null && (t.Moved || moved);
        if (moved)
        {
            t.Scale = scale;
            var offset = PanelMath
                .InFront(t.Yaw, Mathf.Max(0.5f, clear - t.Stack), cfg.MenuTilt.Value)
                .ToUnity();
            t.Position = t.Eye + offset;
            t.Rotation = Quaternion.LookRotation(offset, Vector3.up);
        }
        if (turned != null || (!t.Retest && t.Moved))
        {
            t.Moved = false;
            LogPlacement(t, depth, clear, turned);
        }
    }

    /// <summary>
    /// Walls a menu must stay in front of. The Airport scene puts its hall on Map, doors, stairs and the photo
    /// booth on MirrorHidden, mirrors on MIrror and desks, seats and fences on Default; the beach plane wreck is
    /// non-convex Map mesh colliders (all read from the scenes with UnityPy). Character is left out, and a hit with a
    /// rigidbody (held or dropped items, bodies) is skipped. Back faces count: in the wreck's cabin every ray starts
    /// inside its collider tube, and a default raycast passes through faces turned away from it.
    /// </summary>
    private static readonly int WallMask = LayerMask.GetMask(
        "Terrain",
        "Map",
        "Default",
        "MirrorHidden",
        "MIrror"
    );
    private static readonly RaycastHit[] wallHits = new RaycastHit[16];

    // Headings a menu may open toward when a wall is at your face, in order of preference.
    private static readonly float[] OpenYawSteps = { 0f, 15f, -15f, 30f, -30f, 45f, -45f };
    private static readonly float[] openDepths = new float[OpenYawSteps.Length];
    private static readonly string[] SampleColumn =
    {
        "left edge",
        "left",
        "middle",
        "right",
        "right edge",
    };
    private static readonly string[] SampleRow = { "bottom", "middle", "top" };
    private static Collider limit;
    private static int limitSample;

    /// <summary>
    /// How far ahead along <paramref name="yaw"/> the nearest wall stands in front of any part of the panel, as a
    /// depth on its centre line (infinity for none): <see cref="PanelMath.PanelSamples"/> rays from the eye to points
    /// across the panel as drawn at <c>UI.MenuDistance</c>. Placement only, never per frame.
    /// </summary>
    private static float PanelDepth(TrackedCanvas t, float yaw, UIWorldSettings cfg)
    {
        var wanted = cfg.MenuDistance.Value;
        var size = t.ContentSize.x >= 1f && t.ContentSize.y >= 1f ? t.ContentSize : t.Size;
        var half =
            0.5f * PanelMath.MenuMetersPerPixel(size.x, size.y, cfg.MenuWidth.Value, wanted) * size;
        var offset = PanelMath.InFront(yaw, wanted, cfg.MenuTilt.Value).ToUnity();
        var rotation = Quaternion.LookRotation(offset, Vector3.up);
        float depth = float.PositiveInfinity;
        limit = null;
        var backfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        try
        {
            for (int i = 0; i < PanelMath.PanelSamples; i++)
            {
                var s = PanelMath.PanelSample(i, half.x, half.y);
                var hit = WallAlong(
                    t.Eye,
                    offset + rotation * new Vector3(s.X, s.Y, 0f),
                    out var c
                );
                var d = PanelMath.EdgeDepth(hit, wanted, s.Length());
                if (d < depth)
                {
                    depth = d;
                    limit = c;
                    limitSample = i;
                }
            }
        }
        finally
        {
            Physics.queriesHitBackfaces = backfaces;
        }
        return depth;
    }

    private static void LogPlacement(TrackedCanvas t, float depth, float clear, float? turned)
    {
        var name = t.Canvas != null ? t.Canvas.name : "?";
        var why =
            limit != null
                ? $"'{limit.name}' (layer {LayerMask.LayerToName(limit.gameObject.layer)}) {depth:0.00} m ahead at its "
                    + $"{SampleRow[limitSample / 5]} {SampleColumn[limitSample % 5]}"
                : "nothing in the way";
        var turn = turned is { } deg && deg != 0f ? $", turned {deg:0} deg toward open space" : "";
        var line =
            $"[ui.world] menu '{name}' {(turned != null ? "opened" : "resized")} at {clear:0.00} m{turn}: {why}";
        if (depth - PanelMath.WallGap < PanelMath.NearestMenuDistance)
            Plugin.Log.LogWarning(
                line + $"; held at the {PanelMath.NearestMenuDistance:0.0} m minimum"
            );
        else
            Plugin.Log.LogInfo(line);
    }

    /// <summary>Distance to the first wall on the way from <paramref name="origin"/> to
    /// <paramref name="origin"/> + <paramref name="toPoint"/>, or infinity. Placement only, never per frame.</summary>
    private static float WallAlong(Vector3 origin, Vector3 toPoint, out Collider hitCollider)
    {
        var length = toPoint.magnitude;
        int count = Physics.RaycastNonAlloc(
            origin,
            toPoint / length,
            wallHits,
            length,
            WallMask,
            QueryTriggerInteraction.Ignore
        );
        float nearest = float.PositiveInfinity;
        hitCollider = null;
        for (int i = 0; i < count; i++)
        {
            var h = wallHits[i];
            if (h.rigidbody == null && h.distance < nearest)
            {
                nearest = h.distance;
                hitCollider = h.collider;
            }
        }
        return nearest;
    }

    private static void MeasureIfDue(TrackedCanvas t)
    {
        // Once per frame: a second measure in the same frame would always look settled.
        if (
            Time.unscaledTime < t.NextMeasure
            || t.MeasuredFrame == Time.frameCount
            || Degenerate(t)
        )
            return;
        t.MeasuredFrame = Time.frameCount;
        var size = t.ContentSize;
        var center = t.ContentCenter;
        MeasureContent(t);
        if (t.Strip)
            BackdropStrip.Reassert(t);
        if (!t.Settled)
            t.Settled =
                Time.unscaledTime >= t.SettleUntil
                || (
                    (t.ContentSize - size).sqrMagnitude < 1f
                    && (t.ContentCenter - center).sqrMagnitude < 1f
                );
        t.NextMeasure = t.Settled ? Time.unscaledTime + MeasureSeconds : 0f;
    }

    /// <summary>A canvas scaled to nothing (Hidden placement) cannot map world corners back to pixels.</summary>
    private static bool Degenerate(TrackedCanvas t) => Mathf.Abs(t.Rect.lossyScale.x) < 1e-6f;

    /// <summary>
    /// Bounds of the drawn graphics in canvas pixels (relative to the canvas pivot), clipped to the canvas and
    /// skipping full-canvas ones (dimmers, vignettes). Nothing drawn means the whole canvas.
    /// </summary>
    private static void MeasureContent(TrackedCanvas t)
    {
        var bounds = t.Rect.rect;
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        t.Canvas.GetComponentsInChildren(false, graphics);
        foreach (var g in graphics)
        {
            if (!g.isActiveAndEnabled || g.color.a < 0.01f || IsPopup(t, g))
                continue;
            if (!Clip(t, g, bounds, out var lo, out var hi) || CoversCanvas(bounds, lo, hi))
                continue;
            min = Vector2.Min(min, lo);
            max = Vector2.Max(max, hi);
        }
        graphics.Clear();
        if (max.x <= min.x)
        {
            t.ContentSize = bounds.size;
            t.ContentCenter = bounds.center;
            return;
        }
        t.ContentSize = max - min;
        t.ContentCenter = (min + max) * 0.5f;
    }

    /// <summary>A popup drawn over the menu in its own sorting canvas (a dropdown's open list): transient, so it never
    /// resizes or moves the panel.</summary>
    private static bool IsPopup(TrackedCanvas t, Graphic g)
    {
        var c = g.canvas;
        return c != null && c != t.Canvas && c.overrideSorting;
    }

    /// <summary>Hides backdrops on the canvas, closed pages included, so they never flash before the next measure.
    /// Transparent ones too: a dimmer faded in later (color.a rising from 0) stays hidden.</summary>
    private static void Strip(TrackedCanvas t)
    {
        if (Degenerate(t))
            t.Rescan = true;
        else
        {
            // Rows spawned this frame (a settings tab) sit unlaid-out until the canvases rebuild; the rules judge
            // each graphic by its rect. Only on open, page change or rescan, never per frame.
            if (forcedFrame != Time.frameCount)
            {
                forcedFrame = Time.frameCount;
                Canvas.ForceUpdateCanvases();
            }
            BackdropStrip.Scan(t, !UIWorldFeature.Settings.MenuPanels.Value);
        }
    }

    /// <summary>UI.MenuPanels changed: show or hide the panels on every converted menu at once.</summary>
    public static void OnMenuPanelsChanged()
    {
        foreach (var t in tracked)
            if (t.Converted && t.Strip)
            {
                BackdropStrip.Restore(t, panelsOnly: true);
                t.Rescan = true;
            }
    }

    /// <summary>A graphic's rect in canvas pixels, clipped to the canvas. False when nothing is left.</summary>
    internal static bool Clip(
        TrackedCanvas t,
        Graphic g,
        Rect bounds,
        out Vector2 lo,
        out Vector2 hi
    )
    {
        g.rectTransform.GetWorldCorners(corners);
        Vector2 a = t.Rect.InverseTransformPoint(corners[0]);
        Vector2 b = t.Rect.InverseTransformPoint(corners[2]);
        lo = Vector2.Max(Vector2.Min(a, b), bounds.min);
        hi = Vector2.Min(Vector2.Max(a, b), bounds.max);
        return hi.x > lo.x && hi.y > lo.y;
    }

    /// <summary>Full-canvas graphics (dimmers, vignettes) span most of the canvas both ways.</summary>
    private static bool CoversCanvas(Rect bounds, Vector2 lo, Vector2 hi) =>
        hi.x - lo.x >= bounds.width * BackdropRules.FullCanvas
        && hi.y - lo.y >= bounds.height * BackdropRules.FullCanvas;

    private static void Apply(
        TrackedCanvas t,
        Vector3 position,
        Quaternion rotation,
        float widthMeters
    )
    {
        var rect = t.Rect;
        rect.SetPositionAndRotation(position, rotation);
        var parentScale = rect.parent != null ? rect.parent.lossyScale.x : 1f;
        if (Mathf.Abs(parentScale) < 1e-6f)
            parentScale = 1f;
        var scale = PanelMath.MetersPerPixel(widthMeters, t.Size.x) / parentScale;
        if (rect.localScale.x != scale || rect.localScale.y != scale)
            rect.localScale = new Vector3(scale, scale, scale);
        if (rect.sizeDelta != t.Size)
            rect.sizeDelta = t.Size;
    }
}
