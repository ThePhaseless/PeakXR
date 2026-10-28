using System;
using System.Collections.Generic;

namespace PeakXR.Core;

/// <summary>What a patch guard does with one caught error.</summary>
public enum FaultAction
{
    /// <summary>First error at this site: log it with its stack.</summary>
    Log,

    /// <summary>Seen before: say nothing, so a per-frame error cannot flood the log.</summary>
    Quiet,

    /// <summary>The owner keeps failing: turn it off (once).</summary>
    Disable,
}

/// <summary>
/// Error policy for Harmony patch bodies: an error in a patch must never reach game code, and a feature that throws
/// every frame is turned off, while one that throws now and then (a race at scene load) only logs. Counts per owner
/// inside a sliding window, so a sporadic error never adds up to a shutdown over a long session.
/// </summary>
public sealed class FaultTally
{
    /// <summary>Errors within <see cref="WindowSeconds"/> that turn an owner off: under half a second of
    /// per-frame errors at 90 Hz.</summary>
    public const int DisableAfter = 30;

    public const float WindowSeconds = 5f;

    readonly HashSet<string> sites = new(StringComparer.Ordinal);
    readonly Dictionary<string, Owner> owners = new(StringComparer.Ordinal);

    sealed class Owner
    {
        public float WindowStart;
        public int Count;
        public bool Disabled;
    }

    public FaultAction Record(string owner, string site, float now)
    {
        if (!owners.TryGetValue(owner, out var o))
            owners[owner] = o = new Owner { WindowStart = now };
        if (now - o.WindowStart > WindowSeconds)
        {
            o.WindowStart = now;
            o.Count = 0;
        }
        o.Count++;
        bool first = sites.Add(owner + "|" + site);
        if (!o.Disabled && o.Count >= DisableAfter)
        {
            o.Disabled = true;
            return FaultAction.Disable;
        }
        return first ? FaultAction.Log : FaultAction.Quiet;
    }
}
