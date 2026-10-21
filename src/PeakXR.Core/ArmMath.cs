using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>
    /// Elbow placement for tracked arms. The scout prefab ships its arm IK with hint weight 0, so the two-bone solver
    /// keeps whatever bend plane the idle animation has and swings the whole chain up to the hand: with hands in front
    /// of the face (the scout's shoulders sit 0.47 m under the camera) the elbows end up splayed sideways at hand
    /// height. A pole below, outside and behind the arm gives the natural elbows-down pose for any reach.
    /// </summary>
    public static class ArmMath
    {
        /// <summary>Distance of the hint from the shoulder-hand midpoint; the solver only uses its direction.</summary>
        public const float HintDistance = 0.5f;

        /// <summary>
        /// Hint point for one arm's elbow, in the same space as <paramref name="shoulder"/> and
        /// <paramref name="hand"/>. <paramref name="right"/>, <paramref name="up"/> and <paramref name="forward"/>
        /// are the body's axes.
        /// </summary>
        public static Vector3 ElbowHint(
            Vector3 shoulder,
            Vector3 hand,
            Vector3 right,
            Vector3 up,
            Vector3 forward,
            bool left
        )
        {
            var pole = -up + (left ? -0.5f : 0.5f) * right - 0.3f * forward;
            return (shoulder + hand) * 0.5f + Vector3.Normalize(pole) * HintDistance;
        }

        /// <summary>Unit direction of the elbow off the shoulder-hand line, or zero for a straight arm.</summary>
        public static Vector3 BendDirection(Vector3 shoulder, Vector3 elbow, Vector3 hand)
        {
            var axis = hand - shoulder;
            var off = elbow - shoulder;
            float len2 = axis.LengthSquared();
            if (len2 > 1e-8f)
                off -= axis * (Vector3.Dot(off, axis) / len2);
            return off.LengthSquared() > 1e-8f ? Vector3.Normalize(off) : Vector3.Zero;
        }
    }
}
