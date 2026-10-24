using System;
using System.Collections.Generic;

namespace PeakXR.Core;

/// <summary>
/// Which self-test steps a <c>-peakxr-selftest-only</c> run runs: a comma-separated list of step ids, each exact or a
/// prefix ending in one <c>*</c>. A full VR run takes 45 min; re-checking one or two steps should take minutes.
/// </summary>
public sealed class StepFilter
{
    readonly List<string> exact = new();
    readonly List<string> prefixes = new();
    readonly HashSet<string> ran = new(StringComparer.Ordinal);

    /// <summary>The patterns as given, trimmed.</summary>
    public IReadOnlyList<string> Patterns => patterns;

    readonly List<string> patterns = new();

    /// <summary>Parses <paramref name="spec"/>; false with <paramref name="error"/> naming the bad part.</summary>
    public static bool TryParse(string spec, out StepFilter filter, out string error)
    {
        filter = new StepFilter();
        error = null;
        if (string.IsNullOrWhiteSpace(spec))
        {
            error = "empty step filter";
            return false;
        }
        foreach (var raw in spec.Split(','))
        {
            var p = raw.Trim();
            if (p.Length == 0)
            {
                error = $"empty entry in step filter '{spec}'";
                return false;
            }
            int star = p.IndexOf('*');
            if (star >= 0 && (star != p.Length - 1 || p.Length == 1))
            {
                error = $"'{p}': a '*' may only end a prefix";
                return false;
            }
            filter.patterns.Add(p);
            if (star >= 0)
                filter.prefixes.Add(p.Substring(0, p.Length - 1));
            else
                filter.exact.Add(p);
        }
        return true;
    }

    /// <summary>True when <paramref name="id"/> is selected.</summary>
    public bool Matches(string id)
    {
        if (id == null)
            return false;
        foreach (var e in exact)
        {
            if (string.Equals(e, id, StringComparison.Ordinal))
                return true;
        }
        foreach (var p in prefixes)
        {
            if (id.StartsWith(p, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// The patterns no step can match: an exact id not in <paramref name="known"/> and not built on one of
    /// <paramref name="dynamicPrefixes"/>, or a prefix that starts no known id and overlaps no dynamic prefix.
    /// </summary>
    public string[] MatchingNothing(IEnumerable<string> known, IEnumerable<string> dynamicPrefixes)
    {
        var none = new List<string>();
        foreach (var pattern in patterns)
        {
            bool glob = pattern.EndsWith('*');
            string stem = glob ? pattern.Substring(0, pattern.Length - 1) : pattern;
            bool hit = false;
            foreach (var id in known)
            {
                if (glob ? id.StartsWith(stem, StringComparison.Ordinal) : id == stem)
                {
                    hit = true;
                    break;
                }
            }
            if (!hit)
            {
                foreach (var d in dynamicPrefixes)
                {
                    bool overlaps = glob
                        ? stem.StartsWith(d, StringComparison.Ordinal)
                            || d.StartsWith(stem, StringComparison.Ordinal)
                        : stem.StartsWith(d, StringComparison.Ordinal) && stem.Length > d.Length;
                    if (overlaps)
                    {
                        hit = true;
                        break;
                    }
                }
            }
            if (!hit)
                none.Add(pattern);
        }
        return none.ToArray();
    }

    /// <summary>True when some id starting with <paramref name="prefix"/> could be selected (a setup step such as the
    /// mob scan is only needed then).</summary>
    public bool MayMatchUnder(string prefix)
    {
        foreach (var e in exact)
        {
            if (e.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }
        foreach (var p in prefixes)
        {
            if (
                p.StartsWith(prefix, StringComparison.Ordinal)
                || prefix.StartsWith(p, StringComparison.Ordinal)
            )
                return true;
        }
        return false;
    }

    /// <summary>Records that the selected step <paramref name="id"/> ran.</summary>
    public void MarkRun(string id)
    {
        if (Matches(id))
            ran.Add(id);
    }

    /// <summary>Every selected step has run: only possible with exact ids, as a prefix can always match one more.</summary>
    public bool Complete
    {
        get
        {
            if (prefixes.Count > 0)
                return false;
            foreach (var e in exact)
            {
                if (!ran.Contains(e))
                    return false;
            }
            return true;
        }
    }
}
