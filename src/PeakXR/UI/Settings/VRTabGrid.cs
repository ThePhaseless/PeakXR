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
/// The VR tab laid out for a headset. A rail of category chips runs down the free side of the settings window (one
/// chip per config group, plus the VR Guide and the compatibility report); the chosen category fills a grid of the
/// game's own row cells, two columns where the window is wide enough, a page at a time with Previous and Next chips
/// under it. Every page fits the window: on a world panel nothing cuts off rows that would hang below it.
/// Chips are clones of the game's own tab button, rows its own cells, so a game update restyles both. Anything
/// missing (no tab button, no row prefab, a window too small) falls back to the single paged column.
/// </summary>
internal static class VRTabGrid
{
    private const float ColumnGap = 48f;
    private const float MinColumn = 560f;
    private const int MaxColumns = 2;

    /// <summary>A narrowed row gives its control at most this share of the column.</summary>
    private const float ControlShare = 0.5f;
    private const float LabelGap = 16f;
    private const float RailGap = 24f;
    private const float RailMaxWidth = 320f;
    private const float RailMinWidth = 200f;
    private const float ChipColumnGap = 8f;
    private const float ChipMaxPitch = 52f;
    private const float ChipMinPitch = 34f;
    private const float ChipFill = 0.84f;
    private const float PagerHeight = 56f;
    private const float PagerChipWidth = 220f;
    private const float PagerLabelWidth = 260f;

    private static readonly Vector3[] corners = new Vector3[4];
    private static readonly List<Selectable> selectables = new();
    private static readonly List<float> lows = new();
    private static readonly List<float> highs = new();
    private static readonly List<bool> headers = new();
    private static string lastFallback;

    /// <summary>The last VR fill used this layout (false: the single-column fallback); for the self-test.</summary>
    public static bool Shown { get; private set; }

    /// <summary>Pages of the category on show after the last fill; for the self-test.</summary>
    public static int Pages { get; private set; }

    /// <summary>Removes the rail and grid of the last fill; every tab switch calls it before filling.</summary>
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

