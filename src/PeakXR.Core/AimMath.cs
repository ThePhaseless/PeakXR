using System;
using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>
    /// Aim directions and on-screen sizes for hand-aimed tools. In VR the aim comes from the hands, so a
    /// two-handed tool aims along the line between the hands, and sizes that the game derives from the flat
    /// camera's field of view use a fixed reference angle instead (the headset reports its own wide eye FOV).
    /// </summary>
    public static class AimMath
    {
        /// <summary>
        /// Aim along the two-hand axis: from the holding hand toward the supporting hand, flipped when the support
        /// hand is behind (the axis always points the way the holding hand points). Falls back to
        /// <paramref name="holdForward"/> when the hands are closer than <paramref name="minSeparation"/>.
        /// </summary>
        public static Vector3 TwoHandDirection(
            Vector3 holdPos,
            Vector3 supportPos,
            Vector3 holdForward,
            float minSeparation
        )
        {
            var axis = supportPos - holdPos;
            float len = axis.Length();
            if (len < minSeparation || len < 1e-5f)
                return SafeNormalize(holdForward);
            axis /= len;
            return Vector3.Dot(axis, holdForward) < 0f ? -axis : axis;
        }

        /// <summary>Size (metres) that spans <paramref name="angleDeg"/> at <paramref name="distance"/>.</summary>
        public static float AngularSize(float distance, float angleDeg) =>
            2f * distance * MathF.Tan(angleDeg * 0.5f * UnityQuat.Deg2Rad);

        /// <summary>The ping marker's frustum-height scale (vanilla <c>PointPing.Go</c>), computed from a fixed
        /// reference angle and clamped to the marker's own range.</summary>
        public static float PingMarkerScale(
            float distance,
            float referenceFovDeg,
            float minScale,
            float maxScale
        )
        {
            float s = AngularSize(distance, referenceFovDeg);
            return s < minScale ? minScale
                : s > maxScale ? maxScale
                : s;
        }

        static Vector3 SafeNormalize(Vector3 v)
        {
            float len = v.Length();
            return len > 1e-6f ? v / len : new Vector3(0f, 0f, 1f);
        }
    }
}
