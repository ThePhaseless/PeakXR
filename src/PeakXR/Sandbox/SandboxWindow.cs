using System;
using System.Collections.Generic;
using PeakXR.Core;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>
/// Flat IMGUI window for desktop testing before the VR panel exists. It only calls
/// <see cref="SandboxActions"/>. Lists are virtualised (only visible rows are drawn, from precomputed
/// labels) and filtered only when the query or data changes, so a frame allocates next to nothing.
/// </summary>
internal sealed class SandboxWindow
{
    const int WindowId = 0x50585201; // "PXR" + 1, arbitrary but unlikely to clash
    const float RowHeight = 22f;
    const float Pad = 8f;

    static readonly string[] Tabs = { "Items", "Entities", "Player" };
    static readonly string[] SegmentNames = BuildSegmentNames();
    static readonly string[] TimeNames = { "00:00", "06:00", "09:00", "12:00", "18:00", "21:00" };

    /// <summary>True while the window is visible. Read by the input and cursor patches.</summary>
    internal static bool IsOpen { get; set; }

    readonly GUI.WindowFunction windowFunction;
    readonly List<int> filtered = new();
    Rect rect = new(24f, 24f, 480f, 640f);
    int tab;
    string query = "";
    string[] terms = Array.Empty<string>();
    Vector2 itemsScroll;
    Vector2 mobsScroll;
    bool giveToHand;

    // What the filtered list was built from; any change triggers a rebuild.
    int filterTab = -1;
    int filterVersion = -1;
    string filterQuery;

    public SandboxWindow()
    {
        windowFunction = DrawContents;
    }

    public void Draw()
    {
        rect = GUI.Window(WindowId, rect, windowFunction, "PeakXR Sandbox");
    }

    void DrawContents(int id)
    {
        float w = rect.width - Pad * 2f;
        float y = 22f;

        int newTab = GUI.Toolbar(new Rect(Pad, y, w, 24f), tab, Tabs);
        if (newTab != tab)
            tab = newTab;
        y += 30f;

        if (!SandboxActions.Active)
        {
            GUI.Label(new Rect(Pad, y, w, 22f), "Inactive: enable Sandbox.Enabled; works offline or as host only.");
            y += 24f;
        }

        switch (tab)
        {
            case 0:
                DrawItems(y, w);
                break;
            case 1:
                DrawMobs(y, w);
                break;
            default:
                DrawPlayer(y, w);
                break;
        }

        GUI.Label(new Rect(Pad, rect.height - 26f, w, 22f), SandboxActions.LastMessage);
        GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
    }

    // ------------------------------------------------------------------ items

    void DrawItems(float y, float w)
    {
        var items = SandboxActions.Items;
        y = DrawSearch(y, w);
        giveToHand = GUI.Toggle(new Rect(Pad, y, w, 22f), giveToHand, "Click gives item to hand (off: drops ahead)");
        y += 26f;

        Refilter(0, SandboxActions.ItemsVersion, items.Count, null, items);
        float listHeight = rect.height - y - 76f;
        int clicked = DrawRows(new Rect(Pad, y, w, listHeight), ref itemsScroll, items, null);
        if (clicked >= 0)
        {
            var item = items[clicked];
            if (giveToHand)
                SandboxActions.GiveItem(item);
            else
                SandboxActions.SpawnItem(item);
        }

        y += listHeight + 4f;
        if (GUI.Button(new Rect(Pad, y, w, 24f), "Spawn test line (every item, in rows ahead of you)"))
            SandboxActions.SpawnTestLine();
    }

    // ------------------------------------------------------------------ entities

    void DrawMobs(float y, float w)
    {
        var mobs = SandboxActions.Mobs;
        y = DrawSearch(y, w);

        float half = (w - 4f) * 0.5f;
        if (GUI.Button(new Rect(Pad, y, half, 24f), "Rescan (quick)"))
            SandboxActions.RefreshMobs(deep: false);
        if (GUI.Button(new Rect(Pad + half + 4f, y, half, 24f), "Deep scan Resources"))
            SandboxActions.RefreshMobs(deep: true);
        y += 30f;

        Refilter(1, MobCatalog.Version, mobs.Count, mobs, null);
        float listHeight = rect.height - y - 36f;
        int clicked = DrawRows(new Rect(Pad, y, w, listHeight), ref mobsScroll, null, mobs);
        if (clicked >= 0)
            SandboxActions.SpawnMob(mobs[clicked]);
    }

    // ------------------------------------------------------------------ player

