using PeakXR.Core;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.UI;

namespace PeakXR.UI.World;

/// <summary>
/// The dark rounded plate behind a world-space menu (<c>UI.MenuBackplate</c>): the first child of the canvas, so it
/// draws under everything, sized to the content still drawn after <see cref="BackdropStrip"/> (the game's hidden
/// panels and dimmers do not count) plus <see cref="MenuBackplate.MarginPixels"/>. Never a raycast target, so clicks
/// and the laser behave as before. Resized whenever <see cref="WorldCanvases"/> re-measures the menu (page change,
/// refill, and every 0.5 s, which catches an open dropdown list).
/// </summary>
internal static class BackplateView
{
    /// <summary>Graphics further than this off the canvas plane (title buttons brought to arm's reach) have left the
    /// panel and stay off its plate.</summary>
    private const float OffPlanePixels = 50f;

    private static Sprite sprite;

    /// <summary>Whether <paramref name="g"/> is drawn on the panel and so sits on the plate.</summary>
    public static bool Counts(TrackedCanvas t, Graphic g) =>
        g != t.Plate
        && g.canvasRenderer.GetAlpha() * g.canvasRenderer.GetInheritedAlpha() > 0.01f
        && Mathf.Abs(t.Rect.InverseTransformPoint(g.rectTransform.position).z) < OffPlanePixels;

    /// <summary>Fits the plate to <paramref name="min"/>..<paramref name="max"/> (canvas pixels about the pivot), or
    /// hides it when off, out of VR or nothing is drawn. Allocates only when the plate is first made.</summary>
    public static void Apply(TrackedCanvas t, Vector2 min, Vector2 max)
    {
        t.PlateContentMin = min;
        t.PlateContentMax = max;
        var cfg = UIWorldFeature.Settings;
        if (
            !VRSession.Active
            || !cfg.MenuBackplate.Value
            || !MenuBackplate.Fit(
                new System.Numerics.Vector2(min.x, min.y),
                new System.Numerics.Vector2(max.x, max.y),
                MenuBackplate.MarginPixels,
                out var center,
                out var size
            )
            || Scattered(t, center, size)
        )
        {
            if (t.Plate != null && t.Plate.gameObject.activeSelf)
                t.Plate.gameObject.SetActive(false);
            return;
        }
        if (t.Plate == null)
            t.Plate = Create(t);
        var go = t.Plate.gameObject;
        if (!go.activeSelf)
            go.SetActive(true);
        var rt = t.Plate.rectTransform;
        // Anchored at the canvas pivot, the canvas's local origin, which is what the measure is relative to.
        if (rt.anchorMin != t.Rect.pivot)
            rt.anchorMin = rt.anchorMax = t.Rect.pivot;
        // Set only on a change: each write dirties the canvas.
        var at = new Vector2(center.X, center.Y);
        var span = new Vector2(size.X, size.Y);
        if (rt.anchoredPosition != at)
            rt.anchoredPosition = at;
        if (rt.sizeDelta != span)
            rt.sizeDelta = span;
        if (rt.GetSiblingIndex() != 0)
            rt.SetAsFirstSibling();
        var c = MenuBackplate.Color;
        var color = new Color(c.X, c.Y, c.Z, cfg.MenuBackplateOpacity.Value);
        if (t.Plate.color != color)
            t.Plate.color = color;
    }

    /// <summary>The content is spread over more than <see cref="MenuBackplate.MaxSpanDegrees"/> of view (the title's
    /// sign, logo and credits): one plate around it would be the full-view 2D panel, so the menu gets none. Logged when
    /// that changes.</summary>
    private static bool Scattered(
        TrackedCanvas t,
        System.Numerics.Vector2 center,
        System.Numerics.Vector2 size
    )
    {
        var rig = VRRig.Instance;
        if (rig == null || rig.Head == null)
            return false;
        float mpp = Mathf.Abs(t.Rect.lossyScale.x);
        var at = t.Rect.TransformPoint(new Vector3(center.X, center.Y, 0f));
        float distance = Vector3.Distance(at, rig.Head.position);
        t.PlateSpanDegrees = MenuBackplate.SpanDegrees(
            Mathf.Max(size.X, size.Y) * mpp,
            Mathf.Max(distance, 1e-3f)
        );
        bool scattered = MenuBackplate.Scattered(size.X * mpp, size.Y * mpp, distance);
        if (scattered != t.PlateScattered)
        {
            t.PlateScattered = scattered;
            Plugin.Log.LogInfo(
                $"[ui.world] canvas '{t.Canvas.name}': content spans {t.PlateSpanDegrees:0} deg, menu backplate "
                    + (scattered ? "skipped" : "back")
            );
        }
        return scattered;
    }

    public static void Remove(TrackedCanvas t)
    {
        if (t.Plate != null)
            Object.Destroy(t.Plate.gameObject);
        t.Plate = null;
    }

    private static Image Create(TrackedCanvas t)
    {
        var go = new GameObject(
            "PeakXR_MenuBackplate",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(LayoutElement)
        );
        go.layer = t.Canvas.gameObject.layer;
        go.GetComponent<LayoutElement>().ignoreLayout = true;
        var rt = (RectTransform)go.transform;
        rt.SetParent(t.Rect, false);
        rt.pivot = new Vector2(0.5f, 0.5f);
        var image = go.GetComponent<Image>();
        image.sprite = Sprite();
        image.type = Image.Type.Sliced;
        image.raycastTarget = false;
        Plugin.Log.LogInfo($"[ui.world] canvas '{t.Canvas.name}': menu backplate added");
        return image;
    }

    /// <summary>A white rounded square, 9-sliced so any plate size keeps the same corner radius.</summary>
    private static Sprite Sprite()
    {
        if (sprite != null)
            return sprite;
        const int r = (int)MenuBackplate.CornerPixels;
        const int size = 2 * r + 16;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "PeakXR_MenuBackplate",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave,
        };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // Distance past the corner circle's centre, antialiased over one texel.
            float dx = Mathf.Max(0f, Mathf.Max(r - (x + 0.5f), x + 0.5f - (size - r)));
            float dy = Mathf.Max(0f, Mathf.Max(r - (y + 0.5f), y + 0.5f - (size - r)));
            float a = Mathf.Clamp01(r + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
            pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        sprite = UnityEngine.Sprite.Create(
            tex,
            new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(r, r, r, r)
        );
        sprite.hideFlags = HideFlags.DontSave;
        return sprite;
    }
}
