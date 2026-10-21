using System.Collections.Generic;
using System.Text;
using PeakXR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PeakXR.UI.Menu;

/// <summary>
/// The flat fills a loading screen draws behind (or over) its content: the black, white and cream washes, the
/// splash's black. On a world panel they read as an opaque card, so they are hidden through the canvas renderer
/// alpha, the way ui.world's BackdropStrip hides menu backdrops: the game's own colour and alpha writes still land,
/// and <c>cullTransparentMesh</c> is off while hidden so the alpha-0 mesh stays in the batch and keeps blocking
/// raycasts. Found by shape, not name: a plain, untextured fill covering the canvas. If nothing matches, nothing
/// changes. Only loading canvases (LoadingSpace) use it; menu canvases belong to ui.world.
/// </summary>
internal sealed class ScreenBacking
{
    // Sprites this small are flat fills (the game's procedural_ui_image_default_sprite is 4x4), not artwork.
    private const float PlainSpritePixels = 8f;

    private static readonly Vector3[] Corners = new Vector3[4];
    private static readonly List<Graphic> Scratch = new();
    private static readonly HashSet<string> Logged = new(System.StringComparer.Ordinal);

    private readonly List<Graphic> hidden = new();
    private readonly List<float> alphas = new();
    private readonly List<bool> culls = new();

    public Canvas Canvas { get; private set; }

    public int HiddenCount => hidden.Count;

    /// <summary>
    /// Hides the backing fills of <paramref name="canvas"/> and adds their colours to <paramref name="fills"/>.
    /// A fill another system already hid (renderer alpha 0) is left to it. Logs every graphic once per screen
    /// name. Graphics that are not a backing go to <paramref name="kept"/>.
    /// </summary>
    public void Scan(Canvas canvas, List<Rgba> fills, List<Graphic> kept)
    {
        const string tag = "loading";
        Restore();
        Canvas = canvas;
        var root = (RectTransform)canvas.transform;
        var bounds = root.rect;
        var name = Strip(root.root.name);
        var log = Logged.Add(tag + "/" + name) ? new StringBuilder() : null;
        log?.Append(
            $"[ui.menu] {tag} screen '{name}' canvas {bounds.width:0}x{bounds.height:0}, graphics:"
        );

        canvas.GetComponentsInChildren(true, Scratch);
        foreach (var g in Scratch)
        {
            Cover(root, g, bounds, out var coverX, out var coverY);
            var c = g.color;
            var color = new Rgba(c.r, c.g, c.b, c.a);
            var mask = g.TryGetComponent<Mask>(out _) || g.TryGetComponent<RectMask2D>(out _);
            var backing = LoadingPalette.IsBacking(coverX, coverY, IsPlain(g), mask);
            if (backing)
            {
                var renderer = g.canvasRenderer;
                if (renderer.GetAlpha() > 0f)
                {
                    hidden.Add(g);
                    alphas.Add(renderer.GetAlpha());
                    culls.Add(renderer.cullTransparentMesh);
                    renderer.cullTransparentMesh = false;
                    renderer.SetAlpha(0f);
                }
                fills.Add(color);
            }
            else
                kept.Add(g);
            if (log != null)
                Describe(log, root, g, color, coverX, coverY, backing);
        }
        Scratch.Clear();
        if (log != null)
            Plugin.Log.LogInfo(log.ToString());
    }

    /// <summary>Re-hides fills whose renderer alpha was reset (re-enabled objects). Allocation-free.</summary>
    public void Reassert()
    {
        for (int i = 0; i < hidden.Count; i++)
        {
            var g = hidden[i];
            if (g != null && g.canvasRenderer.GetAlpha() > 0f)
                g.canvasRenderer.SetAlpha(0f);
        }
    }

    public void Restore()
    {
        // Only what this hid, and an alpha only while it is still 0: another system's later write wins.
        for (int i = 0; i < hidden.Count; i++)
        {
            var g = hidden[i];
            if (g == null)
                continue;
            if (g.canvasRenderer.GetAlpha() <= 0f)
                g.canvasRenderer.SetAlpha(alphas[i]);
            g.canvasRenderer.cullTransparentMesh = culls[i];
        }
        hidden.Clear();
        alphas.Clear();
        culls.Clear();
        Canvas = null;
    }

    private static bool IsPlain(Graphic g) =>
        g switch
        {
            Image image => image.sprite == null
                || image.sprite.rect.width <= PlainSpritePixels
                    && image.sprite.rect.height <= PlainSpritePixels,
            RawImage raw => raw.texture == null,
            _ => false,
        };

    /// <summary>Share of the canvas width and height the graphic covers, clipped to the canvas.</summary>
    private static void Cover(
        RectTransform root,
        Graphic g,
        Rect bounds,
        out float coverX,
        out float coverY
    )
    {
        coverX = coverY = 0f;
        if (bounds.width <= 0f || bounds.height <= 0f)
            return;
        g.rectTransform.GetWorldCorners(Corners);
        Vector2 a = root.InverseTransformPoint(Corners[0]);
        Vector2 b = root.InverseTransformPoint(Corners[2]);
        var lo = Vector2.Max(Vector2.Min(a, b), bounds.min);
        var hi = Vector2.Min(Vector2.Max(a, b), bounds.max);
        coverX = Mathf.Max(0f, hi.x - lo.x) / bounds.width;
        coverY = Mathf.Max(0f, hi.y - lo.y) / bounds.height;
    }

    private static void Describe(
        StringBuilder log,
        RectTransform root,
        Graphic g,
        Rgba color,
        float coverX,
        float coverY,
        bool backing
    )
    {
        var size = g.rectTransform.rect.size;
        var art = g switch
        {
            Image image when image.sprite != null =>
                $"sprite {image.sprite.name} {image.sprite.rect.width:0}x{image.sprite.rect.height:0}",
            RawImage raw when raw.texture != null => $"texture {raw.texture.name}",
            TMP_Text => "text",
            _ => "plain",
        };
        var animated = g.GetComponentInParent<Animator>(true) != null ? "animator" : "static";
        log.Append("\n  ")
            .Append(backing ? "HIDE " : "keep ")
            .Append(Path(root, g.transform))
            .Append($" {g.GetType().Name} {size.x:0}x{size.y:0} cover {coverX:0.00}x{coverY:0.00}")
            .Append($" rgba {color} {art} {animated}")
            .Append(g.gameObject.activeInHierarchy ? "" : " inactive");
    }

    private static string Path(Transform root, Transform t)
    {
        var path = t.name;
        while (t != root && t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    private static string Strip(string name) =>
        name.EndsWith("(Clone)", System.StringComparison.Ordinal)
            ? name.Substring(0, name.Length - "(Clone)".Length)
            : name;
}
