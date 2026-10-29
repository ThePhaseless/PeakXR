using System;
using System.Collections.Generic;

namespace PeakXR.Core;

/// <summary>
/// Which uncaught Unity exceptions to copy into the BepInEx log. Where BepInEx cannot start its Unity log writer they
/// land only in Player.log, which users rarely send. Only stacks with PeakXR frames; each distinct stack the first
/// time, then its count at 10, 100, 1000 repeats, so an exception every frame cannot flood the log.
/// </summary>
public sealed class ExceptionDigest
{
    /// <summary>Distinct stacks remembered; later new ones stay quiet, so memory stays bounded.</summary>
    public const int MaxStacks = 256;

    readonly Dictionary<string, int> counts = new(StringComparer.Ordinal);

    public static bool IsOurs(string stack) =>
        stack != null && stack.Contains("PeakXR.", StringComparison.Ordinal);

    /// <summary>Counts one more of <paramref name="stack"/>; the count to report now, or 0 to stay quiet.</summary>
    public int Record(string stack)
    {
        stack ??= "";
        if (!counts.TryGetValue(stack, out int n) && counts.Count >= MaxStacks)
            return 0;
        counts[stack] = ++n;
        while (n % 10 == 0)
            n /= 10;
        return n == 1 ? counts[stack] : 0;
    }
}
