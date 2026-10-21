using System;
using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>
    /// Crouch from the real head: in when the head drops more than <see cref="EnterFraction"/> of the standing
    /// height below the calibrated eye, out above <see cref="ExitFraction"/>, so standing at the edge never flickers.
    /// </summary>
    public sealed class PhysicalCrouch
    {
        public float EnterFraction = 0.30f;
        public float ExitFraction = 0.22f;

        public bool Crouched { get; private set; }

        /// <param name="enabled">Setting on, head tracked, not seated.</param>
        /// <param name="headY">Tracking-space head height now.</param>
        /// <param name="calibratedEyeY">Tracking-space eye height captured at the last recenter.</param>
        /// <param name="standingHeight">Real standing eye height above the floor, metres (reference height when the
        /// tracking space has no floor).</param>
        public bool Update(bool enabled, float headY, float calibratedEyeY, float standingHeight)
        {
            if (!enabled || !(standingHeight >= HeightCalibration.MinPlausibleEyeHeight))
                return Crouched = false;
            float drop = (calibratedEyeY - headY) / standingHeight;
            if (float.IsNaN(drop))
                return Crouched = false;
            Crouched = Crouched ? drop > ExitFraction : drop > EnterFraction;
            return Crouched;
        }
    }

    /// <summary>
    /// Arm-swing sprint: alternating hand swings (hands moving in opposite directions, positions relative to the
    /// head) above <see cref="EnterSpeed"/> average hand speed hold sprint; it lets go below <see cref="ExitSpeed"/>.
    /// The average decays over <see cref="TimeConstant"/>, so the slow points of each swing do not drop it.
    /// </summary>
    public sealed class ArmSwing
    {
        public float EnterSpeed = 1.2f;
        public float ExitSpeed = 0.8f;
        public float TimeConstant = 0.35f;

        bool _have;
        Vector3 _left,
            _right;
        float _average;

        public bool Active { get; private set; }

        /// <param name="left">Left hand position relative to the head, tracking space.</param>
        /// <param name="right">Right hand position relative to the head, tracking space.</param>
        public bool Update(
            bool leftTracked,
            Vector3 left,
            bool rightTracked,
            Vector3 right,
            float dt
        )
        {
            if (!leftTracked || !rightTracked || dt <= 0f)
            {
                Reset();
                return false;
            }
            float sample = 0f;
            if (_have)
            {
                var vl = (left - _left) / dt;
                var vr = (right - _right) / dt;
                if (Vector3.Dot(vl, vr) < 0f)
                    sample = (vl.Length() + vr.Length()) * 0.5f;
            }
            _have = true;
            _left = left;
            _right = right;
            _average += (sample - _average) * (1f - MathF.Exp(-dt / TimeConstant));
            Active = Active ? _average > ExitSpeed : _average > EnterSpeed;
            return Active;
        }

        public void Reset()
        {
            _have = false;
            _average = 0f;
            Active = false;
        }
    }
}
