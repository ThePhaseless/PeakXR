namespace PeakXR.Core
{
    /// <summary>
    /// The free hand pulling rope off a held spool. The grip has to close within <see cref="GrabRadius"/> of the
    /// spool; from then on the change in hand-to-spool distance becomes the game's scroll value, so pulling away
    /// pays out and pushing back reels in by the same length. The spool turns scroll into
    /// <see cref="MetresPerSecondPerScroll"/> of rope (RopeSpool.cs:122-140), and its smoothing keeps the total.
    /// </summary>
    public sealed class RopePull
    {
        /// <summary>Rope per second at scroll 1: 25 segments/s of 0.25 m (RopeSpool.FixedUpdate, Rope.cs:77).</summary>
        public const float MetresPerSecondPerScroll = 6.25f;

        /// <summary>The spool clamps its smoothed scroll to this range (RopeSpool.cs:132).</summary>
        public const float MinScroll = -1f;
        public const float MaxScroll = 5f;

        /// <summary>How close (metres) to the spool the grip must close.</summary>
        public float GrabRadius = 0.15f;

        /// <summary>Rope length per haptic tick: one rope segment.</summary>
        public float MetresPerTick = 0.25f;

        /// <summary>Rope paid out per metre of hand movement.</summary>
        public float Gain = 1f;

        bool _prevGrip;
        float _last;
        float _tickAccum;

        /// <summary>True while the hand holds rope at the spool.</summary>
        public bool Active { get; private set; }

        /// <param name="grip">The free hand's grip is closed.</param>
        /// <param name="distance">Free hand to spool, metres.</param>
        /// <param name="dt">Frame time, seconds.</param>
        /// <param name="ticks">Whole rope segments crossed this frame (signed), for haptics.</param>
        /// <returns>Scroll value for the spool (positive pays out).</returns>
        public float Update(bool grip, float distance, float dt, out int ticks)
        {
            ticks = 0;
            bool pressed = grip && !_prevGrip;
            _prevGrip = grip;
            if (!grip)
            {
                Active = false;
                return 0f;
            }
            if (!Active)
            {
                if (!pressed || distance > GrabRadius)
                    return 0f;
                Active = true;
                _last = distance;
                _tickAccum = 0f;
                return 0f;
            }

            float delta = (distance - _last) * Gain;
            _last = distance;
            _tickAccum += delta;
            if (MetresPerTick > 0f)
            {
                ticks = (int)(_tickAccum / MetresPerTick);
                _tickAccum -= ticks * MetresPerTick;
            }
            if (dt <= 0f)
                return 0f;
            float scroll = delta / dt / MetresPerSecondPerScroll;
            return scroll < MinScroll ? MinScroll
                : scroll > MaxScroll ? MaxScroll
                : scroll;
        }

        public void Reset()
        {
            _prevGrip = false;
            _tickAccum = 0f;
            Active = false;
        }
    }
}
