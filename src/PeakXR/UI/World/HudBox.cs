using System.Collections.Generic;
using UnityEngine;

namespace PeakXR.UI.World;

/// <summary>
/// A container under a converted canvas that holds HUD parts lifted out of their panel (reticle, wrist bar,
/// radial wheels). The parts stay inside the game's canvas, so the game keeps driving them and they die with
/// the scene; only the container is posed. It is the size of the canvas, so the parts keep their layout.
/// Everything moved can be put back exactly where it was.
/// </summary>
internal sealed class HudBox
{
    private struct Moved
    {
        public Transform Item;
        public Transform Parent;
        public int Sibling;
    }

    private readonly string name;
    private readonly List<Moved> moved = new();
    private RectTransform root;

    public HudBox(string name) => this.name = name;

    public RectTransform Rect { get; private set; }

    /// <summary>Creates the container under <paramref name="canvasRoot"/> (again if the canvas changed) and shows it.</summary>
    public RectTransform Ensure(RectTransform canvasRoot)
    {
        if (Rect == null || root != canvasRoot)
        {
            Forget();
            root = canvasRoot;
            Rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            Rect.SetParent(canvasRoot, false);
            Rect.anchorMin = Rect.anchorMax = Rect.pivot = new Vector2(0.5f, 0.5f);
            Rect.sizeDelta = canvasRoot.sizeDelta;
        }
        if (!Rect.gameObject.activeSelf)
            Rect.gameObject.SetActive(true);
        return Rect;
    }

    public void SetActive(bool active)
    {
        if (Rect != null && Rect.gameObject.activeSelf != active)
            Rect.gameObject.SetActive(active);
    }

    /// <summary>Re-parents <paramref name="item"/> into the container, remembering where it came from.</summary>
    public void Move(Transform item)
    {
        if (item == null || Rect == null || item.parent == Rect)
            return;
        moved.Add(
            new Moved
            {
                Item = item,
                Parent = item.parent,
                Sibling = item.GetSiblingIndex(),
            }
        );
        item.SetParent(Rect, false);
    }

    /// <summary>Puts every moved part back and hides the container.</summary>
    public void Restore()
    {
        for (int i = moved.Count - 1; i >= 0; i--)
        {
            var m = moved[i];
            if (m.Item != null && m.Parent != null)
            {
                m.Item.SetParent(m.Parent, false);
                m.Item.SetSiblingIndex(m.Sibling);
            }
        }
        moved.Clear();
        SetActive(false);
    }

    /// <summary>The canvas died with its scene, and the container and moved parts with it.</summary>
    public void Forget()
    {
        moved.Clear();
        Rect = null;
        root = null;
    }

    /// <summary>Scales the container so one canvas pixel is <paramref name="metersPerPixel"/> in the world.</summary>
    public void SetScale(float metersPerPixel)
    {
        if (Rect == null)
            return;
        var parent = root != null ? root.lossyScale.x : 1f;
        var s = Mathf.Abs(parent) > 1e-6f ? metersPerPixel / parent : metersPerPixel;
        Rect.localScale = new Vector3(s, s, s);
    }

    /// <summary>Places the container so the point <paramref name="localCenter"/> (container pixels) lands on
    /// <paramref name="anchor"/>.</summary>
    public void Pose(Vector3 anchor, Quaternion rotation, Vector2 localCenter, float metersPerPixel)
    {
        if (Rect == null)
            return;
        SetScale(metersPerPixel);
        Rect.SetPositionAndRotation(anchor - rotation * (localCenter * metersPerPixel), rotation);
    }
}
