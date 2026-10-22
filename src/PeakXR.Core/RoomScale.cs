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
    /// back and forth. Standing back up after a follow leaves the body ahead of the head, and it stays there: walking
    /// it back is the forward-and-back motion players reported. Instead, once the head has been still for
    /// <see cref="StillSeconds"/>, that stance eases into <see cref="Neutral"/>, which the avatar's lean treats as
    /// upright; only the avatar's pose changes, never the body or the view. When the body cannot follow (blocked, climbing) and the head drifts past <see cref="RecenterDistance"/>, the caller fades
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

        /// <summary>The head offset (tracking space) that counts as standing upright: zero, or after a follow the
        /// stance the player stood back up into, against the follow's direction. Moves at most
        /// <see cref="NeutralSpeed"/>.</summary>
        public Vector2 Neutral { get; private set; }

        /// <summary>Seconds the head must stay within 3 cm before its stance updates <see cref="Neutral"/>.</summary>
        public float StillSeconds = 0.75f;

        /// <summary>Speed (m/s) at which <see cref="Neutral"/> eases; the avatar straightens over about a second.</summary>
        public float NeutralSpeed = 0.3f;

        float _absorbed;

        /// <summary>How far the anchor moved along the last follow, coasting after it included.</summary>
        float _followTravel;

        /// <summary>Seconds after a follow during which the body is still settling from its walk.</summary>
        public float FollowSettleSeconds = 1.5f;

        /// <summary>Largest body travel (m) in one update that a follow takes into the anchor whole (about 3 m/s at
        /// 20 updates a second); a bigger step is a jump.</summary>
        public float MaxFollowStep = 0.15f;

        float _sinceFollow = float.MaxValue;
        bool _following;
        Vector2 _followDir;
        Vector2 _neutralGoal;
        Vector2 _stillAt;
        float _stillFor;

        /// <summary>A follow that closes less than <see cref="StallProgress"/> m of the offset in this many seconds is
        /// blocked (a wall, a rock, a friend in the way): it gives up and asks for the recentre fade. Before, an offset
        /// between <see cref="LeanRadius"/> and <see cref="RecenterDistance"/> that the body could not close walked it
        /// into the obstacle for as long as the head stayed there.</summary>
        public float StallSeconds = 1.5f;

        public float StallProgress = 0.03f;

        /// <summary>The current follow stalled; cleared by <see cref="Recenter"/>.</summary>
        public bool Stalled { get; private set; }

        float _stallBest;
        float _stallFor;

        public bool RecenterNeeded => Stalled || Offset.Length() > RecenterDistance;

        /// <summary>A follow is walking the body, or it is settling from one (<see cref="FollowSettleSeconds"/>).</summary>
        public bool Walking => _following || _sinceFollow < FollowSettleSeconds;

        /// <param name="head">HMD position in tracking space (x, z).</param>
        /// <param name="bodyDelta">The body's horizontal movement since the last update, in tracking space.</param>
        /// <param name="canFollow">The body is free to walk: grounded, conscious, not climbing and the move stick
        /// idle. Otherwise the offset is kept and nothing is requested.</param>
        /// <param name="dt">Seconds since the last update; 0 never eases <see cref="Neutral"/>.</param>
        /// <returns>Requested move input in tracking space, length 0..1.</returns>
        public Vector2 Update(Vector2 head, Vector2 bodyDelta, bool canFollow, float dt = 0f)
        {
            if (Vector2.Distance(head, _stillAt) > 0.03f)
            {
                _stillAt = head;
                _stillFor = 0f;
            }
            else
                _stillFor += dt;
            if (canFollow)
                Absorb(head, bodyDelta);
            var d = head - Anchor;
            float dist = d.Length();
            Offset = d;
            if (!canFollow)
                _absorbed = 0f;

            // Hysteresis: start past the lean radius, stop only well inside it. Standing back up after a follow
            // points the head back by up to how far the body walked (more when the walk coasted onto the head, run
            // 31): that stance is not a step, so it never starts a walk back. Neutral then makes it upright.
            bool start = !_following;
            if (start)
            {
                var trigger = d;
                float back = -Vector2.Dot(d, _followDir);
                if (back > 0f)
                    trigger += _followDir * MathF.Min(back, _followTravel);
                start = trigger.Length() > LeanRadius;
            }
            _following = canFollow && (_following ? dist > StopRadius : start);
            if (_following)
                _sinceFollow = 0f;
            else if (!canFollow)
                _sinceFollow = float.MaxValue;
            else if (_sinceFollow < float.MaxValue)
                _sinceFollow += dt;
            if (_following)
            {
                if (start || dist < _stallBest - StallProgress)
                {
                    _stallBest = dist;
                    _stallFor = 0f;
                }
                else if ((_stallFor += dt) >= StallSeconds)
                    Stalled = true;
            }
            // The head came back inside the lean: nothing left to recentre, so no fade for a stall the player undid.
            if (Stalled && dist <= LeanRadius)
                Stalled = false;
            if (Stalled)
                _following = false;
            if (_following)
            {
                if (start)
                    _followTravel = 0f;
                _followDir = d / dist;
                _neutralGoal = Vector2.Zero;
            }
            else if (_stillFor >= StillSeconds)
            {
                // Only the part of a still stance that points back against the last follow: a lean held the
                // follow's way (or with no follow at all) stays a lean.
                float back = -Vector2.Dot(d, _followDir);
                _neutralGoal = back > 0f ? -_followDir * back : Vector2.Zero;
            }
            var toGoal = _neutralGoal - Neutral;
            float step = NeutralSpeed * dt;
            Neutral =
                toGoal.Length() <= step ? _neutralGoal : Neutral + Vector2.Normalize(toGoal) * step;

            if (!_following)
                return Vector2.Zero;
            float amount = RampDistance > 0f ? (dist - StopRadius) / RampDistance : 1f;
            return d / dist * MathF.Min(amount, 1f);
        }

        /// <summary>
        /// Absorbs the body's net travel toward the head into <see cref="Anchor"/>, never past it: a step's
        /// back-swing returns what its forward swing took, so gait sway cannot ratchet the anchor (and the view)
        /// toward the head. Below the start point, pushes away from the head still move the view. <see cref="Update"/>
        /// does this itself; the camera calls it in the same frame it moves the eye anchor, so a moving body never
        /// shows as a frame of view motion (run 32: 0.044 m at the end of a follow walk). Only while following is
        /// allowed (the last <see cref="Update"/>'s <c>canFollow</c>).
        /// </summary>
        public void Absorb(Vector2 head, Vector2 bodyDelta)
        {
            var d = head - Anchor;
            float dist = d.Length();
            // While a follow walks and settles, all of the body's travel is the walk: its sideways gait sway (run 35)
            // and its coast past the head and back (run 36: 0.077 m within the far lean, the origin unchanged at
            // every 0.25 s sample). Take it all so the view stays where the head is. A step bigger than a walk's
            // frame is a jump, not a walk.
            if (_sinceFollow < FollowSettleSeconds && bodyDelta.Length() <= MaxFollowStep)
            {
                Anchor += bodyDelta;
                float toward = dist > 1e-5f ? Vector2.Dot(bodyDelta, d / dist) : 0f;
                _absorbed = MathF.Max(0f, _absorbed + toward);
                _followTravel = MathF.Max(0f, _followTravel + Vector2.Dot(bodyDelta, _followDir));
                return;
            }
            if (dist <= 1e-5f)
                return;
            var dir = d / dist;
            float along = Math.Clamp(Vector2.Dot(bodyDelta, dir), -_absorbed, dist);
            Anchor += dir * along;
            _absorbed += along;
            _followTravel += MathF.Max(0f, along * Vector2.Dot(dir, _followDir));
        }

        public void Recenter(Vector2 head)
        {
            Anchor = head;
            Offset = Vector2.Zero;
            _absorbed = 0f;
            _following = false;
            Stalled = false;
            _stallFor = 0f;
            _followDir = Vector2.Zero;
            _followTravel = 0f;
            _neutralGoal = Vector2.Zero;
            Neutral = Vector2.Zero;
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
