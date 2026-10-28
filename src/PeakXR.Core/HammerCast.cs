using System;

namespace PeakXR.Core;

/// <summary>
/// Hammering a climbing spike while holding the use button. The game adds the frame time to its cast timer and
/// places the spike once the timer reaches the cast time (CharacterItems.cs:999-1004). Holding alone places it at
/// the game's own time; each strike brings it forward by its share, and all <c>needed</c> strikes bring it to
/// <see cref="MaxSpeedup"/> times the hold's pace, so it stays fair against a flat player's timer. The cast
/// is local to the owner and the placement is the game's own RPC, so nothing new is sent.
/// </summary>
public static class HammerCast
{
    /// <summary>Smallest timer value: the game only continues a cast while the timer is above zero.</summary>
    public const float MinTick = 1e-4f;

    /// <summary>The fastest a hammered spike goes in, as a multiple of the hold's pace: a third sooner.</summary>
    public const float MaxSpeedup = 1.5f;

    /// <param name="strikes">Strikes landed since the cast started.</param>
    /// <param name="needed">Strikes that complete the cast (at least 1).</param>
    /// <param name="castTime">The spike's cast time, seconds.</param>
    /// <param name="elapsed">The timer a vanilla hold would have now, before this frame's increment.</param>
    /// <returns>The timer value to set before the game's increment; always above zero, never below
    /// <paramref name="elapsed"/>.</returns>
    public static float TickBeforeUpdate(int strikes, int needed, float castTime, float elapsed)
    {
        if (needed < 1)
            needed = 1;
        if (castTime <= 0f)
            return MinTick;
        // Each strike earns its share of the speed-up: none is the hold's pace, all is MaxSpeedup.
        float share = Math.Min(Math.Max(strikes, 0), needed) / (float)needed;
        float tick = elapsed * (1f + (MaxSpeedup - 1f) * share);
        return tick > MinTick ? tick : MinTick;
    }
}
