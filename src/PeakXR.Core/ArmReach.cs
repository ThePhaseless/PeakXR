using System;
using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>
    /// Arm-length calibration: maps the player's reach onto the avatar's. The scout's shoulders sit about 0.47 m under
    /// and 0.2 m behind its eyes (a person's about 0.25 m and 0.08 m) with 0.605 m of arm, so hands a person holds
    /// comfortably in front of the chest are out of its reach, and a lean (whose bend moves the shoulders about half
    /// as far as the eyes) puts them further out: run 28 had the controllers 0.86 m from the leaned shoulders and
    /// the arms locked straight. Close in, the hand stays exactly on the controller; past a knee, distance from the
    /// avatar's shoulder is compressed smoothly so the player's longest measured reach lands on the avatar's
    /// slightly bent full reach. The hand keeps the controller's direction from the shoulder and its rotation.
    /// </summary>
    public static class ArmReach
    {
        /// <summary>Fraction of the straight arm the avatar reaches at most: the elbow keeps a natural ~30 degree
        /// bend instead of locking (and flipping) straight.</summary>
        public const float BentReach = 0.97f;

        /// <summary>Fraction of the bent reach within which the hand is exactly on the controller.</summary>
        public const float Knee = 0.8f;

        /// <summary>Player reach bounds, in bent avatar reaches. Below the minimum nothing needs compressing; above
        /// the maximum a sample is a warp or a lagging view, not an arm (and the curve would turn back).</summary>
        public const float MinPlayer = 1.1f;

        public const float MaxPlayer = 1.9f;

        /// <summary>
        /// Distance of the avatar's hand from its shoulder for a controller <paramref name="distance"/> from it.
        /// Identity up to the knee, then a cubic easing (slope 1 at the knee, 0 at the player's reach, monotone)
        /// up to the bent reach, which it never passes.
        /// </summary>
        public static float Map(float distance, float avatarReach, float playerReach)
        {
            float r = avatarReach * BentReach;
            float p = Math.Clamp(playerReach, r * MinPlayer, r * MaxPlayer);
            // The cubic stays monotone while its start slope (p - k) / (r - k) is at most 3.
            float k = MathF.Min(r * Knee, (3f * r - p) * 0.5f);
            if (distance <= k)
                return distance;
            if (distance >= p)
                return r;
            float m = (p - k) / (r - k);
            float t = (distance - k) / (p - k);
            return k + (r - k) * t * (m + t * (3f - 2f * m + t * (m - 2f)));
        }

        /// <summary>The avatar's hand target for a controller at <paramref name="controller"/>: on the line from
        /// <paramref name="shoulder"/>, at the mapped distance.</summary>
        public static Vector3 Target(
            Vector3 shoulder,
            Vector3 controller,
            float avatarReach,
            float playerReach
        )
        {
            var off = controller - shoulder;
            float d = off.Length();
            return d < 1e-6f ? controller : shoulder + off * (Map(d, avatarReach, playerReach) / d);
        }

        /// <summary>The player's reach after a sample of controller-to-avatar-shoulder distance: the longest seen,
        /// ignoring implausible samples.</summary>
        public static float Learn(float playerReach, float sample, float avatarReach) =>
            sample > avatarReach * BentReach * MaxPlayer
                ? playerReach
                : MathF.Max(playerReach, sample);
    }
}
