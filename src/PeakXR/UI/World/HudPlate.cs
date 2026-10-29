using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PeakXR.UI.World;

/// <summary>
/// Layout and dark plate of HUD parts lifted into a <see cref="HudBox"/> (the wrist display, the top-of-view stamina
/// meter). The stamina bar is translucent: over the forearm's sleeve or bright sky it read as sunk into what is behind.
/// The layout follows the stamina bar's frame, the plate hugs the graphics drawn now.
/// </summary>
internal sealed class HudPlate
{
    /// <summary>Plate margin round the parts, in HUD pixels.</summary>
    internal const float Pad = 14f;
    private const float Opacity = 0.85f;
    private static readonly Vector3[] corners = new Vector3[4];

    /// <summary>Every graphic under the box, shown or not, refilled when a group shows or hides: the plate hugs the
    /// ones drawn now. The groups' own rects reserve room for parts that are mostly hidden (extra stamina, affliction
    /// segments, the morale text, the numb mushrooms at alpha 0), which left the plate far wider and taller than the
    /// bar. Stencil-only masks are left out (<see cref="PointerHitRule.MaskOnly"/>).</summary>
    private readonly List<Graphic> graphics = new();
    private int signature;
    private int measuredMask;

    /// <summary>Children of the box when <see cref="graphics"/> was filled: a part added later refills it.</summary>
    private int measuredChildren;

    public Image Plate { get; private set; }

    /// <summary>Layout centre and width in box pixels: the stamina bar's frame, else the groups' bounds. Until
    /// something is measured, about the width of the stamina bar, never 1 px (a screen-sized panel).</summary>
    public Vector2 Center;
    public float Span = 400f;

    /// <summary>Size of the moved groups' rects (self-test).</summary>
    public Vector2 GroupsSize;

    /// <summary>Drawn content the plate was last fitted to, and the plate's centre and size, box pixels (self-test).</summary>
    public Vector2 ContentMin,
        ContentMax,
        PlateCenter,
        PlateSize;

