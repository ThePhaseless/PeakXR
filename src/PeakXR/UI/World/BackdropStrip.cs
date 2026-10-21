using System.Collections.Generic;
using System.Text;
using PeakXR.Core;
using PeakXR.UI.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PeakXR.UI.World;

/// <summary>One graphic hidden as a backdrop, with what it takes to show it again.</summary>
internal struct HiddenBackdrop
{
    public Graphic Graphic;

    /// <summary>The renderer alpha it had; negative for a mask graphic, hidden through <c>showMaskGraphic</c>.</summary>
    public float Alpha;
    public bool Panel;
}

/// <summary>
/// Takes the flat-screen backdrops off a world-space menu, so its buttons, text and icons float in the scene with
/// depth: full-canvas dimmers become hard slabs on a world panel, and the panels behind content only framed it on a
/// monitor. Which graphic is which is decided by <see cref="BackdropRules"/> from its role in the canvas, never from
/// names or paths. Hidden through the canvas renderer alpha (or <c>showMaskGraphic</c> for a mask), so the game's
/// raycasts, layout, masks and colour writes all behave as before. Runs when a page opens or changes, not per frame.
/// </summary>
internal static class BackdropStrip
{
    private static readonly List<Graphic> graphics = new();
    private static readonly List<Rect> rects = new();
    private static readonly List<Rect> panelRects = new();
    private static readonly List<TMP_Text> texts = new();
    private static readonly StringBuilder log = new();

    /// <summary>Hides every backdrop on the canvas not hidden yet, closed pages included so they never flash
    /// when they open. Panels and the text halo only when <paramref name="panels"/> is set. Two passes: dimmers and
    /// panels first, then the fills that belonged to a hidden panel (header bands, frames, card shadows), wherever
    /// they sit in the hierarchy.</summary>
    public static void Scan(TrackedCanvas t, bool panels)
    {
        Prune(t);
        var bounds = t.Rect.rect;
        if (bounds.width < 1f || bounds.height < 1f)
            return;
        t.Canvas.GetComponentsInChildren(true, graphics);
        for (int i = 0; i < graphics.Count; i++)
            rects.Add(
                WorldCanvases.Clip(t, graphics[i], bounds, out var lo, out var hi)
                    ? Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y)
                    : Rect.zero
            );

