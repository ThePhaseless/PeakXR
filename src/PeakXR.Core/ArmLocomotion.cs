using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>
    /// Climbing by pulling on grabbed holds. Feed hand positions in tracking space each frame; the result is
    /// the body displacement (opposite of the average motion of hands that were grabbing last frame and still are).
    /// A hand contributes nothing on the frame it starts grabbing, so grab start and end never cause a jump.
    /// </summary>
    public sealed class ArmLocomotion
    {
        /// <summary>Maximum body speed, m/s.</summary>
        public float MaxSpeed = 6f;

        bool _leftWas, _rightWas;
        Vector3 _leftLast, _rightLast;

        public Vector3 Update(bool leftGrab, Vector3 leftPos, bool rightGrab, Vector3 rightPos, float dt)
        {
            Vector3 sum = Vector3.Zero;
            int n = 0;
            if (leftGrab && _leftWas)
            {
                sum += leftPos - _leftLast;
                n++;
            }
            if (rightGrab && _rightWas)
            {
                sum += rightPos - _rightLast;
                n++;
            }
            _leftWas = leftGrab;
            _rightWas = rightGrab;
            _leftLast = leftPos;
            _rightLast = rightPos;

            if (n == 0 || dt <= 0f)
                return Vector3.Zero;

            Vector3 d = -sum / n;
            float max = MaxSpeed * dt;
            float len = d.Length();
            if (len > max && len > 0f)
                d *= max / len;
            return d;
        }

        public void Reset()
        {
            _leftWas = _rightWas = false;
        }
    }
}
