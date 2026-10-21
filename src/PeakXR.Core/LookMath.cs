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

        /// <summary>Past this yaw the synced half float steps 8 degrees or more (CharacterSyncData.cs:62).</summary>
        public const float YawSafetyLimit = 8192f;

        /// <summary>
        /// <paramref name="next"/> unwrapped to the turn nearest <paramref name="previous"/>. Remote peers linearly
        /// interpolate the synced yaw from its current value (CharacterSyncer.cs:93, :139-141), so a jump from 179 to
        /// -179, or any pull-back by whole turns, spins the avatar on their screens. Like vanilla mouse look
        /// (CharacterMovement.cs:286) the value accumulates. With <paramref name="rebase"/> (nobody can see the
        /// avatar) it wraps to one turn; past <see cref="YawSafetyLimit"/> it wraps anyway, before the half float
        /// gets coarse and long before it overflows.
        /// </summary>
        public static float ContinueYaw(float previous, float next, bool rebase = false)
        {
            float yaw = previous + WrapDegrees(next - previous);
            return rebase || Math.Abs(yaw) > YawSafetyLimit ? WrapDegrees(yaw) : yaw;
        }
    }
}
