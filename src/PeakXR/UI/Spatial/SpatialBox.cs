using System.Collections.Generic;
using PeakXR.Core;
using PeakXR.UI.Common;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace PeakXR.UI.Spatial;

/// <summary>
/// A small container under the converted HUD canvas that carries game HUD parts to a spot in the world (a target,
/// a controller button, a distant sign, another player). The parts stay inside the game's canvas, so the game keeps
/// writing their text (localised, runtime swaps included) and toggling them; only the container is posed. Moving a
/// part also hides its keyboard or gamepad glyphs (<c>InputIcon</c>, wrong in VR), outlines its text (nothing sits
/// behind it out there) and makes it draw over the world, after smoke and other transparent effects too, so it never
/// vanishes into a wall or a plume. <see cref="Restore"/> puts every part back exactly as it was.
/// </summary>
internal sealed class SpatialBox
{
    private struct Moved
    {
        public RectTransform Item;
        public Transform Parent;
        public int Sibling;
        public Vector2 AnchorMin,
            AnchorMax,
            Pivot,
            Position,
            Size;
        public Vector3 Scale;
    }

    private struct Swapped
    {
        public Graphic Graphic;
        public Material Original,
            Copy;
    }

    private struct Faded
    {
        public CanvasRenderer Renderer;
        public float Alpha;
    }

    /// <summary>Above every world effect, below the VR keyboard (30000) and the fade sphere.</summary>
    private const int SortingOrder = 29000;

    private static readonly int OutlineWidth = Shader.PropertyToID("_OutlineWidth");
    private static readonly Dictionary<(Material, bool), Material> onTop = new();
    private static readonly HashSet<Material> copies = new();
    private static readonly List<Graphic> graphics = new();
    private static readonly List<InputIcon> glyphs = new();
    private static readonly List<TMP_Text> texts = new();

    private readonly string name;
    private readonly List<Moved> moved = new();
    private readonly List<Swapped> swapped = new();
    private readonly List<TMP_Text> hiddenGlyphs = new();
    private readonly List<Faded> faded = new();
    private RectTransform root;

    public SpatialBox(string name) => this.name = name;

    public RectTransform Rect { get; private set; }

    public bool Holding => moved.Count > 0 && Rect != null;

    public int Count => moved.Count;

    public RectTransform Item(int i) => moved[i].Item;

