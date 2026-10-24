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

        /// <summary>Sine of the elbow bend (about 6 degrees) below which <see cref="SolveTwoBone"/> treats the arm as
        /// straight and takes its bend plane from the hint.</summary>
        public const float StraightSin = 0.1f;

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

        /// <summary>Sine of the angle between the arm and the hint below which the elbow keeps its last side
        /// (<see cref="StableHint"/>), and above which it follows the hint alone.</summary>
        public const float HoldSin = 0.17f;

        public const float FreeSin = 0.5f;

        /// <summary>
        /// <paramref name="hint"/> (from <see cref="ElbowHint"/>) made stable for an arm pointing along it. The solver
        /// bends the elbow toward the hint's offset off the shoulder-hand line, and an arm hanging down, out and back
        /// lies on that line: millimetres of hand jitter then swing the elbow around the arm. Near the line the bend
        /// keeps <paramref name="bend"/> (last tick's unit bend direction, zero for none; updated), blending back to
        /// the hint by <see cref="FreeSin"/>.
        /// </summary>
        public static Vector3 StableHint(
            Vector3 shoulder,
            Vector3 hand,
            Vector3 hint,
            ref Vector3 bend
        )
        {
            var axis = hand - shoulder;
            var mid = (shoulder + hand) * 0.5f;
            var off = hint - mid;
            float len = off.Length();
            if (axis.LengthSquared() < 1e-8f || len < 1e-5f)
                return hint;
            axis = Vector3.Normalize(axis);
            var perp = off - axis * Vector3.Dot(off, axis);
            var held = bend - axis * Vector3.Dot(bend, axis);
            float w = System.Math.Clamp(
                (perp.Length() / len - HoldSin) / (FreeSin - HoldSin),
                0f,
                1f
            );
            if (held.LengthSquared() < 1e-6f)
                w = 1f;
            var dir =
                (
                    w > 0f && perp.LengthSquared() > 1e-12f
                        ? Vector3.Normalize(perp) * w
                        : Vector3.Zero
                ) + (w < 1f ? Vector3.Normalize(held) * (1f - w) : Vector3.Zero);
            if (dir.LengthSquared() < 1e-8f)
                return hint;
            bend = Vector3.Normalize(dir);
            return mid + bend * HintDistance;
        }

        /// <summary>Unit direction of the elbow off the shoulder-hand line, or zero for a straight arm.</summary>
        public static Vector3 BendDirection(Vector3 shoulder, Vector3 elbow, Vector3 hand)
        {
            var off = BendOffset(shoulder, elbow, hand);
            return off.LengthSquared() > 1e-8f ? Vector3.Normalize(off) : Vector3.Zero;
        }

        /// <summary>The elbow's offset (metres) off the shoulder-hand line: how much the arm is bent, and which way.
        /// A few millimetres is a straight arm whose direction means nothing.</summary>
        public static Vector3 BendOffset(Vector3 shoulder, Vector3 elbow, Vector3 hand)
        {
            var axis = hand - shoulder;
            var off = elbow - shoulder;
            float len2 = axis.LengthSquared();
            if (len2 > 1e-8f)
                off -= axis * (Vector3.Dot(off, axis) / len2);
            return off;
        }

        /// <summary>
        /// Two-bone reach from the arm's current pose: the elbow opens or closes in the arm's own bend plane until the
        /// shoulder-hand distance matches <paramref name="target"/> (straight when out of reach), then the whole arm
        /// turns about the shoulder onto it. Returns world-space deltas: <paramref name="upper"/> for the upper arm,
        /// <paramref name="lower"/> for the forearm (both turns). Keeping the current plane keeps the elbow on the
        /// side the IK and its hint chose; only a straight arm takes the plane from <paramref name="hint"/>. Identity
        /// when the hand is already on target.
        /// </summary>
        public static void SolveTwoBone(
            Vector3 shoulder,
            Vector3 elbow,
            Vector3 hand,
            Vector3 target,
            Vector3 hint,
            out Quaternion upper,
            out Quaternion lower
        )
        {
            upper = lower = Quaternion.Identity;
            var u = elbow - shoulder;
            var v = hand - elbow;
            var st = target - shoulder;
            float a = u.Length();
            float b = v.Length();
            float dist = st.Length();
            if (a < 1e-5f || b < 1e-5f || dist < 1e-5f)
                return;
            var n = Vector3.Cross(u, v);
            var straighten = Quaternion.Identity;
            // Within StraightSin of straight the bend plane is noise (an out-of-reach IK leaves a millimetre kink):
            // straighten the forearm and bend toward the hint instead, or a lean in flips the elbow up or back.
            if (n.Length() < StraightSin * a * b)
            {
                straighten = BodyLean.FromTo(v, u);
                v = u * (b / a);
                n = Vector3.Cross(hint - shoulder, u);
            }
            if (n.LengthSquared() < 1e-10f)
                return;
            n = Vector3.Normalize(n);
            float reach = System.Math.Clamp(dist, System.MathF.Abs(a - b) + 1e-4f, a + b - 1e-4f);
            float interior = System.MathF.Acos(
                System.Math.Clamp((a * a + b * b - reach * reach) / (2f * a * b), -1f, 1f)
            );
            float bend = System.MathF.Acos(System.Math.Clamp(Vector3.Dot(u, v) / (a * b), -1f, 1f));
            var open = Quaternion.CreateFromAxisAngle(n, System.MathF.PI - interior - bend);
            var swing = BodyLean.FromTo(elbow + Vector3.Transform(v, open) - shoulder, st);
            upper = swing;
            lower = Quaternion.Multiply(swing, Quaternion.Multiply(open, straighten));
        }
    }
}