    /// <summary>Puts the plate first in <paramref name="box"/> (a new box after a scene change gets a new plate);
    /// <see cref="Measure"/> fits it. Drawn on top with the box, under the parts.</summary>
    public void Ensure(RectTransform box)
    {
        if (Plate == null || Plate.transform.parent != box)
        {
            var go = new GameObject(
                box.name + "Plate",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(PanelPart)
            );
            go.layer = box.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(box, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            Plate = go.GetComponent<Image>();
            Plate.sprite = BackplateView.PlateSprite();
            Plate.type = Image.Type.Sliced;
            Plate.raycastTarget = false;
            var c = MenuBackplate.Color;
            Plate.color = new Color(c.X, c.Y, c.Z, Opacity);
        }
        Plate.transform.SetAsFirstSibling();
    }

    /// <summary>A group showed or hid, or a part was added since the last <see cref="Measure"/>.</summary>
    public bool Stale(RectTransform box) =>
        ShownMask(box) != measuredMask || box.childCount != measuredChildren;

    /// <summary>Measures the moved groups that are shown: the box itself is HUD-sized and would make the span the
    /// whole HUD. Runs on apply and when a group shows or hides (the dying bar), not every frame.</summary>
    public void Measure(RectTransform box, RectTransform frame)
    {
        measuredMask = ShownMask(box);
        bool any = false;
        var bounds = default(Bounds);
        var plate = Plate != null ? Plate.transform : null;
        for (int i = 0; i < box.childCount; i++)
        {
            var child = box.GetChild(i);
            if (!child.gameObject.activeSelf || child == plate)
                continue;
            var b = RectTransformUtility.CalculateRelativeRectTransformBounds(box, child);
            if (any)
                bounds.Encapsulate(b);
            else
                bounds = b;
            any = true;
        }
        if (any)
            GroupsSize = bounds.size;
        // Layout and scale follow the stamina bar's frame (A/StaminaBar.cs:135), the bar the player reads, so the
        // width setting is its width and the anchor its centre, and nothing jumps when a part shows. The groups'
        // rects also hold the frame's 1214 px stencil mask (Bar/OutlineMask): the bar came out half as wide, off-centre.
        // shortcut: a frame grown past 614 px by overflowing afflictions (statusSum > 1) when this runs shrinks the
        // bar until the next measure; measure at statusSum 1 if that shows up in play.
        if (frame != null && frame.gameObject.activeInHierarchy && frame.IsChildOf(box))
        {
            frame.GetWorldCorners(corners);
            Vector2 a = box.InverseTransformPoint(corners[0]);
            Vector2 b = box.InverseTransformPoint(corners[2]);
            Center = (a + b) * 0.5f;
            Span = Mathf.Max(1f, Mathf.Abs(b.x - a.x));
        }
        else if (any)
        {
            // No bar in the box (spectating moves it to the friend's nameplate): the hotbar groups set the layout.
            Center = bounds.center;
            Span = Mathf.Max(1f, bounds.size.x);
        }
        box.GetComponentsInChildren(true, graphics);
        // The bar's OutlineMask (Image + Mask, showMaskGraphic off) spans 1214 px round the 614 px frame it clips and
        // drew the plate twice the bar's length. The game never toggles these masks, so they are dropped here, once.
        graphics.RemoveAll(PointerHitRule.MaskOnly);
        measuredChildren = box.childCount;
        signature = 0;
        Fit(box);
    }

    /// <summary>Fits the plate to the graphics drawn now. Each frame folds their visibility, size and local position
    /// into a signature (no allocation, no world corners), in <see cref="MenuBackplate.FoldPixels"/> steps: the
    /// stamina fill's per-frame lerp (A/StaminaBar.cs:126) re-measures once per step, not every frame.
    /// shortcut: a parent moving with no graphic under it changing is missed until the next change; fold parent
    /// positions in if a part ever slides that way.</summary>
    public void Fit(RectTransform box)
    {
        if (Plate == null)
            return;
        int sig = MenuBackplate.FoldStart;
        for (int i = 0; i < graphics.Count; i++)
        {
            var g = graphics[i];
            bool drawn = Drawn(g);
            var r = drawn ? g.rectTransform.rect : default;
            var at = drawn ? g.rectTransform.localPosition : default;
            sig = MenuBackplate.Fold(sig, drawn, at.x + r.x, at.y + r.y, r.width, r.height);
        }
        if (sig == signature)
            return;
        signature = sig;

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        for (int i = 0; i < graphics.Count; i++)
        {
            var g = graphics[i];
            if (!Drawn(g))
                continue;
            g.rectTransform.GetWorldCorners(corners);
            Vector2 a = box.InverseTransformPoint(corners[0]);
            Vector2 b = box.InverseTransformPoint(corners[2]);
            min = Vector2.Min(min, Vector2.Min(a, b));
            max = Vector2.Max(max, Vector2.Max(a, b));
        }
        bool any = MenuBackplate.Fit(min.N(), max.N(), Pad, out var center, out var size);
        if (any)
        {
            var rt = Plate.rectTransform;
            rt.anchoredPosition = PlateCenter = new Vector2(center.X, center.Y);
            rt.sizeDelta = PlateSize = new Vector2(size.X, size.Y);
            ContentMin = min;
            ContentMax = max;
        }
        if (Plate.gameObject.activeSelf != any)
            Plate.gameObject.SetActive(any);
    }

    /// <summary>A graphic of the parts that reaches the screen: shown, enabled, not faded out by its colour or a
    /// canvas group, and not empty. The plate never counts.</summary>
    private bool Drawn(Graphic g) =>
        g != null
        && g != Plate
        && g.isActiveAndEnabled
        && g.color.a >= 0.01f
        && g.canvasRenderer.GetAlpha() * g.canvasRenderer.GetInheritedAlpha() > 0.01f
        && g.rectTransform.rect.width > 0.5f
        && g.rectTransform.rect.height > 0.5f;

    private int ShownMask(RectTransform box)
    {
        int mask = 0;
        int n = Mathf.Min(box.childCount, 31);
        var plate = Plate != null ? Plate.transform : null;
        for (int i = 0; i < n; i++)
        {
            var child = box.GetChild(i);
            if (child != plate && child.gameObject.activeSelf)
                mask |= 1 << i;
        }
        return mask;
    }
}
