using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Positions for the sandbox "spawn test line": every item laid out on a grid in front of the tester so
/// each one can be inspected, grabbed and used in turn. Pure math so it can be unit tested.
/// </summary>
public static class SpawnLayout
{
    /// <summary>
    /// Fills <paramref name="positions"/> with grid points. Items go left to right in rows of
    /// <paramref name="perRow"/>, each row centered on the origin line; rows advance along
    /// <paramref name="forward"/> (flattened to the ground plane, so looking up or down is harmless).
    /// </summary>
    public static void Grid(
        Vector3[] positions,
        Vector3 origin,
        Vector3 forward,
        float spacing,
        int perRow
    )
    {
        if (perRow < 1)
            perRow = 1;

        forward.Y = 0f;
        forward = forward.LengthSquared() < 1e-8f ? Vector3.UnitZ : Vector3.Normalize(forward);
        Vector3 right = Vector3.Cross(Vector3.UnitY, forward);

        for (int i = 0; i < positions.Length; i++)
        {
            int row = i / perRow;
            int col = i % perRow;
            int inRow = Math.Min(perRow, positions.Length - row * perRow);
            float x = (col - (inRow - 1) * 0.5f) * spacing;
            positions[i] = origin + right * x + forward * (row * spacing);
        }
    }
}

/// <summary>Search box matching for the sandbox lists: every term must appear in the name, any case.</summary>
public static class NameFilter
{
    static readonly char[] SplitChars = { ' ', '\t' };
    static readonly string[] None = Array.Empty<string>();

    /// <summary>Split a query once when it changes, so per-frame matching does not allocate.</summary>
    public static string[] Parse(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return None;
        return query.Split(SplitChars, StringSplitOptions.RemoveEmptyEntries);
    }

    public static bool Matches(string name, string[] terms)
    {
        if (terms.Length == 0)
            return true;
        if (name == null)
            return false;
        for (int i = 0; i < terms.Length; i++)
        {
            if (name.IndexOf(terms[i], StringComparison.OrdinalIgnoreCase) < 0)
                return false;
        }
        return true;
    }
}

/// <summary>
/// Whether this run used the sandbox, so it must not touch the player's real progress (quicksave, achievements,
/// stats, ascents) and the player hears about that once. A run spans from the title or the airport to the next one:
/// anything spawned or switched on in the airport rides into the run that boards from it. God mode still on when a
/// new run starts keeps the mark.
/// </summary>
public sealed class SandboxRunMark
{
    public bool Marked { get; private set; }

    /// <summary>The player has not been told yet that this run saves nothing.</summary>
    public bool NoticeDue { get; private set; }

    public void Use()
    {
        if (Marked)
            return;
        Marked = true;
        NoticeDue = true;
    }

    public void NoticeShown() => NoticeDue = false;

    public void SceneLoaded(string scene, bool godMode)
    {
        if (godMode || (scene != "Title" && scene != "Airport"))
            return;
        Marked = false;
        NoticeDue = false;
    }
}
