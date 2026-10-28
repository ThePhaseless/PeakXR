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

    /// <summary>The site keeps failing (once per site): report it as failing, not just degraded.</summary>
    Persistent,
}

/// <summary>
/// Error policy for Harmony patch bodies, per site (file:line of the catch). A site is persistent once it failed on at
/// least <see cref="PersistentFrames"/> distinct frames within the last <see cref="WindowSeconds"/>, spread over at
/// least <see cref="MinSpanSeconds"/>. Distinct frames, so a patch that runs once per character fails once per frame
/// however many characters there are; the span, so a scene-load race (a few frames) or a one-second hitch only logs;
/// the sliding window, so an error now and then never adds up over a long session.
/// </summary>
public sealed class FaultTally
{
    /// <summary>Failing frames within the window: two thirds of a second of every frame at 90 Hz.</summary>
    public const int PersistentFrames = 60;

    public const float WindowSeconds = 10f;

    public const float MinSpanSeconds = 2f;

    readonly Dictionary<string, Site> sites = new(StringComparer.Ordinal);

    sealed class Site
    {
        public readonly Queue<float> Frames = new();
        public int LastFrame = int.MinValue;
        public bool Persistent;
    }

    /// <param name="site">Owner and place, e.g. <c>player.locomotion|LocomotionPatches.cs:35</c>.</param>
    /// <param name="frame">The frame number, so several errors in one frame count once.</param>
    /// <param name="now">Seconds, any monotonic clock.</param>
    public FaultAction Record(string site, int frame, float now)
    {
        bool first = !sites.TryGetValue(site, out var s);
        if (first)
            sites[site] = s = new Site();
        if (s.Persistent)
            return FaultAction.Quiet;
        if (frame != s.LastFrame)
        {
            s.LastFrame = frame;
            s.Frames.Enqueue(now);
        }
        while (s.Frames.Count > 0 && now - s.Frames.Peek() > WindowSeconds)
            s.Frames.Dequeue();
        if (s.Frames.Count >= PersistentFrames && now - s.Frames.Peek() >= MinSpanSeconds)
        {
            s.Persistent = true;
            s.Frames.Clear();
            return FaultAction.Persistent;
        }
        return first ? FaultAction.Log : FaultAction.Quiet;
    }
}
