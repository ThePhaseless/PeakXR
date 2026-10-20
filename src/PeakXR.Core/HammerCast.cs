namespace PeakXR.Core
{
    /// <summary>
    /// Hammering a climbing spike instead of holding a button for its cast time. The game adds the frame time to
    /// its cast timer and places the spike once the timer reaches the cast time (CharacterItems.cs:999-1004).
    /// Setting the timer before that increment to the value returned here makes each strike fill its share of
    /// the cast and only the last strike complete it, whatever the frame time.
    /// </summary>
    public static class HammerCast
    {
        /// <summary>Smallest timer value: the game only continues a cast while the timer is above zero.</summary>
        public const float MinTick = 1e-4f;

        /// <param name="strikes">Strikes landed since the cast started.</param>
        /// <param name="needed">Strikes that complete the cast (at least 1).</param>
        /// <param name="castTime">The spike's cast time, seconds.</param>
        /// <param name="dt">The frame time the game is about to add.</param>
        /// <returns>The timer value to set before the game's increment; always above zero.</returns>
        public static float TickBeforeUpdate(int strikes, int needed, float castTime, float dt)
        {
            if (needed < 1)
                needed = 1;
            if (castTime <= 0f)
                return MinTick;
            if (strikes >= needed)
                return castTime;
            float tick = castTime * strikes / needed - dt;
            float cap = castTime - dt - MinTick;
            if (tick > cap)
                tick = cap;
            return tick > MinTick ? tick : MinTick;
        }
    }
}
