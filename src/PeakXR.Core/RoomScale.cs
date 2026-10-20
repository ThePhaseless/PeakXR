using System;
using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>Yaw math on horizontal vectors stored as (x, z), in Unity's convention: positive yaw turns
    /// +Z (forward) toward +X (right). Stick vectors (x right, y forward) use the same layout.</summary>
    public static class YawMath
    {
        public static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * UnityQuat.Deg2Rad;
            float c = MathF.Cos(r),
                s = MathF.Sin(r);
            return new Vector2(v.X * c + v.Y * s, -v.X * s + v.Y * c);
        }

        /// <summary>Yaw in degrees of a horizontal direction; 0 for a zero vector.</summary>
        public static float Yaw(Vector2 v) => MathF.Atan2(v.X, v.Y) * UnityQuat.Rad2Deg;
    }

    /// <summary>
    /// Room-scale walking for a physics body that cannot be teleported. The camera feature keeps a tracking-space
    /// <see cref="Anchor"/> over the character, so physically walking moves the view exactly like the real head
    /// (no artificial camera motion). When the head leaves <see cref="LeanRadius"/> around the anchor, this asks
    /// for stick input that walks the body after the head; as the body arrives, the anchor slides toward the head
    /// by the same distance, so the view stays world-fixed while the body catches up underneath it. When the body
    /// cannot follow (blocked, climbing) and the head drifts past <see cref="RecenterDistance"/>, the caller fades
    /// out and calls <see cref="Recenter"/>.
    /// </summary>
    public sealed class RoomScaleFollow
    {
        /// <summary>Head offset (m) the player can lean without moving the body.</summary>
        public float LeanRadius = 0.2f;

        /// <summary>Distance past the lean radius (m) over which the requested input ramps to full.</summary>
        public float RampDistance = 0.3f;

        /// <summary>Offset (m) at which the anchor should be snapped back under a fade.</summary>
        public float RecenterDistance = 1f;

        /// <summary>Tracking-space (x, z) point kept over the character's body.</summary>
        public Vector2 Anchor;

        public Vector2 Offset { get; private set; }

        public bool RecenterNeeded => Offset.Length() > RecenterDistance;

        /// <param name="head">HMD position in tracking space (x, z).</param>
        /// <param name="bodyDelta">The body's horizontal movement since the last update, in tracking space.</param>
        /// <param name="canFollow">The body is free to walk: grounded, conscious, not climbing and the move stick
        /// idle. Otherwise the offset is kept and nothing is requested.</param>
        /// <returns>Requested move input in tracking space, length 0..1.</returns>
        public Vector2 Update(Vector2 head, Vector2 bodyDelta, bool canFollow)
        {
            var d = head - Anchor;
            float dist = d.Length();
            if (canFollow && dist > 1e-5f)
            {
                // Absorb only body motion toward the head, and never past it; pushes the other way still move the view.
                var dir = d / dist;
                float along = Vector2.Dot(bodyDelta, dir);
                if (along > 0f)
                    Anchor += dir * MathF.Min(along, dist);
                d = head - Anchor;
                dist = d.Length();
            }
            Offset = d;

            if (!canFollow || dist <= LeanRadius)
                return Vector2.Zero;
            float amount = RampDistance > 0f ? (dist - LeanRadius) / RampDistance : 1f;
            return d / dist * MathF.Min(amount, 1f);
        }

        public void Recenter(Vector2 head)
        {
            Anchor = head;
            Offset = Vector2.Zero;
        }

        /// <summary>
        /// The anchor after the rig yaws by <paramref name="degrees"/> around the head, so the head's world
        /// position does not move: world = body + R(yaw) * (p - anchor) must stay equal at p = head.
        /// </summary>
        public static Vector2 AnchorAfterYaw(Vector2 anchor, Vector2 head, float degrees) =>
            head - YawMath.Rotate(head - anchor, -degrees);
    }
}
