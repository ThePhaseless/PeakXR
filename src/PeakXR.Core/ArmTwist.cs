using System;
using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>
    /// Wrist roll for tracked hands. The arm IK puts the controller's whole rotation on the hand bone and leaves the
    /// forearm at the animation's roll, and the scout rig has no forearm twist bone, so every degree of roll lands on
    /// the one wrist joint: the skin wraps around the wrist, and near a half turn the ragdoll's wrist drive and
    /// alignment torque (Bodypart.cs:175-191) cannot tell which way round to go and wind the arm back and forth.
    /// Here the roll is split into swing and twist about the forearm, the twist is kept continuous across the half
    /// turn, and it is shared between the forearm (rolling about its own axis, which moves neither the elbow nor the
    /// hand) and the wrist, each held to a natural <see cref="MaxWrist"/>.
    /// </summary>
    public static class ArmTwist
    {
        /// <summary>Most twist (radians) either joint takes: a forearm pronates or supinates about this far.</summary>
        public const float MaxWrist = MathF.PI / 2f;

        /// <summary>Share of the twist the forearm takes; the wrist takes the rest.</summary>
        public const float ForearmShare = 0.5f;

        /// <summary>How far (radians) past a half turn the twist keeps its side before it wraps to the other. A hand
        /// held at the half turn would otherwise alternate between rolling the arm one way and the other.</summary>
        public const float WrapHysteresis = MathF.PI / 4f;

        /// <summary>
        /// Swing-twist split of <paramref name="q"/> about unit <paramref name="axis"/>: q = swing * twist, twist about
        /// the axis and swing about an axis perpendicular to it. Takes the shortest path (q and -q give the same
        /// split). A half-turn swing has no defined twist and gets none.
        /// </summary>
        public static void Decompose(
            Quaternion q,
            Vector3 axis,
            out Quaternion swing,
            out Quaternion twist
        )
        {
            if (q.W < 0f)
                q = Quaternion.Negate(q);
            var along = Vector3.Dot(new Vector3(q.X, q.Y, q.Z), axis) * axis;
            twist = new Quaternion(along, q.W);
            float len = twist.Length();
            twist = len < 1e-6f ? Quaternion.Identity : Quaternion.Multiply(twist, 1f / len);
            swing = Quaternion.Multiply(q, Quaternion.Conjugate(twist));
        }

        /// <summary>Signed twist of <paramref name="q"/> about unit <paramref name="axis"/>, radians in [-pi, pi]
        /// (right-hand rule in System.Numerics, Unity's AngleAxis sense).</summary>
        public static float TwistAngle(Quaternion q, Vector3 axis)
        {
            if (q.W < 0f)
                q = Quaternion.Negate(q);
            return 2f * MathF.Atan2(Vector3.Dot(new Vector3(q.X, q.Y, q.Z), axis), q.W);
        }

        /// <summary>The turn equal to <paramref name="angle"/> nearest <paramref name="previous"/>, wrapped to the
        /// other side only once it is more than <see cref="WrapHysteresis"/> past a half turn.</summary>
        public static float Unwrap(float angle, float previous)
        {
            const float Turn = 2f * MathF.PI;
            float a = angle + Turn * MathF.Round((previous - angle) / Turn);
            if (a > MathF.PI + WrapHysteresis)
                a -= Turn;
            else if (a < -MathF.PI - WrapHysteresis)
                a += Turn;
            return a;
        }

        /// <summary>Splits a <paramref name="total"/> twist (radians) into the forearm's roll and the wrist's twist,
        /// each within <see cref="MaxWrist"/>; past twice that the hand stops rolling with the controller.</summary>
        public static void Split(float total, out float forearm, out float wrist)
        {
            forearm = Math.Clamp(total * ForearmShare, -MaxWrist, MaxWrist);
            wrist = Math.Clamp(total - forearm, -MaxWrist, MaxWrist);
        }

        /// <summary>
        /// Shares the wrist roll with the forearm. <paramref name="relative"/> is the hand's rotation off its rest
        /// pose in the forearm's frame (forearm^-1 * hand * rest^-1) and <paramref name="axis"/> the forearm's unit
        /// long axis in that frame. Returns the hand's new relative rotation for a forearm turned by
        /// <paramref name="forearm"/> (forearm' = forearm * <paramref name="forearm"/>): within range, forearm' * result
        /// is the original hand, and the hand always keeps its pointing direction (the swing).
        /// <paramref name="twist"/> carries the unwrapped twist from tick to tick; start it at 0.
        /// </summary>
        public static Quaternion Spread(
            Quaternion relative,
            Vector3 axis,
            ref float twist,
            out Quaternion forearm
        )
        {
            Decompose(relative, axis, out var swing, out var raw);
            twist = Unwrap(TwistAngle(raw, axis), twist);
            Split(twist, out float f, out float w);
            forearm = Quaternion.CreateFromAxisAngle(axis, f);
            // forearm^-1 * swing * forearm is still a swing (turned about the axis), so the hand's twist is w.
            var turned = Quaternion.Multiply(
                Quaternion.Conjugate(forearm),
                Quaternion.Multiply(swing, forearm)
            );
            return Quaternion.Multiply(turned, Quaternion.CreateFromAxisAngle(axis, w));
        }
    }
}
