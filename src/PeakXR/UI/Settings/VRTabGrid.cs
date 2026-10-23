using System;
using System.Collections.Generic;
using PeakXR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Zorro.Settings;
using Zorro.UI;
using Object = UnityEngine.Object;

namespace PeakXR.UI.Settings;

/// <summary>
/// The VR tab laid out for a headset instead of the game's 16:9 page: the chosen category's rows in one column about
/// 30 degrees wide at the menu's distance, scrolled vertically (thumbstick or laser drag, through a plain
/// <see cref="ScrollRect"/>), and the category rail as its own small panel beside it, turned toward the eye. Both are
/// scaled so their smallest text meets <see cref="ReadableText.MinDegrees"/>, and each gets its own plate
/// (<see cref="World.PanelPlate"/>). Rows are the game's own cells and chips clones of its tab button, so a game
/// update restyles both. The game's own tabs keep the game's layout. Anything missing falls back to the single paged
/// column. Built once per fill; nothing here runs per frame.
/// </summary>
internal static class VRTabGrid
{
    /// <summary>A narrowed row gives its control at most this share of the column.</summary>
    private const float ControlShare = 0.5f;
    private const float LabelGap = 16f;
    private const float RailGap = 24f;
    private const float ChipPad = 20f;
    private const float ChipGap = 6f;
    private const float ChipFallbackHeight = 44f;

    private static readonly List<TMP_Text> texts = new();
    private static readonly List<RectTransform> spawned = new();
    private static string lastFallback;

    /// <summary>The last VR fill used this layout (false: the single-column fallback); for the self-test.</summary>
    public static bool Shown { get; private set; }

    /// <summary>Pages of the category on show: always one, the column scrolls. Kept for the self-test.</summary>
    public static int Pages { get; private set; }

    /// <summary>Removes the rail and column of the last fill; every tab switch calls it before filling.</summary>
    public static void Clear(SharedSettingsMenu menu)
    {
        var parent = menu != null ? menu.m_settingsContentParent : null;
        if (parent != null && parent.TryGetComponent<VRTabLayout>(out var layout))
            layout.Clear();
    }

    /// <summary>Rows a page of the single-column fallback can hold under its header and page buttons.</summary>
    public static int FallbackRows(SharedSettingsMenu menu)
    {
        var content = menu.m_settingsContentParent as RectTransform;
        if (!Measure(menu, content, out float cellHeight, out float spacing))
            return VRTab.PageRows;
        return Math.Max(4, SettingsGrid.Fit(content.rect.height, cellHeight, spacing) - 3);
    }

