using System;
using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>
    /// Upper-body lean toward the tracked head. The avatar's spine swings about a pivot low in the back so the eyes
    /// move onto the line from the pivot to the head, as far as a natural bend allows. Leaning over a table then bends
    /// the avatar and carries its shoulders (and the arms' reach) with the head, instead of leaving the arms short of
    /// the controllers or walking the whole character (room-scale only follows past its lean radius).
    /// </summary>
    public static class BodyLean
    {
        /// <summary>Limits of the bend away from the animated pose, in degrees.</summary>
        public const float MaxForwardDeg = 40f;
        public const float MaxBackDeg = 15f;
        public const float MaxSideDeg = 25f;

        const float Rad2Deg = 180f / MathF.PI;
        const float Deg2Rad = MathF.PI / 180f;

        /// <summary>
        /// World rotation about <paramref name="pivot"/> that swings <paramref name="eye"/> toward
        /// <paramref name="head"/>: forward/back pitch and sideways roll in the body frame
        /// (<paramref name="forward"/>, <paramref name="up"/>), each clamped to its limit. Distance along the line
        /// (ducking) is left to the crouch. Identity when the head is on the eye's line.
        /// </summary>
        public static Quaternion Swing(
            Vector3 pivot,
            Vector3 eye,
            Vector3 head,
            Vector3 forward,
            Vector3 up
        )
        {
            TrySwing(pivot, eye, head, forward, up, out var q);
            return q;
        }

        /// <summary>
        /// <see cref="Swing"/>, false (with identity) when the eye or the head is not above the pivot: lying down or a
        /// deep duck has no lean, and the caller fades out the last one.
        /// </summary>
        public static bool TrySwing(
            Vector3 pivot,
            Vector3 eye,
            Vector3 head,
            Vector3 forward,
            Vector3 up,
            out Quaternion swing
        )
        {
            swing = Quaternion.Identity;
            var right = Vector3.Cross(up, forward);
            var from = eye - pivot;
            var to = head - pivot;
            float fu = Vector3.Dot(from, up);
            float tu = Vector3.Dot(to, up);
            if (fu <= 1e-4f || tu <= 1e-4f)
                return false;
            float pitch0 = MathF.Atan2(Vector3.Dot(from, forward), fu) * Rad2Deg;
            float roll0 = MathF.Atan2(Vector3.Dot(from, right), fu) * Rad2Deg;
            float pitch =
                pitch0
                + Math.Clamp(
                    MathF.Atan2(Vector3.Dot(to, forward), tu) * Rad2Deg - pitch0,
                    -MaxBackDeg,
                    MaxForwardDeg
                );
            float roll =
                roll0
                + Math.Clamp(
                    MathF.Atan2(Vector3.Dot(to, right), tu) * Rad2Deg - roll0,
                    -MaxSideDeg,
                    MaxSideDeg
                );
            var dir = up + MathF.Tan(pitch * Deg2Rad) * forward + MathF.Tan(roll * Deg2Rad) * right;
            swing = FromTo(from, dir);
            return true;
        }

        /// <summary>
        /// Horizontal shift of the legs' animated pose (relative to the hip) that keeps the mass centre over the
        /// planted feet while the upper body bends. The ragdoll pulls every part toward hip + its animated offset
        /// (Bodypart.cs:193-207) and the feet grip, so a bend alone leaves the leg targets behind the feet: friction
        /// then creeps the whole body toward the lean until the hip is back over them (run 28: the mass centre 0.06 m
        /// forward, back again on standing up). With the legs shifted, the hip settles back by this much and the
        /// mass centre stays, as a person's hips go back when they bend. <paramref name="upperMoment"/> is the sum of
        /// mass times animated displacement of the bent parts; <paramref name="legMass"/> the mass that stays planted.
        /// </summary>
        public static Vector3 LegShift(Vector3 upperMoment, float totalMass, float legMass)
        {
            float carried = totalMass - legMass;
            if (carried <= 1e-4f)
                return Vector3.Zero;
            return new Vector3(upperMoment.X, 0f, upperMoment.Z) / carried;
        }

        /// <summary>
        /// What to add to the tracked head so the lean starts from the calibrated eyes: the unbent animated eyes
        /// (<paramref name="eyeFromHip"/>, relative to the animated hip, taken on a physics step) carried onto the
        /// rendered frame's physical <paramref name="hip"/>, minus that frame's <paramref name="calibratedEye"/>.
        /// Both ends on the same frame, so a moving body adds no bias.
        /// </summary>
        public static Vector3 HeadBias(Vector3 eyeFromHip, Vector3 hip, Vector3 calibratedEye) =>
            hip + eyeFromHip - calibratedEye;

        /// <summary>Shortest rotation turning direction <paramref name="a"/> onto <paramref name="b"/>.</summary>
        public static Quaternion FromTo(Vector3 a, Vector3 b)
        {
            float la = a.Length();
            float lb = b.Length();
            if (la < 1e-6f || lb < 1e-6f)
                return Quaternion.Identity;
            a /= la;
            b /= lb;
            var axis = Vector3.Cross(a, b);
            float sin = axis.Length();
            float cos = Vector3.Dot(a, b);
            if (sin < 1e-6f)
                return Quaternion.Identity; // parallel; an exact flip never happens within the limits
            return Quaternion.CreateFromAxisAngle(axis / sin, MathF.Atan2(sin, cos));
        }
    }
}
