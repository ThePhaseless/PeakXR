namespace PeakXR.Core
{
    /// <summary>
    /// "Finger gun" ping gesture: point (grip closed, index straight), then squeeze the trigger. Curling the
    /// index to press the trigger ends the pointing pose, so the squeeze counts within <see cref="Window"/>
    /// seconds of the last pointing frame while the grip stays closed.
    /// </summary>
    public sealed class FingerGun
    {
        public float Window = 0.35f;

        float _sincePoint = float.MaxValue;

        /// <returns>True on the frame the gesture fires.</returns>
        public bool Update(bool pointing, bool gripHeld, bool triggerPressed, float dt)
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
            if (_sincePoint < float.MaxValue)
                _sincePoint += dt;
            if (!triggerPressed || _sincePoint > Window)
                return false;
            _sincePoint = float.MaxValue;
            return true;
        }
    }
}
