using System;
using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>
    /// Rigid-transform helpers for placing held items in tracked hands, in Unity conventions
    /// (see <see cref="UnityQuat"/>). A pose (p, r) maps a local point x to p + r * x.
    /// </summary>
    public static class GripMath
    {
        /// <summary>World pose of a child given its parent's pose and its local pose.</summary>
        public static void Compose(Vector3 parentPos, Quaternion parentRot, Vector3 localPos, Quaternion localRot,
            out Vector3 pos, out Quaternion rot)
        {
            pos = parentPos + UnityQuat.Rotate(parentRot, localPos);
            rot = Quaternion.Normalize(UnityQuat.Mul(parentRot, localRot));
        }

        public static void Inverse(Vector3 pos, Quaternion rot, out Vector3 invPos, out Quaternion invRot)
        {
            invRot = Quaternion.Inverse(rot);
            invPos = -UnityQuat.Rotate(invRot, pos);
        }

        /// <summary>
        /// Where an item sits in a controller's frame so that its grip point (pose relative to the item) lands on the
        /// hand bone (pose relative to the controller): item = bone * grip⁻¹.
        /// </summary>
        public static void ItemInHand(Vector3 gripPos, Quaternion gripRot, Vector3 bonePos, Quaternion boneRot,
            out Vector3 itemPos, out Quaternion itemRot)
        {
            Inverse(gripPos, gripRot, out var ip, out var ir);
            Compose(bonePos, boneRot, ip, ir, out itemPos, out itemRot);
        }

        /// <summary>Reflects a pose across the local YZ plane: right-hand data becomes left-hand data.</summary>
        public static void MirrorX(ref Vector3 pos, ref Quaternion rot)
        {
            pos.X = -pos.X;
            rot = new Quaternion(rot.X, -rot.Y, -rot.Z, rot.W);
        }

        /// <summary>Shortest rotation taking direction <paramref name="from"/> onto <paramref name="to"/>.</summary>
        public static Quaternion FromTo(Vector3 from, Vector3 to)
        {
            float lf = from.Length(), lt = to.Length();
            if (lf < 1e-6f || lt < 1e-6f)
                return Quaternion.Identity;
            from /= lf;
            to /= lt;
            float d = Vector3.Dot(from, to);
            if (d < -0.99999f)
            {
                // Opposite: any perpendicular axis works.
                var axis = Vector3.Cross(new Vector3(1f, 0f, 0f), from);
                if (axis.LengthSquared() < 1e-6f)
                    axis = Vector3.Cross(new Vector3(0f, 1f, 0f), from);
                return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI);
            }
            var c = Vector3.Cross(from, to);
            return Quaternion.Normalize(new Quaternion(c.X, c.Y, c.Z, 1f + d));
        }

        /// <summary>
        /// Turns <paramref name="rot"/> the least amount so its local <paramref name="axisLocal"/> points along
        /// <paramref name="worldDir"/>. Used for two-hand holds: the item's grip-to-grip axis points at the second hand.
        /// </summary>
        public static Quaternion AlignAxis(Quaternion rot, Vector3 axisLocal, Vector3 worldDir)
        {
            if (worldDir.LengthSquared() < 1e-8f || axisLocal.LengthSquared() < 1e-8f)
                return rot;
            var current = UnityQuat.Rotate(rot, axisLocal);
            return Quaternion.Normalize(UnityQuat.Mul(FromTo(current, worldDir), rot));
        }

        /// <summary>Same rotation axis, angle limited to <paramref name="maxDeg"/>.</summary>
        public static Quaternion LimitAngle(Quaternion q, float maxDeg)
        {
            q = Quaternion.Normalize(q);
            if (q.W < 0f)
                q = new Quaternion(-q.X, -q.Y, -q.Z, -q.W);
            float angle = 2f * MathF.Acos(Math.Clamp(q.W, -1f, 1f)) * UnityQuat.Rad2Deg;
            if (angle <= maxDeg || angle < 1e-4f)
                return q;
            return Slerp(Quaternion.Identity, q, maxDeg / angle);
        }

        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) => Quaternion.Slerp(a, b, t);

        /// <summary>
        /// Extra head rotation (world space, applied on top of the animated head) that turns the game's look
        /// direction (yaw/pitch, pitch positive up, no roll) into the tracked head rotation, limited in angle.
        /// </summary>
        public static Quaternion HeadDelta(Quaternion hmdWorld, float yawDeg, float pitchDeg, float maxDeg)
        {
            var look = UnityQuat.Euler(-pitchDeg, yawDeg, 0f);
            var delta = UnityQuat.Mul(hmdWorld, Quaternion.Inverse(look));
            return LimitAngle(delta, maxDeg);
        }
    }
}
