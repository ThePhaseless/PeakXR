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

    /// <summary>Its renderer's <c>cullTransparentMesh</c> before it was hidden.</summary>
    public bool Cull;
    public bool Panel;
}

/// <summary>
/// Takes the flat-screen backdrops off a world-space menu, so its buttons, text and icons float in the scene with
/// depth: full-canvas dimmers become hard slabs on a world panel, and the panels behind content only framed it on a
/// monitor. Which graphic is which is decided by <see cref="BackdropRules"/> from its role in the canvas, never from
/// names or paths. Hidden through the canvas renderer alpha (or <c>showMaskGraphic</c> for a mask), so layout, masks
/// and the game's colour writes behave as before. Runs when a page opens or changes, not per frame.
/// </summary>
/// <remarks>
/// Raycasts stay vanilla: GraphicRaycaster skips a graphic whose <c>depth</c> is -1 (UnityEngine.UI
/// GraphicRaycaster.Raycast), and a renderer with <c>cullTransparentMesh</c> drops its alpha-0 mesh from the batch,
/// so a hidden graphic keeps its mesh in the batch (fully transparent) for as long as it is hidden.
/// </remarks>
internal static class BackdropStrip
{
    private static readonly List<Graphic> graphics = new();
    private static readonly List<Rect> rects = new();
    private static readonly List<Rect> panelRects = new();
    private static readonly List<Transform> panelGroups = new();
    private static readonly List<TMP_Text> texts = new();
    private static readonly StringBuilder log = new();