    /// <summary>Fills the VR tab as rail and column; false (nothing spawned) when the window lacks what it needs.</summary>
    public static bool TryShow(SharedSettingsMenu menu)
    {
        var content = menu.m_settingsContentParent as RectTransform;
        var chip = ChipSource(UISettingsFeature.TabsRef(menu));
        if (chip == null || !Measure(menu, content, out float cellHeight, out float spacing))
            return Fallback("no content parent, tab button or row prefab");
        var area = content.rect;
        float mpu = Mathf.Abs(content.lossyScale.x);
        if (area.height < 2f * cellHeight || mpu < 1e-6f)
            return Fallback($"window {area.width:0}x{area.height:0} for {cellHeight:0} px rows");
        // Angles are judged where the menu stands now; a wall that pulls it closer shrinks it to the same angles.
        var head = VR.VRRig.Instance != null ? VR.VRRig.Instance.Head : null;
        float distance =
            head != null
                ? Vector3.Distance(head.position, content.TransformPoint(area.center))
                : World.UIWorldFeature.Settings.MenuDistance.Value;

        var groups = VRTab.BuildGroups();
        int selected = groups.FindIndex(g =>
            string.Equals(g.Key, VRTab.Group, StringComparison.Ordinal)
        );
        if (selected < 0)
            selected = 0;
        var group = groups[selected];
        VRTab.Group = group.Key;
        VRTab.Page = 0;

        var layout = content.GetComponent<VRTabLayout>();
        if (layout == null)
            layout = content.gameObject.AddComponent<VRTabLayout>();
        var cells = UISettingsFeature.CellsRef(menu);
        int before = cells.Count;
        float columnScale,
            railScale,
            rowWidth;
        try
        {
            // The column: its plate, then a masked viewport that scrolls the rows.
            var column = NewRoot("PeakXR_VRColumn", content);
            layout.Grid = column.gameObject;
            var viewport = NewRect("PeakXR_VRViewport", column);
            Stretch(viewport);
            // Invisible, but it takes the laser between rows, so a drag or the stick scrolls there too.
            viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            viewport.gameObject.AddComponent<World.PanelPart>();
            viewport.gameObject.AddComponent<RectMask2D>();
            var rows = NewRect("PeakXR_VRRows", viewport);
            rows.anchorMin = new Vector2(0f, 1f);
            rows.anchorMax = Vector2.one;
            rows.pivot = new Vector2(0.5f, 1f);

            bool first = true;
            float minFont = float.MaxValue;
            spawned.Clear();
            foreach (var row in group.Rows)
            {
                var cell = ShowSettingsPatch.Spawn(menu, row, ref first);
                if (cell == null)
                    continue;
                var rt = (RectTransform)cell.transform;
                rt.SetParent(rows, false);
                spawned.Add(rt);
                cell.GetComponentsInChildren(false, texts);
                foreach (var t in texts)
                    if (t.fontSize > 1f)
                        minFont = Mathf.Min(minFont, t.fontSize);
                texts.Clear();
            }
            if (minFont == float.MaxValue)
                minFont = cellHeight * 0.5f;

            columnScale = VRTabColumn.TextScale(minFont, distance, mpu);
            rowWidth = VRTabColumn.Width(VRTabColumn.ColumnDegrees, distance, mpu) / columnScale;
            float pitch = cellHeight + spacing;
            for (int i = 0; i < spawned.Count; i++)
            {
                var rt = spawned[i];
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.sizeDelta = new Vector2(rowWidth, cellHeight);
                rt.anchoredPosition = new Vector2(0f, -i * pitch);
                Narrow(rt.GetComponent<SettingsUICell>(), rowWidth);
            }
            float rowsHeight = Mathf.Max(0f, spawned.Count * pitch - spacing);
            rows.sizeDelta = new Vector2(0f, rowsHeight);
            rows.anchoredPosition = Vector2.zero;
            spawned.Clear();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = rows;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            // The rail: as wide as its longest chip, hinged on the edge next to the column.
            railScale = VRTabColumn.TextScale(chip.text.fontSize, distance, mpu);
            float chipWidth = 0f;
            foreach (var g in groups)
                chipWidth = Mathf.Max(
                    chipWidth,
                    chip.text.GetPreferredValues(g.Title.ToUpperInvariant()).x + 2f * ChipPad
                );
            var chipRect = ((RectTransform)chip.transform).rect;
            float chipHeight = chipRect.height > 1f ? chipRect.height : ChipFallbackHeight;
            float railWidth = chipWidth * railScale;
            float left = area.center.x - (railWidth + RailGap + rowWidth * columnScale) * 0.5f;
            // As tall as the window allows, or as the rows when they are fewer: the plate hugs what is there.
            float viewHeight = Mathf.Min(area.height / columnScale, rowsHeight);
            Place(column, area, left + railWidth + RailGap, area.yMax, rowWidth, viewHeight);
            column.localScale = new Vector3(columnScale, columnScale, columnScale);
            World.PanelPlate.Add(column, columnScale);

            var rail = NewRoot("PeakXR_VRRail", content);
            layout.Rail = rail.gameObject;
            rail.anchorMin = rail.anchorMax = new Vector2(0f, 1f);
            rail.pivot = Vector2.one;
            rail.sizeDelta = new Vector2(
                chipWidth,
                groups.Count * (chipHeight + ChipGap) - ChipGap
            );
            rail.anchoredPosition = new Vector2(left + railWidth - area.xMin, 0f);
            rail.localScale = new Vector3(railScale, railScale, railScale);
            rail.localRotation = Quaternion.Euler(0f, VRTabColumn.RailYaw(left: true), 0f);
            World.PanelPlate.Add(rail, railScale);
            for (int i = 0; i < groups.Count; i++)
            {
                var key = groups[i].Key;
                var rt = (RectTransform)
                    Chip(
                        chip,
                        rail,
                        key,
                        groups[i].Title,
                        i == selected,
                        () => Select(menu, key)
                    ).transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.sizeDelta = new Vector2(chipWidth, chipHeight);
                rt.anchoredPosition = new Vector2(0f, -i * (chipHeight + ChipGap));
            }
        }
        catch (Exception e)
        {
            spawned.Clear();
            for (int i = cells.Count - 1; i >= before; i--)
            {
                if (cells[i] != null)
                    Object.Destroy(cells[i].gameObject);
                cells.RemoveAt(i);
            }
            layout.Clear();
            return Fallback($"layout failed: {e.GetBaseException().Message}");
        }

        lastFallback = null;
        Shown = true;
        Pages = 1;
        Plugin.Log.LogInfo(
            $"[ui.settings] VR tab: '{group.Title}', {group.Rows.Count} rows in a {VRTabColumn.ColumnDegrees:0} deg "
                + $"column ({rowWidth:0} px at x{columnScale:0.00}), rail of {groups.Count} at x{railScale:0.00} "
                + $"turned {VRTabColumn.RailDegrees:0} deg, {distance:0.00} m away"
        );
        return true;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    /// <summary>Row height from the game's row prefab, and the gap its list leaves between rows.</summary>
    private static bool Measure(
        SharedSettingsMenu menu,
        RectTransform content,
        out float cellHeight,
        out float spacing
    )
    {
        cellHeight = spacing = 0f;
        var prefab =
            menu.m_settingsCellPrefab != null
                ? menu.m_settingsCellPrefab.transform as RectTransform
                : null;
        if (content == null || prefab == null)
            return false;
        cellHeight = prefab.rect.height > 1f ? prefab.rect.height : prefab.sizeDelta.y;
        var list = content.GetComponent<HorizontalOrVerticalLayoutGroup>();
        spacing = list != null ? Mathf.Max(0f, list.spacing) : 0f;
        return cellHeight >= 1f;
    }

    private static bool Fallback(string reason)
    {
        Shown = false;
        if (!string.Equals(reason, lastFallback, StringComparison.Ordinal))
            Plugin.Log.LogWarning($"[ui.settings] VR tab as one paged column: {reason}");
        lastFallback = reason;
        return false;
    }

    private static SettingsTABSButton ChipSource(SettingsTABS tabs)
    {
        if (tabs == null)
            return null;
        foreach (var b in tabs.buttons)
            if (b != null && b.text != null)
                return b;
        return null;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    /// <summary>A layout-free child covering the content exactly, so its space is the content's.</summary>
    private static RectTransform NewRoot(string name, RectTransform content)
    {
        var rt = NewRect(name, content);
        rt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = content.pivot;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    /// <summary>Puts <paramref name="rt"/> at a top-left corner given in the content's space.</summary>
    private static RectTransform Place(
        RectTransform rt,
        Rect area,
        float left,
        float top,
        float width,
        float height
    )
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(width, height);
        rt.anchoredPosition = new Vector2(left - area.xMin, top - area.yMax);
        return rt;
    }

    /// <summary>
    /// A row narrower than the game drew it for: the control keeps at most half the column, the label takes the rest
    /// and wraps. Only rects anchored to one edge are touched.
    /// </summary>
    private static void Narrow(SettingsUICell cell, float width)
    {
        var input = cell.m_settingsContentParent as RectTransform;
        var text = cell.m_text;
        var label = text != null ? text.rectTransform : null;
        if (input == null || label == null || !OneEdge(input) || !OneEdge(label))
            return;
        float control = Mathf.Min(input.sizeDelta.x, width * ControlShare);
        float room = width - control - LabelGap;
        if (control >= input.sizeDelta.x && label.sizeDelta.x <= room)
            return;
        SetWidth(input, control, keepLeft: false);
        SetWidth(label, room, keepLeft: true);
        text.textWrappingMode = TextWrappingModes.Normal;
        // Never shrinks: the column's scale holds the text at the size floor; a label too long ends in an ellipsis.
        text.overflowMode = TextOverflowModes.Ellipsis;
    }

    private static bool OneEdge(RectTransform rt) =>
        Mathf.Approximately(rt.anchorMin.x, rt.anchorMax.x);

    private static void SetWidth(RectTransform rt, float width, bool keepLeft)
    {
        float old = rt.sizeDelta.x;
        var pos = rt.anchoredPosition;
        // The kept edge stays put whatever the pivot.
        pos.x += keepLeft ? rt.pivot.x * (width - old) : -(1f - rt.pivot.x) * (width - old);
        rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);
        rt.anchoredPosition = pos;
    }

    /// <summary>
    /// A chip: a clone of the game's tab button without its tab behaviour (it would look for a tab bar to select
    /// in), its selected look set once, clicking <paramref name="onClick"/>.
    /// </summary>
    private static Button Chip(
        SettingsTABSButton source,
        Transform parent,
        string key,
        string title,
        bool selected,
        UnityAction onClick
    )
    {
        var clone = Object.Instantiate(source, parent, false);
        var go = clone.gameObject;
        var selectedGraphic = clone.SelectedGraphic;
        var text = clone.text;
        foreach (var tab in go.GetComponents<TAB_Button>())
            Object.DestroyImmediate(tab);
        go.name = "PeakXR_Chip_" + key;
        if (selectedGraphic != null)
            selectedGraphic.SetActive(selected);
        if (text != null)
        {
            var localized = text.GetComponent<LocalizedText>();
            var id = Loc.Set(key, title.ToUpperInvariant());
            if (localized != null)
                localized.SetIndex(id);
            else
                text.text = title.ToUpperInvariant();
            text.color = selected ? Color.black : Color.white;
            // Kept at the size the rail's scale was measured for.
            text.enableAutoSizing = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
        }
        if (!go.TryGetComponent<Button>(out var button))
            button = go.AddComponent<Button>();
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(onClick);
        go.SetActive(true);
        return button;
    }

    /// <summary>Shows <paramref name="page"/> of rail entry <paramref name="key"/> (a chip click, or the self-test).</summary>
    internal static void Select(SharedSettingsMenu menu, string key, int page = 0)
    {
        VRTab.Group = key;
        VRTab.Page = page;
        Refill(menu);
    }

    private static void Refill(SharedSettingsMenu menu)
    {
        if (menu == null || !menu.isActiveAndEnabled)
            return;
        VRTab.Refilling = true;
        menu.ShowSettings(VRTab.Category);
    }

    /// <summary>Middle of the row's right edge and the direction away from the column there: previews and guide
    /// demos stand beside the row they belong to, clear of the rail on the column's left.</summary>
    internal static void RowOuterEdge(
        RectTransform row,
        Vector3[] scratch,
        out Vector3 edge,
        out Vector3 outward
    )
    {
        row.GetWorldCorners(scratch);
        outward = (scratch[3] - scratch[0]).normalized;
        edge = (scratch[2] + scratch[3]) * 0.5f;
    }
}

/// <summary>The VR layout's objects on a settings window, so the next fill removes them.</summary>
internal sealed class VRTabLayout : MonoBehaviour
{
    /// <summary>The row column (its plate, the scrolled viewport and rows).</summary>
    public GameObject Grid;

    /// <summary>The category rail, a panel of its own beside the column.</summary>
    public GameObject Rail;

    public void Clear()
    {
        if (Grid != null)
            Destroy(Grid);
        if (Rail != null)
            Destroy(Rail);
        Grid = Rail = null;
    }
}
