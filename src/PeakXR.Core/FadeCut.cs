using System.Collections.Generic;

namespace PeakXR.Core;

/// <summary>
/// When a view cut may happen. A requester ramps its own fade, but the fade actually drawn eases at
/// <c>UI.FadeSeconds</c> (up to 1.5 s), so a cut timed by the requester's ramp lands while the world still shows.
/// Every cut waits for the drawn fade to be black, the way loading does, with a timeout so a stuck fade never
/// freezes the view.
/// </summary>
public static class FadeCut
{
    /// <summary>The drawn fade at or above this is black.</summary>
    public const float BlackLevel = 0.99f;

    /// <summary>The longest a cut waits from the start of its fade-out: the slowest <c>UI.FadeSeconds</c> plus a
    /// margin.</summary>
    public const float MaxWaitSeconds = 2f;

    /// <param name="drawn">The drawn fade, 0 clear to 1 black.</param>
    /// <param name="waited">Seconds since the fade-out started.</param>
    public static bool Ready(float drawn, float waited) =>
        drawn >= BlackLevel || waited >= MaxWaitSeconds;
}

/// <summary>
/// The fade requests of every holder; the strongest wins. A hold set with an end time lapses on its own, so a
/// holder that never clears (replaced or torn down mid-fade) cannot leave the headset black.
/// </summary>
public sealed class FadeHolds
{
    private struct Hold
    {
        public float Alpha;
        public float Until;
    }

    private readonly Dictionary<object, Hold> holds = new();

    /// <param name="until">The time the hold lapses on its own (same clock as <see cref="Target"/>).</param>
    public void Set(object owner, float alpha, float until = float.PositiveInfinity) =>
        holds[owner] = new Hold { Alpha = alpha, Until = until };

    public bool Clear(object owner) => holds.Remove(owner);

    /// <summary>The strongest live hold at <paramref name="now"/>, 0 when none.</summary>
    public float Target(float now)
    {
        float max = 0f;
        foreach (var h in holds.Values)
            if (h.Until > now && h.Alpha > max)
                max = h.Alpha;
        return max;
    }
}