        log.Clear();
        Pass(t, bounds, panels, parts: false);
        if (panels)
        {
            foreach (var h in t.Hidden)
                if (
                    h.Panel
                    && h.Graphic != null
                    && WorldCanvases.Clip(t, h.Graphic, bounds, out var lo, out var hi)
                )
                    panelRects.Add(Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y));
            if (panelRects.Count > 0)
                Pass(t, bounds, panels, parts: true);
            panelRects.Clear();
        }
        graphics.Clear();
        rects.Clear();

        var halos = panels && t.Hidden.Count > 0 ? Halo(t) : 0;
        if (log.Length > 0 || halos > 0)
            Plugin.Log.LogInfo(
                $"[ui.world] canvas '{t.Canvas.name}': hid {(log.Length > 0 ? log.ToString() : "nothing new")}; text outline on {halos} more"
            );
    }

    /// <summary>Keeps hidden graphics hidden when the game's own tweens reset their renderer alpha.</summary>
    public static void Reassert(TrackedCanvas t)
    {
        foreach (var h in t.Hidden)
        {
            if (h.Graphic == null)
                continue;
            if (h.Alpha < 0f)
            {
                if (h.Graphic.TryGetComponent<Mask>(out var mask) && mask.showMaskGraphic)
                    mask.showMaskGraphic = false;
            }
            else if (h.Graphic.canvasRenderer.GetAlpha() > 0f)
                h.Graphic.canvasRenderer.SetAlpha(0f);
        }
    }

    /// <summary>Shows hidden graphics again (all, or only panels) and puts the text materials back.</summary>
    public static void Restore(TrackedCanvas t, bool panelsOnly)
    {
        for (int i = t.Hidden.Count - 1; i >= 0; i--)
        {
            var h = t.Hidden[i];
            if (panelsOnly && !h.Panel)
                continue;
            if (h.Graphic != null)
            {
                if (h.Alpha >= 0f)
                    h.Graphic.canvasRenderer.SetAlpha(h.Alpha);
                else if (h.Graphic.TryGetComponent<Mask>(out var mask))
                    mask.showMaskGraphic = true;
            }
            t.Hidden.RemoveAt(i);
        }
        for (int i = 0; i < t.HaloTexts.Count; i++)
            if (t.HaloTexts[i] != null)
                t.HaloTexts[i].fontSharedMaterial = t.HaloOriginals[i];
        t.HaloTexts.Clear();
        t.HaloOriginals.Clear();
    }

    private static void Pass(TrackedCanvas t, Rect bounds, bool panels, bool parts)
    {
        for (int i = 0; i < graphics.Count; i++)
        {
            var g = graphics[i];
            // Switched off on its own (a selection highlight, a warning icon): state, not a backdrop.
            if (
                rects[i].width <= 0f
                || !g.enabled
                || !g.gameObject.activeSelf
                || IndexOf(t, g) >= 0
            )
                continue;
            var traits = Traits(i, bounds);
            if (parts)
                traits.PanelShare = PanelShare(rects[i]);
            var role = BackdropRules.Classify(traits);
            if (role == BackdropRole.Content || (role == BackdropRole.Panel && !panels))
                continue;
            Hide(t, g, role == BackdropRole.Panel);
            log.Append(log.Length == 0 ? "" : ", ")
                .Append(role)
                .Append(" '")
                .Append(g.name)
                .Append('\'');
        }
    }

    private static BackdropTraits Traits(int i, Rect bounds)
    {
        var g = graphics[i];
        var r = rects[i];
        var traits = new BackdropTraits
        {
            IsImage = g is Image or RawImage,
            WidthFraction = r.width / bounds.width,
            HeightFraction = r.height / bounds.height,
        };
        if (!traits.IsImage)
            return traits;
        traits.Interactive = g.GetComponentInParent<Selectable>(true) != null;
        traits.LiveTexture = g is RawImage raw && raw.texture is RenderTexture;
        traits.Gauge = g is Image { type: Image.Type.Filled };
        if (traits.Interactive || traits.LiveTexture || traits.Gauge)
            return traits;
        // A plain fill: no picture, or a 9-sliced or tiled panel sprite (ProceduralImage draws without one).
        traits.Flat = g is Image img
            ? img.sprite == null || img.type is Image.Type.Sliced or Image.Type.Tiled
            : ((RawImage)g).texture == null;
        for (int j = i + 1; j < graphics.Count; j++)
            // Hierarchy order is draw order within one canvas: later graphics sit on top.
            if (rects[j].width > 0f && r.Contains(rects[j].center))
                traits.ContentAbove++;
        return traits;
    }

    /// <summary>Largest share of a hidden panel's area that <paramref name="r"/> covers, counting only panels it
    /// lies mostly inside.</summary>
    private static float PanelShare(Rect r)
    {
        float best = 0f;
        float area = r.width * r.height;
        foreach (var p in panelRects)
        {
            float w = Mathf.Min(r.xMax, p.xMax) - Mathf.Max(r.xMin, p.xMin);
            float h = Mathf.Min(r.yMax, p.yMax) - Mathf.Max(r.yMin, p.yMin);
            if (w <= 0f || h <= 0f || w * h < 0.5f * area)
                continue;
            best = Mathf.Max(best, w * h / (p.width * p.height));
        }
        return best;
    }

    private static void Hide(TrackedCanvas t, Graphic g, bool panel)
    {
        float alpha;
        // A mask graphic at alpha 0 stops writing its stencil and would hide everything it masks.
        if (g.TryGetComponent<Mask>(out var mask))
        {
            alpha = -1f;
            mask.showMaskGraphic = false;
        }
        else
        {
            alpha = g.canvasRenderer.GetAlpha();
            g.canvasRenderer.SetAlpha(0f);
        }
        t.Hidden.Add(
            new HiddenBackdrop
            {
                Graphic = g,
                Alpha = alpha,
                Panel = panel,
            }
        );
    }

    /// <summary>Text that sat on a panel now sits on the world: give it an outline. Returns how many changed.</summary>
    private static int Halo(TrackedCanvas t)
    {
        int count = 0;
        t.Canvas.GetComponentsInChildren(true, texts);
        foreach (var text in texts)
        {
            if (t.HaloTexts.Contains(text))
                continue;
            var original = TextHalo.Apply(text);
            if (original == null)
                continue;
            t.HaloTexts.Add(text);
            t.HaloOriginals.Add(original);
            count++;
        }
        texts.Clear();
        return count;
    }

    private static int IndexOf(TrackedCanvas t, Graphic g)
    {
        for (int i = 0; i < t.Hidden.Count; i++)
            if (t.Hidden[i].Graphic == g)
                return i;
        return -1;
    }

    /// <summary>Forgets graphics and texts destroyed with their page.</summary>
    private static void Prune(TrackedCanvas t)
    {
        t.Hidden.RemoveAll(h => h.Graphic == null);
        for (int i = t.HaloTexts.Count - 1; i >= 0; i--)
            if (t.HaloTexts[i] == null)
            {
                t.HaloTexts.RemoveAt(i);
                t.HaloOriginals.RemoveAt(i);
            }
    }
}
