using System.Collections.Generic;
using UnityEngine;

namespace PeakXR.Inventory;

/// <summary>
/// Hides the game's 2D hotbar (<c>GUIManager.items</c>, <c>backpack</c>, <c>temporaryItem</c>, A/GUIManager.cs:32-36)
/// on the VR HUD while <see cref="SlotGhosts"/> draws the hotbar on the body, so the inventory reads as slots you
/// reach for instead of numbered buttons. Each cell's row gets a <c>CanvasGroup</c> at alpha 0: the game keeps
/// driving the cells (<c>UpdateItems</c>, A/GUIManager.cs:1109), nothing is moved, and removing the groups
/// restores the hotbar exactly.
/// </summary>
internal static class HotbarHider
{
    private static readonly List<CanvasGroup> added = new();
    private static GUIManager hidden;

    /// <summary>The current HUD's hotbar is hidden.</summary>
    public static bool Active => hidden != null && hidden == GUIManager.instance;

    /// <summary>Hides or restores to match <paramref name="hide"/>; cheap to call every frame.</summary>
    public static void Sync(bool hide)
    {
        var gui = GUIManager.instance;
        if (hidden != null && (!hide || hidden != gui))
            Restore();
        if (hide && hidden == null && gui != null)
            Hide(gui);
    }

    public static void Restore()
    {
        foreach (var g in added)
        {
            if (g != null)
                Object.Destroy(g);
        }
        added.Clear();
        hidden = null;
    }

    private static void Hide(GUIManager gui)
    {
        hidden = gui;
        var bar = gui.bar != null ? gui.bar.transform : null;
        if (gui.items != null)
        {
            foreach (var cell in gui.items)
                HideCell(cell, bar);
        }
        HideCell(gui.backpack, bar);
        HideCell(gui.temporaryItem, bar);
    }

    /// <summary>Hides the cell's row (<c>Inventory/Layout</c>, which also holds the key hints) unless that row is
    /// a canvas or holds the stamina bar; then only the cell.</summary>
    private static void HideCell(InventoryItemUI cell, Transform bar)
    {
        if (cell == null)
            return;
        var target = cell.transform.parent;
        if (
            target == null
            || target.GetComponent<Canvas>() != null
            || (bar != null && bar.IsChildOf(target))
        )
            target = cell.transform;
        // A group the game put on the row is its to drive: hide the cell instead.
        if (target.TryGetComponent(out CanvasGroup existing) && !added.Contains(existing))
            target = cell.transform;
        // Already hidden through another cell of the row, or the cell has a group of its own.
        if (target.TryGetComponent(out CanvasGroup _))
            return;
        var group = target.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
        added.Add(group);
    }
}