    /// <summary>Hides every backdrop on the canvas not hidden yet, closed pages included so they never flash
    /// when they open. Panels and the text halo only when <paramref name="panels"/> is set. Two passes: dimmers and
    /// panels first, then the fills that belonged to a hidden panel (header bands, frames, card shadows, a tooltip
    /// card), wherever they sit in the hierarchy.</summary>
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
            {
                if (!h.Panel || h.Graphic == null)
                    continue;
                if (WorldCanvases.Clip(t, h.Graphic, bounds, out var lo, out var hi))
                    panelRects.Add(Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y));
                // The panel's parent groups its card; the canvas root would group everything.
                var group = h.Graphic.transform.parent;
                if (group != null && group != t.Rect && !panelGroups.Contains(group))
                    panelGroups.Add(group);
            }
            if (panelRects.Count > 0 || panelGroups.Count > 0)
                Pass(t, bounds, panels, parts: true);
            panelRects.Clear();
            panelGroups.Clear();
        }
        graphics.Clear();
        rects.Clear();

        var halos = panels && t.Hidden.Count > 0 ? Halo(t) : 0;
        if (log.Length > 0 || halos > 0)
            Plugin.Log.LogInfo(
                $"[ui.world] canvas '{t.Canvas.name}': hid {(log.Length > 0 ? log.ToString() : "nothing new")}; text halo on {halos} more"
            );
    }

    /// <summary>
    /// Called by the slow re-measure (0.5 s), never a hierarchy scan: keeps hidden graphics hidden when the game's own
    /// tweens reset their renderer alpha, and hides a dimmer that was off at scan time once it switches itself on.
    /// </summary>
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
        for (int i = t.PendingDimmers.Count - 1; i >= 0; i--)
        {
            var g = t.PendingDimmers[i];
            if (g == null)
                t.PendingDimmers.RemoveAt(i);
            else if (g.isActiveAndEnabled && g.canvasRenderer.GetAlpha() > 0f)
            {
                t.PendingDimmers.RemoveAt(i);
                if (IndexOf(t, g) < 0 && Hide(t, g, panel: false))
                    Plugin.Log.LogInfo(
                        $"[ui.world] canvas '{t.Canvas.name}': hid Dimmer '{g.name}' (switched on)"
                    );
            }
        }
    }

    /// <summary>Shows hidden graphics again (all, or only panels) and puts the text materials back. A value
    /// another system changed since (its own hide or show) is left as that system set it.</summary>
    public static void Restore(TrackedCanvas t, bool panelsOnly)
    {
        for (int i = t.Hidden.Count - 1; i >= 0; i--)
        {
            var h = t.Hidden[i];
            if (panelsOnly && !h.Panel)
                continue;
            var g = h.Graphic;
            if (g != null)
            {
                if (h.Alpha < 0f)
                {
                    if (g.TryGetComponent<Mask>(out var mask) && !mask.showMaskGraphic)
                        mask.showMaskGraphic = true;
                }
                else
                {
                    if (g.canvasRenderer.GetAlpha() <= 0f)
                        g.canvasRenderer.SetAlpha(h.Alpha);
                    g.canvasRenderer.cullTransparentMesh = h.Cull;
                }
            }
            t.Hidden.RemoveAt(i);
        }
        if (!panelsOnly)
            t.PendingDimmers.Clear();
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
            if (rects[i].width <= 0f || IndexOf(t, g) >= 0)
                continue;
            var traits = Traits(i, bounds);
            if (parts)
            {
                traits.PanelShare = PanelShare(rects[i]);
                traits.InPanelGroup = InPanelGroup(g.transform);
            }
            var role = BackdropRules.Classify(traits);
            if (role == BackdropRole.Content || (role == BackdropRole.Panel && !panels))
                continue;
            // Switched off on its own (a selection highlight), or already hidden by another system: state, not ours.
            // A dimmer is watched instead, and hidden once it shows (Reassert).
            if (!g.enabled || !g.gameObject.activeSelf || g.canvasRenderer.GetAlpha() <= 0f)
            {
                if (role == BackdropRole.Dimmer && !t.PendingDimmers.Contains(g))
                    t.PendingDimmers.Add(g);
                continue;
            }
            if (!Hide(t, g, role == BackdropRole.Panel))
                continue;
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

    private static bool InPanelGroup(Transform tr)
    {
        if (panelGroups.Count == 0)
            return false;
        for (var p = tr.parent; p != null; p = p.parent)
            if (panelGroups.Contains(p))
                return true;
        return false;
    }

    /// <summary>Hides one graphic. False when it was already hidden by someone else (nothing recorded).</summary>
    private static bool Hide(TrackedCanvas t, Graphic g, bool panel)
    {
        var h = new HiddenBackdrop { Graphic = g, Panel = panel };
        // A mask graphic at alpha 0 stops writing its stencil and would hide everything it masks.
        if (g.TryGetComponent<Mask>(out var mask))
        {
            if (!mask.showMaskGraphic)
                return false;
            h.Alpha = -1f;
            mask.showMaskGraphic = false;
        }
        else
        {
            var cr = g.canvasRenderer;
            h.Alpha = cr.GetAlpha();
            if (h.Alpha <= 0f)
                return false;
            h.Cull = cr.cullTransparentMesh;
            // Keeps the invisible mesh batched, so it keeps its depth and blocks or catches raycasts as before.
            cr.cullTransparentMesh = false;
            cr.SetAlpha(0f);
        }
        t.Hidden.Add(h);
        return true;
    }

    /// <summary>Text that sat on a panel now sits on the world: give it a halo. Text on a backing that stays (a
    /// button's ribbon, a row's plate) reads as it did and is left alone. Returns how many changed.</summary>
    private static int Halo(TrackedCanvas t)
    {
        int count = 0;
        t.Canvas.GetComponentsInChildren(true, texts);
        foreach (var text in texts)
        {
            if (t.HaloTexts.Contains(text) || OnKeptBacking(t, text))
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

    /// <summary>An image under the text (an ancestor, or the button's target graphic) is drawn and covers the text's
    /// centre. Structural, so a closed page is judged the same as an open one.</summary>
    private static bool OnKeptBacking(TrackedCanvas t, TMP_Text text)
    {
        var rt = text.rectTransform;
        var centre = rt.TransformPoint(rt.rect.center);
        for (var p = rt.parent; p != null && p != t.Rect; p = p.parent)
        {
            if (p.TryGetComponent<Image>(out var image) && Backs(image, centre))
                return true;
            if (
                p.TryGetComponent<Selectable>(out var selectable)
                && selectable.targetGraphic is Image target
                && Backs(target, centre)
            )
                return true;
        }
        return false;
    }

    private static bool Backs(Image image, Vector3 centre)
    {
        if (!image.enabled || image.color.a < 0.5f || image.canvasRenderer.GetAlpha() < 0.5f)
            return false;
        if (image.TryGetComponent<Mask>(out var mask) && !mask.showMaskGraphic)
            return false;
        var r = image.rectTransform;
        return r.rect.Contains(r.InverseTransformPoint(centre));
    }

    private static int IndexOf(TrackedCanvas t, Graphic g)
    {
        for (int i = 0; i < t.Hidden.Count; i++)
            if (t.Hidden[i].Graphic == g)
                return i;
        return -1;
    }

    /// <summary>Hidden graphics on shown pages that GraphicRaycaster would now skip (self-test: should be none).</summary>
    public static int Unraycastable(TrackedCanvas t)
    {
        int count = 0;
        foreach (var h in t.Hidden)
        {
            var g = h.Graphic;
            if (
                g != null
                && h.Alpha >= 0f
                && g.raycastTarget
                && g.isActiveAndEnabled
                && (g.canvasRenderer.cull || g.depth == -1)
            )
                count++;
        }
        return count;
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
