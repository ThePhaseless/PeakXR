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
            // NaN fails every comparison and would pass a clamp: treat it as not learned yet.
            float p = Math.Clamp(
                playerReach >= 0f ? playerReach : 0f,
                r * MinPlayer,
                r * MaxPlayer
            );
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

        /// <summary>Seconds the map takes to fade in, or out when the body stops being upright.</summary>
        public const float FadeSeconds = 0.3f;

        /// <summary>
        /// One tick of a hand's map weight. Grabbing drops it at once: the item is welded to the controller pose
        /// that same tick, so a hand still fading in from the mapped target would jump and tug. Releasing (and
        /// standing up) fades it back in, moving the hand off the controller smoothly.
        /// </summary>
        public static float StepWeight(float weight, bool holding, bool upright, float dt)
        {
            if (holding)
                return 0f;
            float to = upright ? 1f : 0f;
            float step = dt / FadeSeconds;
            return MathF.Abs(to - weight) <= step ? to : weight + MathF.Sign(to - weight) * step;
        }
    }

    /// <summary>
    /// Learns the player's reach from controller distances to the avatar's shoulder: the longest distance held for
    /// <see cref="HoldSeconds"/> (the minimum of a held span), so a one-frame pop, warp or respawn cannot pin it at
    /// the cap for the session. Invalid spans (the caller's view is settling after a warp, spawn or takeover),
    /// non-finite samples, implausible distances and overhead reaches (from the scout's low shoulder a hand overhead
    /// looks about 1 m long) restart the span and teach nothing.
    /// </summary>
    public sealed class ReachLearner
    {
        public const float HoldSeconds = 0.2f;

        /// <summary>Rise over distance above which a reach counts as overhead (30 degrees above the shoulder).</summary>
        public const float MaxRise = 0.5f;

        /// <summary>The learned reach (m), 0 until a span has been held.</summary>
        public float Reach { get; private set; }

        float _spanMin = float.MaxValue;
        float _spanTime;

        /// <param name="distance">Controller to avatar shoulder (m).</param>
        /// <param name="rise">How far the controller is above the shoulder (m).</param>
        /// <param name="valid">The caller's own gates: upright, empty hand, body at rest, view settled.</param>
        public void Sample(float distance, float rise, float avatarReach, bool valid, float dt)
        {
            float max = avatarReach * ArmReach.BentReach * ArmReach.MaxPlayer;
            if (!valid || !(distance <= max) || !(rise <= MaxRise * distance) || !(dt > 0f))
            {
                _spanMin = float.MaxValue;
                _spanTime = 0f;
                return;
            }
            _spanMin = MathF.Min(_spanMin, distance);
            _spanTime += dt;
            if (_spanTime < HoldSeconds)
                return;
            Reach = MathF.Max(Reach, _spanMin);
            _spanMin = float.MaxValue;
            _spanTime = 0f;
        }
    }
}