    /// <summary>Moves <paramref name="item"/> into the box with its rect centre at <paramref name="at"/> (box
    /// pixels). Its size is kept even if it stretched with its old parent.</summary>
    public void Move(RectTransform hudRoot, Transform item, Vector2 at)
    {
        if (item is not RectTransform rect || hudRoot == null)
            return;
        Ensure(hudRoot);
        if (rect.parent == Rect)
            return;
        var size = rect.rect.size;
        moved.Add(
            new Moved
            {
                Item = rect,
                Parent = rect.parent,
                Sibling = rect.GetSiblingIndex(),
                AnchorMin = rect.anchorMin,
                AnchorMax = rect.anchorMax,
                Pivot = rect.pivot,
                Position = rect.anchoredPosition,
                Size = rect.sizeDelta,
                Scale = rect.localScale,
            }
        );
        rect.SetParent(Rect, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = at;
        HideGlyphs(rect);
        DrawOnTop(rect);
    }

    /// <summary>Places a moved part's centre at <paramref name="at"/> (box pixels).</summary>
    public void Place(int i, Vector2 at)
    {
        var item = moved[i].Item;
        if (item != null)
            item.anchoredPosition = at;
    }

    /// <summary>Shifts every part so the centre of <paramref name="focus"/> (a part or a child of one) sits at the
    /// box origin, which is where <see cref="Pose"/> puts it.</summary>
    public void CenterOn(RectTransform focus)
    {
        if (focus == null || Rect == null)
            return;
        Vector2 local = Rect.InverseTransformPoint(focus.TransformPoint(focus.rect.center));
        foreach (var m in moved)
        {
            if (m.Item != null)
                m.Item.anchoredPosition -= local;
        }
    }

    public void SetVisible(bool visible)
    {
        if (Rect != null && Rect.gameObject.activeSelf != visible)
            Rect.gameObject.SetActive(visible);
    }

    /// <summary>Puts every part back and hides the box.</summary>
    public void Restore()
    {
        for (int i = moved.Count - 1; i >= 0; i--)
        {
            var m = moved[i];
            if (m.Item == null || m.Parent == null)
                continue;
            m.Item.SetParent(m.Parent, false);
            m.Item.SetSiblingIndex(m.Sibling);
            m.Item.anchorMin = m.AnchorMin;
            m.Item.anchorMax = m.AnchorMax;
            m.Item.pivot = m.Pivot;
            m.Item.sizeDelta = m.Size;
            m.Item.anchoredPosition = m.Position;
            m.Item.localScale = m.Scale;
        }
        moved.Clear();
        // Reverse order: a part nested in another part was swapped twice and must unwind last-in first-out.
        for (int i = swapped.Count - 1; i >= 0; i--)
        {
            var s = swapped[i];
            // Only undo our own swap: a font change meanwhile (language switch) set a material we must keep.
            if (s.Graphic == null || s.Copy == null)
                continue;
            if (s.Graphic is TMP_Text text)
            {
                if (text.fontSharedMaterial == s.Copy)
                    text.fontSharedMaterial = s.Original;
            }
            else if (s.Graphic.material == s.Copy)
                s.Graphic.material = s.Original;
        }
        swapped.Clear();
        foreach (var f in faded)
        {
            if (f.Renderer != null)
                f.Renderer.SetAlpha(f.Alpha);
        }
        faded.Clear();
        foreach (var t in hiddenGlyphs)
        {
            if (t != null)
                t.enabled = true;
        }
        hiddenGlyphs.Clear();
        SetVisible(false);
    }

    /// <summary>The HUD died with its scene, and the box and its parts with it.</summary>
    public void Forget()
    {
        moved.Clear();
        swapped.Clear();
        hiddenGlyphs.Clear();
        faded.Clear();
        Rect = null;
        root = null;
    }

    /// <summary>Places the box origin at <paramref name="position"/>, one box pixel being
    /// <paramref name="metersPerPixel"/>. Allocation-free; call every frame.</summary>
    public void Pose(Vector3 position, Quaternion rotation, float metersPerPixel)
    {
        if (Rect == null)
            return;
        var parent = root != null ? root.lossyScale.x : 1f;
        var s = Mathf.Abs(parent) > 1e-6f ? metersPerPixel / parent : metersPerPixel;
        // A scale change re-meshes every text in the box; small ones (head bob) are not worth it.
        var current = Rect.localScale.x;
        if (current <= 0f || Mathf.Abs(s / current - 1f) > 0.015f)
            Rect.localScale = new Vector3(s, s, s);
        Rect.SetPositionAndRotation(position, rotation);
        // InputIcon turns its text back on when it is enabled again (OnEnable -> OnDeviceChange).
        for (int i = 0; i < hiddenGlyphs.Count; i++)
        {
            var t = hiddenGlyphs[i];
            if (t != null && t.enabled)
                t.enabled = false;
        }
    }

    /// <summary>Stacks the active parts top to bottom, in the order they were moved, centred on the box origin.
    /// Returns the stack height in box pixels.</summary>
    /// <remarks>Stacks what is drawn (the text, else the rect), not the rects: a game prompt can be a tall rect with
    /// its text at one end, which left the action half a screen below its name, out on the ground.</remarks>
    public float Stack(float spacing)
    {
        float height = 0f;
        foreach (var m in moved)
        {
            if (m.Item != null && m.Item.gameObject.activeSelf)
                height += ContentBounds(m.Item).height + (height > 0f ? spacing : 0f);
        }
        float y = height * 0.5f;
        bool first = true;
        foreach (var m in moved)
        {
            if (m.Item == null || !m.Item.gameObject.activeSelf)
                continue;
            if (!first)
                y -= spacing;
            first = false;
            var content = ContentBounds(m.Item);
            m.Item.anchoredPosition += new Vector2(-content.center.x, y - content.yMax);
            y -= content.height;
        }
        return height;
    }

    /// <summary>Box-local bounds (pixels) of the text <paramref name="item"/> draws, or of its rect when it shows no
    /// text. Measured on a layout change or a new target, not per frame.</summary>
    public Rect ContentBounds(RectTransform item)
    {
        bool any = false;
        var min = Vector2.zero;
        var max = Vector2.zero;
        item.GetComponentsInChildren(false, texts);
        foreach (var t in texts)
        {
            if (!t.enabled || string.IsNullOrEmpty(t.text))
                continue;
            if (t.havePropertiesChanged)
                t.ForceMeshUpdate();
            var b = t.textBounds;
            Vector2 lo = b.min,
                hi = b.max;
            if (b.size.y < 1e-3f)
            {
                var r = t.rectTransform.rect;
                lo = r.min;
                hi = r.max;
            }
            Add(t.transform, lo, hi, ref any, ref min, ref max);
        }
        texts.Clear();
        if (!any)
        {
            var r = item.rect;
            Add(item, r.min, r.max, ref any, ref min, ref max);
        }
        return new Rect(min, max - min);
    }

    private void Add(
        Transform from,
        Vector2 lo,
        Vector2 hi,
        ref bool any,
        ref Vector2 min,
        ref Vector2 max
    )
    {
        Vector2 a = Rect.InverseTransformPoint(from.TransformPoint(lo));
        Vector2 b = Rect.InverseTransformPoint(from.TransformPoint(hi));
        var l = Vector2.Min(a, b);
        var h = Vector2.Max(a, b);
        min = any ? Vector2.Min(min, l) : l;
        max = any ? Vector2.Max(max, h) : h;
        any = true;
    }

    /// <summary>Hides the flat-screen backings inside a moved part (every graphic that is not text) through the
    /// renderer alpha, so the game's own colour writes still land; <see cref="Restore"/> shows them again.</summary>
    public void HideBackings(Transform part)
    {
        part.GetComponentsInChildren(true, graphics);
        foreach (var g in graphics)
        {
            if (g is TMP_Text)
                continue;
            var cr = g.canvasRenderer;
            faded.Add(new Faded { Renderer = cr, Alpha = cr.GetAlpha() });
            cr.SetAlpha(0f);
        }
        graphics.Clear();
    }

    /// <summary>Destroys on-top copies whose source material died with its scene.</summary>
    public static void PruneMaterials()
    {
        List<(Material, bool)> dead = null;
        foreach (var pair in onTop)
        {
            if (pair.Key.Item1 == null)
                (dead ??= new List<(Material, bool)>()).Add(pair.Key);
        }
        copies.RemoveWhere(m => m == null);
        if (dead == null)
            return;
        foreach (var key in dead)
        {
            if (onTop.TryGetValue(key, out var copy) && copy != null)
                Object.Destroy(copy);
            onTop.Remove(key);
        }
    }

    /// <summary>Box-local bounds (pixels) of the active parts' rects, for stacking and sizing.</summary>
    public Rect ActiveBounds()
    {
        bool any = false;
        var min = Vector2.zero;
        var max = Vector2.zero;
        foreach (var m in moved)
        {
            if (m.Item == null || !m.Item.gameObject.activeSelf)
                continue;
            var r = m.Item.rect;
            var c = m.Item.anchoredPosition;
            var lo = c - r.size * 0.5f;
            var hi = c + r.size * 0.5f;
            min = any ? Vector2.Min(min, lo) : lo;
            max = any ? Vector2.Max(max, hi) : hi;
            any = true;
        }
        return new Rect(min, max - min);
    }

    private void Ensure(RectTransform hudRoot)
    {
        if (Rect != null && root == hudRoot)
            return;
        Forget();
        root = hudRoot;
        // Own (nested) Canvas: posing it every frame re-batches only this box, not the whole HUD canvas.
        Rect = new GameObject(
            name,
            typeof(RectTransform),
            typeof(Canvas)
        ).GetComponent<RectTransform>();
        Rect.SetParent(hudRoot, false);
        Rect.anchorMin = Rect.anchorMax = Rect.pivot = new Vector2(0.5f, 0.5f);
        Rect.sizeDelta = Vector2.zero;
        // Its own sorting: drawn after smoke, fire and other transparent effects, which would otherwise cut the text.
        var canvas = Rect.GetComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = SortingOrder;
    }

    private void HideGlyphs(RectTransform rect)
    {
        rect.GetComponentsInChildren(true, glyphs);
        foreach (var g in glyphs)
        {
            if (g.TryGetComponent(out TMP_Text text) && text.enabled)
            {
                text.enabled = false;
                hiddenGlyphs.Add(text);
            }
        }
        glyphs.Clear();
    }

    private void DrawOnTop(RectTransform rect)
    {
        rect.GetComponentsInChildren(true, graphics);
        foreach (var g in graphics)
        {
            Material original;
            Material copy;
            if (g is TMP_Text text)
            {
                original = text.fontSharedMaterial;
                if (original == null || IsCopy(original))
                    continue;
                var c = text.color;
                copy = OnTop(original, BackdropRules.NeedsLightHalo(c.r, c.g, c.b), halo: true);
                text.fontSharedMaterial = copy;
            }
            else
            {
                original = g.material;
                if (original == null || IsCopy(original))
                    continue;
                copy = OnTop(original, light: false, halo: false);
                g.material = copy;
            }
            swapped.Add(
                new Swapped
                {
                    Graphic = g,
                    Original = original,
                    Copy = copy,
                }
            );
        }
        graphics.Clear();
    }

    /// <summary>Already drawn on top (a part nested in another moved part).</summary>
    private static bool IsCopy(Material m) => copies.Contains(m);

    /// <summary>One shared copy per source material and halo shade that skips the depth test (uGUI and TMP shaders
    /// read their ZTest from <c>unity_GUIZTestMode</c>). Text also gets <see cref="TextHalo"/>'s thin contrasting
    /// outline unless the game already outlines it, so it reads on bright sand and snow without a panel.</summary>
    private static Material OnTop(Material source, bool light, bool halo)
    {
        var key = (source, halo && light);
        if (onTop.TryGetValue(key, out var copy) && copy != null)
            return copy;
        copy = new Material(source) { name = source.name + " (PeakXR on top)" };
        copy.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
        if (halo && source.HasProperty(OutlineWidth) && source.GetFloat(OutlineWidth) < 0.1f)
            TextHalo.Outline(copy, light);
        onTop[key] = copy;
        copies.Add(copy);
        return copy;
    }
}