    static void DrawPlayer(float y, float w)
    {
        bool god = GUI.Toggle(new Rect(Pad, y, w, 24f), SandboxActions.GodMode, "God mode (infinite stamina, no afflictions, no fall damage)");
        if (god != SandboxActions.GodMode)
            SandboxActions.SetGodMode(god);
        y += 30f;

        float half = (w - 4f) * 0.5f;
        if (GUI.Button(new Rect(Pad, y, half, 26f), "Revive"))
            SandboxActions.Revive();
        if (GUI.Button(new Rect(Pad + half + 4f, y, half, 26f), "Warp to spawn"))
            SandboxActions.WarpToSpawn();
        y += 32f;

        bool cam = GUI.Toggle(new Rect(Pad, y, w, 24f), SandboxActions.FreeCamActive, "Free camera (flat only)");
        if (cam != SandboxActions.FreeCamActive)
            SandboxActions.ToggleFreeCam();
        y += 32f;

        GUI.Label(new Rect(Pad, y, w, 22f), "Jump to segment");
        y += 24f;
        y = DrawGrid(y, w, SegmentNames, 3, out int segment);
        if (segment >= 0)
            SandboxActions.JumpToSegment(SandboxActions.Segments[segment]);

        y += 6f;
        GUI.Label(new Rect(Pad, y, w, 22f), "Time of day");
        y += 24f;
        DrawGrid(y, w, TimeNames, 3, out int time);
        if (time >= 0)
            SandboxActions.SetTimeOfDay(SandboxActions.TimePresets[time]);
    }

    // ------------------------------------------------------------------ helpers

    float DrawSearch(float y, float w)
    {
        GUI.Label(new Rect(Pad, y, 56f, 22f), "Search");
        string q = GUI.TextField(new Rect(Pad + 58f, y, w - 58f, 22f), query, 64);
        if (!ReferenceEquals(q, query) && q != query)
        {
            query = q;
            terms = NameFilter.Parse(q);
        }
        return y + 28f;
    }

    /// <summary>Rebuild <see cref="filtered"/> only when the tab, the source list or the query changed.</summary>
    void Refilter(int forTab, int version, int count, IReadOnlyList<MobEntry> mobs, IReadOnlyList<Item> items)
    {
        if (filterTab == forTab && filterVersion == version && filterQuery == query)
            return;

        filterTab = forTab;
        filterVersion = version;
        filterQuery = query;
        filtered.Clear();
        for (int i = 0; i < count; i++)
        {
            string name = items != null ? items[i].name : mobs[i].Name;
            if (NameFilter.Matches(name, terms))
                filtered.Add(i);
        }
    }

    /// <summary>Draw the visible rows of <see cref="filtered"/>; returns the clicked source index or -1.</summary>
    int DrawRows(Rect view, ref Vector2 scroll, IReadOnlyList<Item> items, IReadOnlyList<MobEntry> mobs)
    {
        int clicked = -1;
        int count = filtered.Count;
        float width = view.width - 18f;

        scroll = GUI.BeginScrollView(view, scroll, new Rect(0f, 0f, width, count * RowHeight));
        int first = Mathf.Max(0, (int)(scroll.y / RowHeight));
        int last = Mathf.Min(count, first + (int)(view.height / RowHeight) + 2);
        for (int row = first; row < last; row++)
        {
            int index = filtered[row];
            string label = items != null ? items[index].name : mobs[index].Label;
            if (GUI.Button(new Rect(0f, row * RowHeight, width, RowHeight - 2f), label))
                clicked = index;
        }
        GUI.EndScrollView();
        return clicked;
    }

    /// <summary>A grid of buttons; <paramref name="clicked"/> is the index pressed this frame or -1.</summary>
    static float DrawGrid(float y, float w, string[] labels, int columns, out int clicked)
    {
        clicked = -1;
        float cell = (w - (columns - 1) * 4f) / columns;
        for (int i = 0; i < labels.Length; i++)
        {
            float x = Pad + (i % columns) * (cell + 4f);
            float cy = y + (i / columns) * 30f;
            if (GUI.Button(new Rect(x, cy, cell, 26f), labels[i]))
                clicked = i;
        }
        return y + ((labels.Length + columns - 1) / columns) * 30f;
    }

    static string[] BuildSegmentNames()
    {
        var names = new string[SandboxActions.Segments.Length];
        for (int i = 0; i < names.Length; i++)
            names[i] = SandboxActions.Segments[i].ToString();
        return names;
    }
}
