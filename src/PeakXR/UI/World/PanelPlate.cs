using System.Collections.Generic;
using PeakXR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PeakXR.UI.World;

/// <summary>
/// A panel inside a menu canvas with a plate of its own (the VR tab's row column and category rail): the same dark
/// rounded plate as <see cref="BackplateView"/>, stretched over the panel plus the margin, so each panel is backed
/// separately instead of one plate around the whole window. Its graphics stay off the canvas plate. Follows
/// <c>UI.MenuBackplate</c> and its opacity when they change; with <c>UI.MenuBackplateAdaptive</c>
/// <see cref="BackdropProbe"/> fits each one's opacity to what is behind it, as for the canvas plates.
/// </summary>
internal sealed class PanelPlate : MonoBehaviour
{
    private static readonly List<PanelPlate> live = new();
    private Image plate;

    /// <summary>Opacity <see cref="BackdropProbe"/> measured behind this plate, and the one drawn now on its way
    /// there; negative until measured.</summary>
    public float Target = -1f;
    public float Alpha = -1f;

    /// <summary>Every enabled panel plate, for <see cref="BackdropProbe"/>. Index it; do not hold on to it.</summary>
    internal static List<PanelPlate> Live => live;

    /// <summary>The plate graphic.</summary>
    public Image Image => plate;

    /// <summary>The plate is drawn now (self-test).</summary>
    public bool Shown => plate != null && plate.isActiveAndEnabled;

    /// <summary>Backs <paramref name="panel"/>, drawn at <paramref name="scale"/>, with a plate.</summary>
    public static PanelPlate Add(RectTransform panel, float scale)
    {
        var self = panel.gameObject.AddComponent<PanelPlate>();
        var go = new GameObject(
            "PeakXR_PanelPlate",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(LayoutElement),
            typeof(PanelPart)
        );
        go.layer = panel.gameObject.layer;
        go.GetComponent<LayoutElement>().ignoreLayout = true;
        var rt = (RectTransform)go.transform;
        rt.SetParent(panel, false);
        rt.SetAsFirstSibling();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        // Margin and corner radius in the window's units, whatever the panel's scale.
        float m = MenuBackplate.MarginPixels / Mathf.Max(scale, 1e-3f);
        rt.offsetMin = new Vector2(-m, -m);
        rt.offsetMax = new Vector2(m, m);
        var image = go.GetComponent<Image>();
        image.sprite = BackplateView.PlateSprite();
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = scale;
        image.raycastTarget = false;
        self.plate = image;
        self.Refresh();
        return self;
    }

    public static void RefreshAll()
    {
        foreach (var p in live)
            p.Refresh();
    }

    private void Refresh()
    {
        if (plate == null)
            return;
        var cfg = UIWorldFeature.Settings;
        bool on = cfg.MenuBackplate.Value;
        if (plate.gameObject.activeSelf != on)
            plate.gameObject.SetActive(on);
        BackplateView.SetAlpha(plate, BackplateView.Alpha(Alpha, cfg));
    }

    private void OnEnable() => live.Add(this);

    private void OnDisable()
    {
        live.Remove(this);
        // Shown again it starts at the full setting until measured.
        Target = Alpha = -1f;
    }
}

/// <summary>Marks a graphic of ours that <see cref="BackdropStrip"/> must leave alone: a panel plate, or a scroll
/// viewport's invisible hit area.</summary>
internal sealed class PanelPart : MonoBehaviour { }
