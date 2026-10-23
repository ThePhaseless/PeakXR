using System.Collections.Generic;
using PeakXR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PeakXR.UI.World;

/// <summary>
/// A panel inside a menu canvas with a plate of its own (the VR tab's row column and category rail): the same dark
/// rounded plate as <see cref="BackplateView"/>, stretched over the panel plus the margin, so each panel is backed
/// separately instead of one plate around the whole window. Its graphics stay off the canvas plate. Follows
/// <c>UI.MenuBackplate</c> and its opacity when they change; nothing runs per frame.
/// </summary>
internal sealed class PanelPlate : MonoBehaviour
{
    private static readonly List<PanelPlate> live = new();
    private Image plate;

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
        var c = MenuBackplate.Color;
        plate.color = new Color(c.X, c.Y, c.Z, cfg.MenuBackplateOpacity.Value);
    }

    private void OnEnable() => live.Add(this);

    private void OnDisable() => live.Remove(this);
}

/// <summary>Marks a graphic of ours that <see cref="BackdropStrip"/> must leave alone: a panel plate, or a scroll
/// viewport's invisible hit area.</summary>
internal sealed class PanelPart : MonoBehaviour { }