    /// <summary>Fills the VR tab as rail and grid; false (nothing spawned) when the window lacks what it needs.</summary>
    public static bool TryShow(SharedSettingsMenu menu)
    {
        var content = menu.m_settingsContentParent as RectTransform;
        var window = menu.transform as RectTransform;
        var tabs = UISettingsFeature.TabsRef(menu);
        var chip = ChipSource(tabs);
        if (
            window == null
            || chip == null
            || !Measure(menu, content, out float cellHeight, out float spacing)
        )
            return Fallback("no content parent, tab button or row prefab");
        var area = content.rect;
        if (area.width < MinColumn || area.height < 2f * cellHeight)
            return Fallback($"window {area.width:0}x{area.height:0} for {cellHeight:0} px rows");

        // The rail: left of the rows, in the window's free margin between its own buttons (Back above, the
        // blocklist toggle below). Too narrow a margin puts it inside, ahead of the rows.
        window.GetWorldCorners(corners);
        float windowLeft = content.InverseTransformPoint(corners[0]).x;
        float railRight = area.xMin - RailGap;
        float railLeft = Mathf.Max(windowLeft + RailGap, railRight - RailMaxWidth);
        bool inside = railRight - railLeft < RailMinWidth;
        if (inside)
        {
            railLeft = area.xMin;
            railRight = area.xMin + RailMinWidth;
        }
        Obstacles(menu, content, tabs, railLeft, railRight);
        SettingsGrid.FreeSpan(
            area.yMin,
            area.yMax,
            lows,
            highs,
            out float railBottom,
            out float railTop
        );
        if (railTop - railBottom < 2f * ChipMinPitch)
            return Fallback($"no room for the category rail ({railTop - railBottom:0} px)");

        var groups = VRTab.BuildGroups();
        int selected = groups.FindIndex(g =>
            string.Equals(g.Key, VRTab.Group, StringComparison.Ordinal)
        );
        if (selected < 0)
            selected = 0;
        var group = groups[selected];
        VRTab.Group = group.Key;

        float gridLeft = inside ? railRight + RailGap : area.xMin;
        float gridWidth = area.xMax - gridLeft;
        int columns = SettingsGrid.Columns(gridWidth, MinColumn, ColumnGap, MaxColumns);
        float columnWidth = SettingsGrid.ColumnWidth(gridWidth, columns, ColumnGap);
        headers.Clear();
        foreach (var row in group.Rows)
            headers.Add(row is InfoRow { IsHeader: true });
        int perColumn = SettingsGrid.Fit(area.height, cellHeight, spacing);
        var pages = SettingsGrid.Paginate(headers, perColumn, columns);
        if (pages.Count > 1)
        {
            perColumn = SettingsGrid.Fit(area.height - PagerHeight - spacing, cellHeight, spacing);
            pages = SettingsGrid.Paginate(headers, perColumn, columns);
        }
        int page = Mathf.Clamp(VRTab.Page, 0, pages.Count - 1);
        VRTab.Page = page;

        var layout = content.GetComponent<VRTabLayout>();
        if (layout == null)
            layout = content.gameObject.AddComponent<VRTabLayout>();
        var cells = UISettingsFeature.CellsRef(menu);
        int before = cells.Count;
        try
        {
            var grid = NewRoot("PeakXR_VRGrid", content);
            layout.Grid = grid.gameObject;
            var rail = NewRoot("PeakXR_VRRail", content);
            layout.Rail = rail.gameObject;

            // Rail chips, column by column when the rail needs more than one.
            SettingsGrid.Rail(
                railTop - railBottom,
                groups.Count,
                ChipMaxPitch,
                ChipMinPitch,
                out int railColumns,
                out float pitch
            );
            int perRail = (groups.Count + railColumns - 1) / railColumns;
            float chipWidth =
                (railRight - railLeft - ChipColumnGap * (railColumns - 1)) / railColumns;
            var railEdge = Place(
                NewRect("PeakXR_RailEdge", rail),
                area,
                railLeft,
                railTop,
                railRight - railLeft,
                railTop - railBottom
            );
            for (int i = 0; i < groups.Count; i++)
            {
                var key = groups[i].Key;
                var button = Chip(
                    chip,
                    rail,
                    key,
                    groups[i].Title,
                    i == selected,
                    () => Select(menu, key)
                );
                Place(
                    (RectTransform)button.transform,
                    area,
                    railLeft + (i / perRail) * (chipWidth + ChipColumnGap),
                    railTop - (i % perRail) * pitch,
                    chipWidth,
                    pitch * ChipFill
                );
            }

            // The page's rows, the game's own cells, in their column slots.
            var shown = pages[page];
            bool first = true;
            for (int c = 0; c < shown.Columns.Count; c++)
            {
                bool opensLeft = SettingsGrid.OpensLeft(shown, c);
                var column = shown.Columns[c];
                for (int r = 0; r < column.Count; r++)
                {
                    var cell = ShowSettingsPatch.Spawn(menu, group.Rows[column[r]], ref first);
                    if (cell == null)
                        continue;
                    var rt = (RectTransform)cell.transform;
                    rt.SetParent(grid, false);
                    Place(
                        rt,
                        area,
                        gridLeft + c * (columnWidth + ColumnGap),
                        area.yMax - r * (cellHeight + spacing),
                        columnWidth,
                        cellHeight
                    );
                    Narrow(cell, columnWidth);
                    var slot = cell.gameObject.AddComponent<VRTabSlot>();
                    slot.OpensLeft = opensLeft;
                    slot.LeftEdge = railEdge;
                }
            }

            if (pages.Count > 1)
                Pager(menu, chip, grid, area, gridLeft + gridWidth * 0.5f, page, pages.Count);
        }
        catch (Exception e)
        {
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
        Pages = pages.Count;
        Plugin.Log.LogInfo(
            $"[ui.settings] VR tab: '{group.Title}' page {page + 1}/{pages.Count}, {columns} columns of {perColumn} rows "
                + $"({columnWidth:0} px), rail of {groups.Count} {(inside ? "inside" : "beside")} the rows"
        );
        return true;
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

    /// <summary>The window's own controls outside the rows and tabs that share the rail's width, as vertical spans in
    /// the content's space, padded by the rail gap.</summary>
    private static void Obstacles(
        SharedSettingsMenu menu,
        RectTransform content,
        SettingsTABS tabs,
        float left,
        float right
    )
    {
        lows.Clear();
        highs.Clear();
        menu.GetComponentsInChildren(false, selectables);
        foreach (var s in selectables)
        {
            var t = s.transform;
            if (t.IsChildOf(content) || (tabs != null && t.IsChildOf(tabs.transform)))
                continue;
            ((RectTransform)t).GetWorldCorners(corners);
            var a = content.InverseTransformPoint(corners[0]);
            var b = content.InverseTransformPoint(corners[2]);
            if (Mathf.Max(a.x, b.x) < left || Mathf.Min(a.x, b.x) > right)
                continue;
            lows.Add(Mathf.Min(a.y, b.y) - RailGap);
            highs.Add(Mathf.Max(a.y, b.y) + RailGap);
        }
        selectables.Clear();
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
    /// and wraps (shrinking a little before it would spill). Only rects anchored to one edge are touched.
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
        text.overflowMode = TextOverflowModes.Ellipsis;
        if (!text.enableAutoSizing)
        {
            text.fontSizeMax = text.fontSize;
            text.fontSizeMin = text.fontSize * 0.75f;
            text.enableAutoSizing = true;
        }
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
        }
        if (!go.TryGetComponent<Button>(out var button))
            button = go.AddComponent<Button>();
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(onClick);
        go.SetActive(true);
        return button;
    }

    /// <summary>Previous and Next chips under the rows with "Page x of y" between them; a chip shows only where there
    /// is a page to go to.</summary>
    private static void Pager(
        SharedSettingsMenu menu,
        SettingsTABSButton source,
        RectTransform grid,
        Rect area,
        float centre,
        int page,
        int pages
    )
    {
        float top = area.yMin + PagerHeight * ChipFill;
        float height = PagerHeight * ChipFill;
        if (page > 0)
            Place(
                (RectTransform)
                    Chip(
                        source,
                        grid,
                        "PEAKXR_PAGE_PREVIOUS",
                        "Previous",
                        false,
                        () => Turn(menu, -1)
                    ).transform,
                area,
                centre - PagerLabelWidth * 0.5f - PagerChipWidth,
                top,
                PagerChipWidth,
                height
            );
        if (page < pages - 1)
            Place(
                (RectTransform)
                    Chip(
                        source,
                        grid,
                        "PEAKXR_PAGE_NEXT",
                        "Next",
                        false,
                        () => Turn(menu, 1)
                    ).transform,
                area,
                centre + PagerLabelWidth * 0.5f,
                top,
                PagerChipWidth,
                height
            );
        var label = Place(
            NewRect("PeakXR_PageOf", grid),
            area,
            centre - PagerLabelWidth * 0.5f,
            top,
            PagerLabelWidth,
            height
        );
        var text = label.gameObject.AddComponent<TextMeshProUGUI>();
        if (source.text != null)
        {
            text.font = source.text.font;
            text.fontSize = source.text.fontSize;
        }
        text.text = $"PAGE {page + 1} OF {pages}";
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
    }

    /// <summary>Shows <paramref name="page"/> of rail entry <paramref name="key"/> (a chip click, or the self-test).</summary>
    internal static void Select(SharedSettingsMenu menu, string key, int page = 0)
    {
        VRTab.Group = key;
        VRTab.Page = page;
        Refill(menu);
    }

    private static void Turn(SharedSettingsMenu menu, int delta)
    {
        VRTab.Page += delta;
        Refill(menu);
    }

    private static void Refill(SharedSettingsMenu menu)
    {
        if (menu == null || !menu.isActiveAndEnabled)
            return;
        VRTab.Refilling = true;
        menu.ShowSettings(VRTab.Category);
    }

    /// <summary>
    /// Middle of the row's outer edge and the direction away from the panel there: the right edge, or for a row
    /// whose right side is taken by the next column, the left edge of the rail at the row's height. Previews and guide
    /// demos stand there, beside the row they belong to and clear of the other rows.
    /// </summary>
    internal static void RowOuterEdge(
        RectTransform row,
        Vector3[] scratch,
        out Vector3 edge,
        out Vector3 outward
    )
    {
        row.GetWorldCorners(scratch);
        var right = (scratch[3] - scratch[0]).normalized;
        if (row.TryGetComponent<VRTabSlot>(out var slot) && slot.OpensLeft && slot.LeftEdge != null)
        {
            var mid = (scratch[0] + scratch[1]) * 0.5f;
            slot.LeftEdge.GetWorldCorners(scratch);
            edge = mid - right * Vector3.Dot(mid - scratch[0], right);
            outward = -right;
            return;
        }
        edge = (scratch[2] + scratch[3]) * 0.5f;
        outward = right;
    }
}

/// <summary>The VR layout's objects on a settings window, so the next fill removes them.</summary>
internal sealed class VRTabLayout : MonoBehaviour
{
    public GameObject Grid;
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

/// <summary>Which side of a grid row its preview and demo open on (<see cref="VRTabGrid.RowOuterEdge"/>).</summary>
internal sealed class VRTabSlot : MonoBehaviour
{
    public bool OpensLeft;

    /// <summary>The rail's outline: a left-opening preview stands past its left edge.</summary>
    public RectTransform LeftEdge;
}
