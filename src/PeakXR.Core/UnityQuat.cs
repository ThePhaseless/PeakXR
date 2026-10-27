using System;
using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>
    /// Quaternion helpers that reproduce Unity's conventions with System.Numerics types.
    /// Unity is left-handed, +Y up, +Z forward, +X right. The component formulas are identical to
    /// Unity's: <c>Quaternion.AngleAxis</c> == <see cref="Quaternion.CreateFromAxisAngle"/>, and
    /// <c>q * v</c> == <see cref="Vector3.Transform(Vector3, Quaternion)"/>.
    /// </summary>
    public static class UnityQuat
    {
        public const float Deg2Rad = MathF.PI / 180f;
        public const float Rad2Deg = 180f / MathF.PI;

        static readonly Vector3 Forward = new Vector3(0f, 0f, 1f);
        static readonly Vector3 Up = new Vector3(0f, 1f, 0f);
        static readonly Vector3 Right = new Vector3(1f, 0f, 0f);

        /// <summary>Unity's <c>a * b</c>: rotation <paramref name="b"/> is applied first, then <paramref name="a"/>.</summary>
        public static Quaternion Mul(Quaternion a, Quaternion b) => Quaternion.Multiply(a, b);

        /// <summary>Unity's <c>q * v</c>.</summary>
        public static Vector3 Rotate(Quaternion q, Vector3 v) => Vector3.Transform(v, q);

        public static Quaternion Inverse(Quaternion q) => Quaternion.Inverse(q);

        /// <summary>Unity's <c>Quaternion.Euler(x, y, z)</c> in degrees: applies Z, then X, then Y.</summary>
        public static Quaternion Euler(float x, float y, float z)
        {
            var qx = Quaternion.CreateFromAxisAngle(Right, x * Deg2Rad);
            var qy = Quaternion.CreateFromAxisAngle(Up, y * Deg2Rad);
            var qz = Quaternion.CreateFromAxisAngle(Forward, z * Deg2Rad);
            return Mul(Mul(qy, qx), qz);
        }

        /// <summary>Heading-only rotation about +Y that keeps the yaw of <paramref name="q"/>.</summary>
        public static Quaternion YawOnly(Quaternion q) =>
            Quaternion.CreateFromAxisAngle(Up, HeadingRadians(q));

        /// <summary>
        /// Yaw of the rotation's forward axis, in radians, 0 at +Z and positive toward +X.
        /// When looking straight up or down, the head's up axis gives the heading.
        /// </summary>
        internal static float HeadingRadians(Quaternion q)
        {
            Vector3 f = Rotate(q, Forward);
            float hx = f.X,
                hz = f.Z;
            if (hx * hx + hz * hz < 1e-6f)
            {
                Vector3 u = Rotate(q, Up);
                float s = f.Y > 0f ? -1f : 1f;
                hx = u.X * s;
                hz = u.Z * s;
            }
            return MathF.Atan2(hx, hz);
        }

        /// <summary>Unity's <c>Quaternion.LookRotation(forward, up)</c>.</summary>
        public static Quaternion LookRotation(Vector3 forward, Vector3 up)
        {
            float flen = forward.Length();
            if (flen < 1e-6f)
                return Quaternion.Identity;
            Vector3 z = forward / flen;
            Vector3 x = Vector3.Cross(up, z);
            float xlen = x.Length();
            if (xlen < 1e-6f)
                return FromTo(Forward, z);
            x /= xlen;
            Vector3 y = Vector3.Cross(z, x);
            return FromBasis(x, y, z);
        }

        static Quaternion FromTo(Vector3 from, Vector3 to)
        {
            float d = Vector3.Dot(from, to);
            if (d < -0.999999f)
                return Quaternion.CreateFromAxisAngle(Up, MathF.PI);
            Vector3 c = Vector3.Cross(from, to);
            return Quaternion.Normalize(new Quaternion(c, 1f + d));
        }

        /// <summary>Rotation whose columns are the orthonormal basis x, y, z.</summary>
        static Quaternion FromBasis(Vector3 x, Vector3 y, Vector3 z)
        {
            float m00 = x.X,
                m01 = y.X,
                m02 = z.X;
            float m10 = x.Y,
                m11 = y.Y,
                m12 = z.Y;
            float m20 = x.Z,
                m21 = y.Z,
                m22 = z.Z;
            float trace = m00 + m11 + m22;
            Quaternion q;
            if (trace > 0f)
            {
                float s = MathF.Sqrt(trace + 1f) * 2f;
                q = new Quaternion((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, 0.25f * s);
            }
            else if (m00 > m11 && m00 > m22)
            {
                float s = MathF.Sqrt(1f + m00 - m11 - m22) * 2f;
                q = new Quaternion(0.25f * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s);
            }
            else if (m11 > m22)
            {
                float s = MathF.Sqrt(1f + m11 - m00 - m22) * 2f;
                q = new Quaternion((m01 + m10) / s, 0.25f * s, (m12 + m21) / s, (m02 - m20) / s);
            }
            else
            {
                float s = MathF.Sqrt(1f + m22 - m00 - m11) * 2f;
                q = new Quaternion((m02 + m20) / s, (m12 + m21) / s, 0.25f * s, (m10 - m01) / s);
            }
            return Quaternion.Normalize(q);
        }
    }
}
