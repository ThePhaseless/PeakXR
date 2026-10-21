using System;
using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>
    /// Size and tilt of the wrist status display (stamina, afflictions). Lying flat on the forearm it is seen edge-on,
    /// a sliver; so it keeps a fixed angular size at any reading distance and turns toward the eye when looked at or
    /// when an alert pulses.
    /// </summary>
    public static class WristHud
    {
        /// <summary>Eye distance at which the configured width applies (a forearm raised to read a watch).</summary>
        public const float ReadDistance = 0.4f;

        const float MinDistance = 0.2f;
        const float MaxDistance = 0.7f;

        /// <summary>Extra width at the peak of an alert pulse.</summary>
        public const float PulseGrow = 0.2f;

        /// <summary>Gaze within this many degrees of the display: it faces the eye fully.</summary>
        public const float LookDegrees = 20f;

        /// <summary>Gaze beyond this many degrees: it rests at <see cref="RestFacing"/>.</summary>
        public const float AwayDegrees = 45f;

        /// <summary>Share of the turn toward the eye while not looked at: still readable from the corner of the eye.</summary>
        public const float RestFacing = 0.5f;

        /// <summary>Out of the back of the wrist (grip-space x), metres: clear of the game's chunky forearm.</summary>
        public const float DorsalOffset = 0.06f;

        /// <summary>From the grip back toward the elbow (grip-space -z), metres: over the wrist, not the hand.</summary>
        public const float WristBack = 0.12f;

        /// <summary>
        /// Centre of the display in the world: on the back of the wrist, the side that faces the eye when the wrist
        /// is raised to read a watch. Grip space is +z toward the fingers, +y up out of the fist; the back of the left
        /// hand is -x and of the right hand +x.
        /// </summary>
        public static Vector3 Anchor(Vector3 hand, Quaternion handRotation, bool left) =>
            hand
            + UnityQuat.Rotate(
                handRotation,
                new Vector3(left ? -DorsalOffset : DorsalOffset, 0f, -WristBack)
            );

        /// <summary>
        /// Rotation of the display lying on the back of the wrist like a watch face: seen from outside the wrist, text
        /// up toward the fingers, so the bar runs across the wrist and reads left to right on either hand raised to the
        /// eye.
        /// </summary>
        public static Quaternion Rest(Quaternion handRotation, bool left) =>
            UnityQuat.Mul(
                handRotation,
                UnityQuat.LookRotation(new Vector3(left ? 1f : -1f, 0f, 0f), Vector3.UnitZ)
            );

        /// <summary>Width in metres: <paramref name="baseWidth"/> at <see cref="ReadDistance"/>, the same angle nearer
        /// or farther (within a forearm's reach), grown by an alert <paramref name="pulse"/> 0..1.</summary>
        public static float Width(float baseWidth, float eyeDistance, float pulse)
        {
            float d = Math.Clamp(eyeDistance, MinDistance, MaxDistance);
            return baseWidth * d / ReadDistance * (1f + PulseGrow * Math.Clamp(pulse, 0f, 1f));
        }

        /// <summary>How far (0..1) the display turns from lying on the arm toward facing the eye, from the angle
        /// between the gaze and the display and the alert pulse 0..1.</summary>
        public static float Facing(float gazeDegrees, float pulse)
        {
            float t = Math.Clamp((AwayDegrees - gazeDegrees) / (AwayDegrees - LookDegrees), 0f, 1f);
            float look = t * t * (3f - 2f * t);
            return Math.Max(
                Math.Max(RestFacing, RestFacing + (1f - RestFacing) * look),
                Math.Clamp(pulse, 0f, 1f)
            );
        }
    }
}
