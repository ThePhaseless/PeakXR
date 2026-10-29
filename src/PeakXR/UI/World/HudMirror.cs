using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PeakXR.UI.World;

/// <summary>
/// A second, script-free copy of HUD groups the game drives (the stamina bar when <c>UI.HUD.StaminaView</c> is Both),
/// so one bar reads in two places. The copy keeps only what draws (graphics, masks, canvas groups); every frame it
/// takes the original's shown state, layout, colours, sprites, fills and text, so the game's own animation shows in
/// both and the copy never plays a sound or reacts to the game by itself.
/// </summary>
internal sealed class HudMirror
{
    private readonly List<GameObject> copies = new();
    private readonly List<RectTransform> from = new(),
        to = new();
    private readonly List<Graphic> fromGraphics = new(),
        toGraphics = new();
    private readonly List<CanvasGroup> fromGroups = new(),
        toGroups = new();
    private static readonly List<RectTransform> rects = new();
    private static readonly List<Graphic> drawn = new();
    private static readonly List<CanvasGroup> groups = new();
    private static readonly List<Behaviour> scripts = new();

    /// <summary>Copies <paramref name="group"/> into <paramref name="box"/> at the same layout.
    /// shortcut: parts the game adds to the group later (a TMP fallback sub-mesh) are not copied; rebuild on a child
    /// count change if one shows up missing.</summary>
    public void Add(Transform group, RectTransform box)
    {
        // Copied under an inactive holder, so no script on the copy wakes, enables or starts before it is removed.
        var holder = new GameObject("PeakXR_MirrorHolder");
        holder.SetActive(false);
        var copy = Object.Instantiate(group.gameObject, holder.transform, false);
        copy.GetComponentsInChildren(true, scripts);
        // Last first: a script that requires another on its object comes after it.
        for (int i = scripts.Count - 1; i >= 0; i--)
            if (scripts[i] is not (Graphic or Mask or RectMask2D or Canvas or CanvasGroup))
                Object.DestroyImmediate(scripts[i]);
        scripts.Clear();
        copy.transform.SetParent(box, false);
        Object.Destroy(holder);
        copies.Add(copy);

        Pair(group, copy.transform, rects, from, to);
        Pair(group, copy.transform, drawn, fromGraphics, toGraphics);
        Pair(group, copy.transform, groups, fromGroups, toGroups);
    }

    private static void Pair<T>(
        Transform a,
        Transform b,
        List<T> scratch,
        List<T> into,
        List<T> intoCopy
    )
        where T : Component
    {
        a.GetComponentsInChildren(true, scratch);
        into.AddRange(scratch);
        b.GetComponentsInChildren(true, scratch);
        intoCopy.AddRange(scratch);
        scratch.Clear();
        // The copy has every graphic, rect and group of the original, in the same order; trim if that ever differs.
        int n = Mathf.Min(into.Count, intoCopy.Count);
        into.RemoveRange(n, into.Count - n);
        intoCopy.RemoveRange(n, intoCopy.Count - n);
    }

    /// <summary>The copy of <paramref name="part"/>, or null when it is not mirrored.</summary>
    public RectTransform CopyOf(Transform part)
    {
        for (int i = 0; i < from.Count; i++)
            if (from[i] == part)
                return to[i];
        return null;
    }

    public bool Empty => copies.Count == 0;

    /// <summary>Takes the originals' state. Per frame: writes only what changed, allocates nothing.</summary>
    public void Sync()
    {
        for (int i = 0; i < from.Count; i++)
        {
            var a = from[i];
            var b = to[i];
            if (a == null || b == null)
                continue;
            bool active = a.gameObject.activeSelf;
            if (b.gameObject.activeSelf != active)
                b.gameObject.SetActive(active);
            if (!active)
                continue;
            if (b.anchoredPosition != a.anchoredPosition)
                b.anchoredPosition = a.anchoredPosition;
            if (b.sizeDelta != a.sizeDelta)
                b.sizeDelta = a.sizeDelta;
            if (b.localScale != a.localScale)
                b.localScale = a.localScale;
            if (b.localRotation != a.localRotation)
                b.localRotation = a.localRotation;
        }
        for (int i = 0; i < fromGraphics.Count; i++)
        {
            var a = fromGraphics[i];
            var b = toGraphics[i];
            if (a == null || b == null)
                continue;
            if (b.enabled != a.enabled)
                b.enabled = a.enabled;
            if (b.color != a.color)
                b.color = a.color;
            float alpha = a.canvasRenderer.GetAlpha();
            if (b.canvasRenderer.GetAlpha() != alpha)
                b.canvasRenderer.SetAlpha(alpha);
            if (a is Image ai && b is Image bi)
            {
                if (bi.sprite != ai.sprite)
                    bi.sprite = ai.sprite;
                if (bi.fillAmount != ai.fillAmount)
                    bi.fillAmount = ai.fillAmount;
            }
            else if (
                a is TMPro.TMP_Text at
                && b is TMPro.TMP_Text bt
                && !ReferenceEquals(bt.text, at.text)
            )
                bt.text = at.text;
        }
        for (int i = 0; i < fromGroups.Count; i++)
        {
            var a = fromGroups[i];
            var b = toGroups[i];
            if (a != null && b != null && b.alpha != a.alpha)
                b.alpha = a.alpha;
        }
    }

    /// <summary>Destroys the copies (the mode changed or VR stopped).</summary>
    public void Clear()
    {
        foreach (var c in copies)
            if (c != null)
                Object.Destroy(c);
        copies.Clear();
        from.Clear();
        to.Clear();
        fromGraphics.Clear();
        toGraphics.Clear();
        fromGroups.Clear();
        toGroups.Clear();
    }
}
