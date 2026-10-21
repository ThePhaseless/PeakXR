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
    /// for stick input that walks the body after the head until the head is back inside <see cref="StopRadius"/>; as
    /// the body arrives, the anchor slides toward the head by the same distance, so the view stays world-fixed while
    /// the body catches up underneath it. Inside the lean radius the avatar's upper body leans instead (avatar.local),
    /// and the follow stops well inside it, so standing up after a lean or swaying after a step never walks the body
    /// back and forth. When the body
    /// cannot follow (blocked, climbing) and the head drifts past <see cref="RecenterDistance"/>, the caller fades
    /// out and calls <see cref="Recenter"/>.
    /// </summary>
    public sealed class RoomScaleFollow
    {
        /// <summary>Head offset (m) the player can lean without moving the body: about as far as a standing person
        /// leans over a table. A smaller radius walked the character on every lean and back on standing up.</summary>
        public float LeanRadius = 0.35f;

        /// <summary>A started follow walks until the head is this close (m) to the anchor: half the lean radius, so a
        /// lean past the limit and back up stays inside it instead of walking the body back.</summary>
        public float StopRadius => LeanRadius * 0.5f;

        /// <summary>Distance past the stop radius (m) over which the requested input ramps to full.</summary>
        public float RampDistance = 0.3f;

        /// <summary>Offset (m) at which the anchor should be snapped back under a fade.</summary>
        public float RecenterDistance = 1f;

        /// <summary>Tracking-space (x, z) point kept over the character's body.</summary>
        public Vector2 Anchor;

        public Vector2 Offset { get; private set; }

        float _absorbed;
        bool _following;

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
                // Absorb the body's net travel toward the head, never past it: a step's back-swing returns what its
                // forward swing took, so gait sway cannot ratchet the anchor (and the view) toward the head. Below
                // the start point, pushes away from the head still move the view.
                var dir = d / dist;
                float along = Math.Clamp(Vector2.Dot(bodyDelta, dir), -_absorbed, dist);
                Anchor += dir * along;
                _absorbed += along;
                d = head - Anchor;
                dist = d.Length();
            }
            Offset = d;
            if (!canFollow)
                _absorbed = 0f;

            // Hysteresis: start past the lean radius, stop only well inside it.
            _following = canFollow && (_following ? dist > StopRadius : dist > LeanRadius);
            if (!_following)
                return Vector2.Zero;
            float amount = RampDistance > 0f ? (dist - StopRadius) / RampDistance : 1f;
            return d / dist * MathF.Min(amount, 1f);
        }

        public void Recenter(Vector2 head)
        {
            Anchor = head;
            Offset = Vector2.Zero;
            _absorbed = 0f;
            _following = false;
        }

        /// <summary>
        /// The anchor after the rig yaws by <paramref name="degrees"/> around the head, so the head's world
        /// position does not move: world = body + R(yaw) * (p - anchor) must stay equal at p = head.
        /// </summary>
        public static Vector2 AnchorAfterYaw(Vector2 anchor, Vector2 head, float degrees) =>
            head - YawMath.Rotate(head - anchor, -degrees);
    }

    /// <summary>
    /// Sequences a room-scale recenter under a fade, so the view never jumps in sight: the fade target goes black,
    /// and after <see cref="FadeTime"/> <see cref="Update"/> returns true once (snap the anchor then) and the target
    /// clears. A started fade always ends in a recenter.
    /// </summary>
    public sealed class RecenterFade
    {
        /// <summary>Seconds the screen gets to go dark before the snap.</summary>
        public float FadeTime = 0.25f;

        float _timer = -1f;

        /// <summary>Fade request: 1 = black, 0 = clear.</summary>
        public float Target => _timer >= 0f ? 1f : 0f;

        /// <param name="needed">The offset is past the recenter distance.</param>
        /// <returns>True on the frame to recenter.</returns>
        public bool Update(bool needed, float dt)
        {
            if (_timer < 0f)
            {
                if (needed)
                    _timer = 0f;
                return false;
            }
            _timer += dt;
            if (_timer < FadeTime)
                return false;
            _timer = -1f;
            return true;
        }

        public void Reset() => _timer = -1f;
    }
}
