using System;

namespace PeakXR.Core
{
    /// <summary>
    /// Hammering a climbing spike while holding the use button. The game adds the frame time to its cast timer and
    /// places the spike once the timer reaches the cast time (CharacterItems.cs:999-1004). Holding alone places it at
    /// the game's own time; the strikes can only bring it forward, and all <c>needed</c> strikes bring it forward to at
    /// most <see cref="MaxSpeedup"/> times the hold's pace, so it stays fair against a flat player's timer. The cast
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
        /// <param name="dt">The frame time the game is about to add.</param>
        /// <param name="elapsed">The timer a vanilla hold would have now, before this frame's increment.</param>
        /// <returns>The timer value to set before the game's increment; always above zero, never below
        /// <paramref name="elapsed"/>.</returns>
        public static float TickBeforeUpdate(
            int strikes,
            int needed,
            float castTime,
            float dt,
            float elapsed
        )
        {
            if (needed < 1)
                needed = 1;
            if (castTime <= 0f)
                return MinTick;
            // The strikes' share of the cast; short of the last strike it never completes it.
            float struck =
                strikes >= needed
                    ? castTime
                    : Math.Min(castTime * strikes / needed, castTime - dt - MinTick);
            float tick = Math.Max(elapsed, Math.Min(struck, elapsed * MaxSpeedup));
            return tick > MinTick ? tick : MinTick;
        }
    }
}
