using System;
using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>
    /// Glider heading from the hands holding the wing: perpendicular to the line between the hands, turned toward
    /// the lower hand when the arms lean. Lets the player steer with the arms and look around freely, where the
    /// game steers by body facing. World space, Y up, Unity's left-handed axes (Z forward, X right).
    /// </summary>
    public static class GliderSteer
    {
        /// <param name="left">Left hand position.</param>
        /// <param name="right">Right hand position.</param>
        /// <param name="minSpan">Horizontal hand separation (metres) below which there is no heading.</param>
        /// <param name="bankGain">Degrees of heading per degree of arm lean.</param>
        /// <param name="maxBankDegrees">Largest heading offset from leaning.</param>
        /// <param name="forward">Horizontal unit heading.</param>
        public static bool TryForward(
            Vector3 left,
            Vector3 right,
            float minSpan,
            float bankGain,
            float maxBankDegrees,
            out Vector3 forward
        )
        {
            forward = Vector3.Zero;
            var d = right - left;
            float span = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
            if (span < minSpan || span < 1e-5f)
                return false;

            // Cross(right axis, up), flattened.
            float fx = -d.Z / span;
            float fz = d.X / span;

            // Right hand lower: lean negative, turn right (positive yaw about up).
            float lean = MathF.Atan2(d.Y, span) * UnityQuat.Rad2Deg;
            float yaw =
                Math.Clamp(-lean * bankGain, -maxBankDegrees, maxBankDegrees) * UnityQuat.Deg2Rad;
            float c = MathF.Cos(yaw);
            float s = MathF.Sin(yaw);
            forward = new Vector3(fx * c + fz * s, 0f, -fx * s + fz * c);
            return true;
        }
    }
}
