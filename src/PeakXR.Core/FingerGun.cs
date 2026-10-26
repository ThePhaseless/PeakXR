namespace PeakXR.Core
{
    /// <summary>
    /// "Finger gun" ping gesture: point (grip closed, index straight), then squeeze the trigger. Curling the
    /// index to press the trigger ends the pointing pose, so the squeeze counts within <see cref="Window"/>
    /// seconds of the last pointing frame while the grip stays closed.
    /// </summary>
    public sealed class FingerGun
    {
        /// <summary>Default <see cref="Window"/>; a pointing grip's interact waits this long for the squeeze.</summary>
        public const float DefaultWindow = 0.35f;

        public float Window = DefaultWindow;

        /// <summary>
        /// Only an empty hand pings. A hand that holds an item, or steadies a two-handed one, keeps its grip closed
        /// and often its index off the trigger, which reads as pointing; its trigger squeeze is the item's use.
        /// </summary>
        public static bool HandMayFire(bool holdsItem, bool steadiesItem) =>
            !holdsItem && !steadiesItem;

        float _sincePoint = float.MaxValue;

        /// <summary>
        /// The hand is aiming a ping now: grip closed and pointing within <see cref="Window"/>, so it stays true while
        /// the index curls onto the trigger. False once the gesture fired or the grip opened. Drives the aim preview.
        /// </summary>
        public bool Aiming => _sincePoint <= Window;

        /// <returns>True on the frame the gesture fires.</returns>
        /// <param name="triggerResting">The index partly pulls the trigger, short of the press: the squeeze on its
        /// way, so the aim holds past <see cref="Window"/> instead of ending on a light press.</param>
        public bool Update(
            bool pointing,
            bool gripHeld,
            bool triggerPressed,
            float dt,
            bool triggerResting = false
        )
        {
            if (!gripHeld)
            {
                _sincePoint = float.MaxValue;
                return false;
            }
            if (pointing)
            {
                _sincePoint = 0f;
                return false;
            }
            if (_sincePoint < float.MaxValue && !triggerResting)
                _sincePoint += dt;
            if (!triggerPressed || _sincePoint > Window)
                return false;
            _sincePoint = float.MaxValue;
            return true;
        }
    }
}
