using System;
using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>Converts headset rotation to PEAK's look values.</summary>
    public static class LookMath
    {
        public const float MaxPitch = 85f;

        /// <summary>Wraps an angle in degrees to (-180, 180].</summary>
        public static float WrapDegrees(float deg)
        {
            deg %= 360f;
            if (deg <= -180f)
                deg += 360f;
            else if (deg > 180f)
                deg -= 360f;
            return deg;
        }

        /// <summary>
        /// PEAK look values from a world-space head rotation: X = yaw in degrees wrapped to (-180, 180],
        /// Y = pitch in degrees with positive looking up, clamped to [-85, 85]. Roll is ignored.
        /// </summary>
        public static Vector2 ToLookValues(Quaternion hmdWorldRot)
        {
            float yaw = WrapDegrees(UnityQuat.HeadingRadians(hmdWorldRot) * UnityQuat.Rad2Deg);
            Vector3 f = UnityQuat.Rotate(hmdWorldRot, new Vector3(0f, 0f, 1f));
            float pitch = MathF.Asin(Math.Clamp(f.Y, -1f, 1f)) * UnityQuat.Rad2Deg;
            return new Vector2(yaw, Math.Clamp(pitch, -MaxPitch, MaxPitch));
        }
    }
}
