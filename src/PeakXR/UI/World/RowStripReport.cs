using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PeakXR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PeakXR.UI.World;

/// <summary>
/// Self-test diagnostic for settings row strips. Remembers what the strip pass decided for every image inside a
/// settings row and in which frame, and when each row was spawned, so a strip left drawn names its rule, its state
/// and its timing instead of only being counted. Records only on scans and spawns, never per frame.
/// </summary>
internal static class RowStripReport
{
    private struct Verdict
    {
        public int Frame;
        public BackdropRole Role;
        public string Rule;
        public string Outcome;
        public bool Parts;
    }

    private const int PruneAt = 512;

    private static readonly Dictionary<Graphic, Verdict> verdicts = new();
    private static readonly Dictionary<SettingsUICell, int> born = new();
    private static readonly List<SettingsUICell> cells = new();
    private static readonly List<Image> images = new();
    private static readonly Vector3[] corners = new Vector3[4];
    private static readonly StringBuilder line = new();
    private static bool listedAll;

    /// <summary>A row was spawned this frame (the game's tab fill or the VR tab).</summary>
    public static void Born(SettingsUICell cell)
    {
        if (cell == null || born.ContainsKey(cell))
            return;
        if (born.Count >= PruneAt)
            Prune(born);
        born[cell] = Time.frameCount;
    }

    /// <summary>The strip pass judged <paramref name="g"/>; kept only for images inside a settings row.</summary>
    public static void Judged(Graphic g, BackdropRole role, string rule, string outcome, bool parts)
    {
        if (g is not Image || g.GetComponentInParent<SettingsUICell>(true) == null)
            return;
        if (verdicts.Count >= PruneAt)
            Prune(verdicts);
        verdicts[g] = new Verdict
        {
            Frame = Time.frameCount,
            Role = role,
            Rule = rule,
            Outcome = outcome,
            Parts = parts,
        };
    }

    /// <summary>
    /// Strips drawn on <paramref name="root"/>: a plain image across most of a settings row that is not part of a
    /// control. Logs one line per candidate the first time, then only the drawn ones, plus a summary each call.
    /// </summary>
    public static int Count(Canvas root, string label)
    {
        int drawn = 0,
            candidates = 0;
        bool all = !listedAll;
        listedAll = true;
        var space = (RectTransform)root.transform;
        root.GetComponentsInChildren(false, cells);
        foreach (var cell in cells)
        {
            var width = ((RectTransform)cell.transform).rect.width;
            cell.GetComponentsInChildren(false, images);
            foreach (var image in images)
            {
                if (
                    image.GetComponentInParent<Selectable>() != null
                    || image.rectTransform.rect.width < 0.8f * width
                )
                    continue;
                candidates++;
                var cr = image.canvasRenderer;
                // Scrolled out of the list's RectMask2D: not drawn.
                bool draws = image.enabled && cr.GetAlpha() > 0f && !cr.cull;
                if (draws)
                    drawn++;
                if (draws || all)
                    Plugin.Log.LogInfo(Describe(image, cell, space, draws));
            }
            images.Clear();
        }
        cells.Clear();
        Plugin.Log.LogInfo(
            $"[ui.world] row strips on '{root.name}' {label}: {drawn} drawn of {candidates} (frame {Time.frameCount})"
        );
        return drawn;
    }

    private static string Describe(
        Image image,
        SettingsUICell cell,
        RectTransform space,
        bool draws
    )
    {
        var cr = image.canvasRenderer;
        image.rectTransform.GetWorldCorners(corners);
        Vector2 a = space.InverseTransformPoint(corners[0]);
        Vector2 b = space.InverseTransformPoint(corners[2]);
        line.Clear()
            .Append("[ui.world] row strip ")
            .Append(draws ? "DRAWN" : "hidden")
            .Append(" '");
        AppendPath(image.transform, space);
        line.Append("' active ")
            .Append(image.gameObject.activeInHierarchy ? 1 : 0)
            .Append(" enabled ")
            .Append(image.enabled ? 1 : 0)
            .Append(" alpha ")
            .Append(cr.GetAlpha().ToString("0.00", CultureInfo.InvariantCulture))
            .Append(" cullTransparent ")
            .Append(cr.cullTransparentMesh ? 1 : 0)
            .Append(" culled ")
            .Append(cr.cull ? 1 : 0)
            .Append(" depth ")
            .Append(image.depth)
            .Append(" rect ")
            .Append(Mathf.Min(a.x, b.x).ToString("0", CultureInfo.InvariantCulture))
            .Append(',')
            .Append(Mathf.Min(a.y, b.y).ToString("0", CultureInfo.InvariantCulture))
            .Append(' ')
            .Append(Mathf.Abs(b.x - a.x).ToString("0", CultureInfo.InvariantCulture))
            .Append('x')
            .Append(Mathf.Abs(b.y - a.y).ToString("0", CultureInfo.InvariantCulture))
            .Append(" | ");
        if (verdicts.TryGetValue(image, out var v))
            line.Append(v.Role)
                .Append(" by '")
                .Append(v.Rule)
                .Append("', ")
                .Append(v.Outcome)
                .Append(v.Parts ? " (parts pass)" : "")
                .Append(" at frame ")
                .Append(v.Frame);
        else
            line.Append("never judged");
        line.Append(" | row born frame ")
            .Append(
                born.TryGetValue(cell, out var f) ? f.ToString(CultureInfo.InvariantCulture) : "?"
            )
            .Append(", now ")
            .Append(Time.frameCount);
        return line.ToString();
    }

    private static void AppendPath(Transform t, Transform root)
    {
        int start = line.Length;
        for (var p = t; p != null && p != root; p = p.parent)
            line.Insert(start, p == t ? p.name : p.name + "/");
    }

    private static void Prune<T, TValue>(Dictionary<T, TValue> map)
        where T : Object
    {
        var dead = new List<T>();
        foreach (var key in map.Keys)
            if (key == null)
                dead.Add(key);
        foreach (var key in dead)
            map.Remove(key);
        // Everything still alive (a huge list): start over rather than prune on every call.
        if (map.Count >= PruneAt)
            map.Clear();
    }
}
