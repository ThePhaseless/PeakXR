using System;

namespace PeakXR.Core
{
    /// <summary>Snap turning from a stick X axis. Call <see cref="Update"/> once per frame.</summary>
    public sealed class SnapTurn
    {
        /// <summary>Degrees per snap. Positive stick X turns right (positive yaw).</summary>
        public float Angle = 30f;
        public float ActivateThreshold = 0.7f;
        public float ReleaseThreshold = 0.3f;

        /// <summary>Seconds between repeated snaps while the stick stays deflected; 0 disables repeat.</summary>
        public float RepeatDelay;

        bool _armed = true;
        float _timer;
        int _sign;

        /// <summary>Returns the yaw delta in degrees for this frame (0 when no snap fires).</summary>
        public float Update(float stickX, float dt)
        {
            float mag = MathF.Abs(stickX);
            int sign = MathF.Sign(stickX);
            // A flick straight to the other side is a fresh press even though the stick never centred.
            if (_armed || (mag >= ActivateThreshold && sign != _sign))
            {
                if (mag < ActivateThreshold)
                    return 0f;
                _armed = false;
                _timer = 0f;
                _sign = sign;
                return sign * Angle;
            }

            if (mag < ReleaseThreshold)
            {
                _armed = true;
                return 0f;
            }

            if (mag < ActivateThreshold)
            {
                // Eased off between the thresholds: the hold is over, the repeat delay starts again.
                _timer = 0f;
                return 0f;
            }
            if (RepeatDelay <= 0f)
                return 0f;
            _timer += dt;
            // Small tolerance so that summed frame times hit the delay on the expected frame.
            if (_timer < RepeatDelay - 1e-5f)
                return 0f;
            _timer -= RepeatDelay;
            return _sign * Angle;
        }

        public void Reset()
        {
            _armed = true;
            _timer = 0f;
        }
    }

    /// <summary>Continuous turning from a stick X axis.</summary>
    public static class SmoothTurn
    {
        /// <summary>Yaw delta in degrees: <paramref name="degPerSec"/> * shaped axis * dt.</summary>
        public static float Delta(
            float stickX,
            float degPerSec,
            float dt,
            float deadzone = 0.15f
        ) => Stick.Axis(stickX, deadzone, 1f) * degPerSec * dt;
    }
}
