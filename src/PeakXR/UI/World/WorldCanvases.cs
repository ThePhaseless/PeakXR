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
    public Vector2 ContentSize;
    public Vector2 ContentCenter;
    public float NextMeasure;
    public Quaternion Rotation;

    /// <summary>Menus get a soft backdrop in place of the game's full-canvas dimmers.</summary>
    public bool WantsBackdrop;
    public Image Backdrop;
    public float BackdropScale;
    public Vector2 BackdropSize;
    public Vector2 BackdropCenter;
    public float BackdropAlpha;

    /// <summary>Largest content graphic: its inherited CanvasGroup alpha is the page's fade.</summary>
    public Graphic FadeProbe;

    /// <summary>False while a page may still be scaling in: measured every frame until the size holds.</summary>
    public bool Settled = true;
    public float SettleUntil;
    public bool BackdropWasOpen;
    public UIPage LastPage;
    public bool Rescan;
    public int MeasuredFrame = -1;
    public readonly List<Graphic> Dimmers = new();
    public readonly List<float> DimmerAlphas = new();
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

    // Re-measure an open menu this often, so a page switch (pause -> settings) resizes it.
    private const float MeasureSeconds = 0.5f;

    // Longest a page may take to scale in before its backdrop is shown anyway (ScaleOnEnable default 0.25 s).
    private const float SettleSeconds = 0.6f;

    public static bool IsConverted(Canvas root) => convertedRoots.Contains(root.GetInstanceID());

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
            WantsBackdrop = kind == CanvasKind.Menu || CanvasRules.WantsBackdrop(placement),
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
        if (t.WantsBackdrop)
        {
            MenuBackdrop.Attach(t);
            HideDimmers(t);
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
        MenuBackdrop.Detach(t);
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
            if (t.Backdrop != null)
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
            if (t.Backdrop != null)
            {
                // Placed custom menus (title) are not measured by PlaceMenu.
                if (t.IsOpen)
                    MeasureIfDue(t);
                MenuBackdrop.Sync(t);
            }
        }
        return interactive;
    }

    /// <summary>
    /// A menu opened, a window popped up or the page changed: hide any new dimmer before this frame renders
    /// and re-measure every frame until the page stops scaling in (ScaleOnEnable).
    /// </summary>
    private static void WatchPages(TrackedCanvas t)
    {
        var page = t.Pages != null ? t.Pages.currentPage : null;
        var opened = t.IsOpen && !t.BackdropWasOpen;
        t.BackdropWasOpen = t.IsOpen;
        if (!opened && !t.Rescan && page == t.LastPage)
            return;
        t.LastPage = page;
        t.Rescan = false;
        if (!t.IsOpen)
            return;
        HideDimmers(t);
        t.NextMeasure = 0f;
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
    /// player ends up far away from it (teleport, respawn).</summary>
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
            // Stack newer menus slightly closer so overlapping panels never z-fight.
            var distance = Mathf.Max(0.5f, cfg.MenuDistance.Value - 0.04f * openMenus);
            openMenus++;
            var yaw = PanelMath.YawOf(head.forward.ToNumerics(), head.up.ToNumerics());
            var offset = PanelMath.InFront(yaw, distance, cfg.MenuTilt.Value).ToUnity();
            t.Position = head.position + offset;
            t.Rotation = Quaternion.LookRotation(offset, Vector3.up);
            t.WasOpen = true;
            t.NextMeasure = 0f;
        }

        MeasureIfDue(t);
        // Sized by what the menu draws, not the whole 1920x1080 canvas: a small menu (pause) grows to a
        // readable size, full-screen ones keep MenuWidth. Its content centre stays where it spawned.
        var mpp = PanelMath.MenuMetersPerPixel(
            t.ContentSize.x,
            t.ContentSize.y,
            cfg.MenuWidth.Value,
            cfg.MenuDistance.Value
        );
        var position = t.Position - t.Rotation * (Vector3)(t.ContentCenter * mpp);
        Apply(t, position, t.Rotation, mpp * t.Size.x);
        return true;
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
    /// skipping full-canvas backdrops (dark ones are handed to <see cref="MenuBackdrop"/> to hide) and our own
    /// backdrop. Nothing drawn means the whole canvas.
    /// </summary>
    private static void MeasureContent(TrackedCanvas t)
    {
        var bounds = t.Rect.rect;
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        Graphic probe = null;
        var probeArea = 0f;
        t.Canvas.GetComponentsInChildren(false, graphics);
        foreach (var g in graphics)
        {
            if (!g.isActiveAndEnabled || g.color.a < 0.01f || g == t.Backdrop)
                continue;
            if (!Clip(t, g, bounds, out var lo, out var hi))
                continue;
            if (CoversCanvas(bounds, lo, hi))
            {
                if (t.Backdrop != null)
                    MenuBackdrop.OnFullCanvasGraphic(t, g);
                continue;
            }
            min = Vector2.Min(min, lo);
            max = Vector2.Max(max, hi);
            var area = (hi.x - lo.x) * (hi.y - lo.y);
            if (area > probeArea)
            {
                probeArea = area;
                probe = g;
            }
        }
        graphics.Clear();
        t.FadeProbe = probe;
        if (max.x <= min.x)
        {
            t.ContentSize = bounds.size;
            t.ContentCenter = bounds.center;
            return;
        }
        t.ContentSize = max - min;
        t.ContentCenter = (min + max) * 0.5f;
    }

    /// <summary>Hides dimmers on pages that are still closed, so they never flash before the next measure.</summary>
    private static void HideDimmers(TrackedCanvas t)
    {
        if (Degenerate(t))
            return;
        MenuBackdrop.PruneDimmers(t);
        var bounds = t.Rect.rect;
        t.Canvas.GetComponentsInChildren(true, graphics);
        // Transparent ones too: a dimmer faded in later (color.a rising from 0) stays hidden.
        foreach (var g in graphics)
            if (
                g != t.Backdrop
                && Clip(t, g, bounds, out var lo, out var hi)
                && CoversCanvas(bounds, lo, hi)
            )
                MenuBackdrop.OnFullCanvasGraphic(t, g);
        graphics.Clear();
    }

    /// <summary>A graphic's rect in canvas pixels, clipped to the canvas. False when nothing is left.</summary>
    private static bool Clip(
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

    /// <summary>Full-canvas backdrops (dimmers, vignettes) span at least 90% of the canvas both ways.</summary>
    private static bool CoversCanvas(Rect bounds, Vector2 lo, Vector2 hi) =>
        hi.x - lo.x >= bounds.width * 0.9f && hi.y - lo.y >= bounds.height * 0.9f;

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
