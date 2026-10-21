using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace PeakXR.UI.Spatial;

/// <summary>
/// A small container under the converted HUD canvas that carries game HUD parts to a spot in the world (a target,
/// a controller button, a distant sign, another player). The parts stay inside the game's canvas, so the game keeps
/// writing their text (localised, runtime swaps included) and toggling them; only the container is posed. Moving a
/// part also hides its keyboard or gamepad glyphs (<c>InputIcon</c>, wrong in VR) and makes it draw over the world,
/// so it never vanishes into a wall. <see cref="Restore"/> puts every part back exactly as it was.
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
    }

    private struct Swapped
    {
        public Graphic Graphic;
        public Material Original;
    }

    private static readonly Dictionary<Material, Material> onTop = new();
    private static readonly List<Graphic> graphics = new();
    private static readonly List<InputIcon> glyphs = new();

    private readonly string name;
    private readonly List<Moved> moved = new();
    private readonly List<Swapped> swapped = new();
    private readonly List<TMP_Text> hiddenGlyphs = new();
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
        }
        moved.Clear();
        foreach (var s in swapped)
        {
            // Only undo our own swap: a font change meanwhile (language switch) set a material we must keep.
            if (s.Graphic == null || !onTop.TryGetValue(s.Original, out var copy))
                continue;
            if (s.Graphic is TMP_Text text)
            {
                if (text.fontSharedMaterial == copy)
                    text.fontSharedMaterial = s.Original;
            }
            else if (s.Graphic.material == copy)
                s.Graphic.material = s.Original;
        }
        swapped.Clear();
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
        Rect.localScale = new Vector3(s, s, s);
        Rect.SetPositionAndRotation(position, rotation);
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
            if (g is TMP_Text text)
            {
                var original = text.fontSharedMaterial;
                if (original == null)
                    continue;
                text.fontSharedMaterial = OnTop(original);
                swapped.Add(new Swapped { Graphic = g, Original = original });
            }
            else
            {
                var original = g.material;
                g.material = OnTop(original);
                swapped.Add(new Swapped { Graphic = g, Original = original });
            }
        }
        graphics.Clear();
    }

    /// <summary>One shared copy per source material that skips the depth test (uGUI and TMP shaders read their
    /// ZTest from <c>unity_GUIZTestMode</c>).</summary>
    private static Material OnTop(Material source)
    {
        if (onTop.TryGetValue(source, out var copy) && copy != null)
            return copy;
        copy = new Material(source) { name = source.name + " (PeakXR on top)" };
        copy.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
        onTop[source] = copy;
        return copy;
    }
}
