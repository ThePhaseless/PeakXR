using System;
using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>
    /// Placement math for the VR rig's tracking origin. Yaw follows Unity's convention: positive turns
    /// right when seen from above, and yaw 90 maps forward (+Z) to right (+X).
    /// </summary>
    public static class RigMath
    {
        const float Deg2Rad = MathF.PI / 180f;

        /// <summary>Rotates <paramref name="v"/> about +Y by <paramref name="yawDeg"/> degrees.</summary>
        public static Vector3 RotateYaw(float yawDeg, Vector3 v)
        {
            float r = yawDeg * Deg2Rad;
            float c = MathF.Cos(r);
            float s = MathF.Sin(r);
            return new Vector3(v.X * c + v.Z * s, v.Y, -v.X * s + v.Z * c);
        }

        /// <summary>
        /// World position of the tracking origin so that a head standing on the calibrated spot
        /// (<paramref name="centerXZ"/> in tracking space) at <paramref name="eyeHeight"/> lands exactly on
        /// <paramref name="eyeAnchor"/>, with the tracking space turned by <paramref name="yawDeg"/>.
        /// </summary>
        public static Vector3 OriginPosition(
            Vector3 eyeAnchor,
            float yawDeg,
            Vector2 centerXZ,
            float eyeHeight
        ) =>
            eyeAnchor
            - RotateYaw(yawDeg, new Vector3(centerXZ.X, 0f, centerXZ.Y))
            - new Vector3(0f, eyeHeight, 0f);

        /// <summary>
        /// New calibrated spot after turning the rig by <paramref name="deltaYawDeg"/>, chosen so the
        /// head (at <paramref name="headXZ"/> in tracking space) keeps its world position: turning pivots
        /// around the player's head instead of the room centre.
        /// </summary>
        public static Vector2 PivotCenter(Vector2 headXZ, Vector2 centerXZ, float deltaYawDeg)
        {
            Vector3 offset = RotateYaw(
                -deltaYawDeg,
                new Vector3(headXZ.X - centerXZ.X, 0f, headXZ.Y - centerXZ.Y)
            );
            return new Vector2(headXZ.X - offset.X, headXZ.Y - offset.Z);
        }

        /// <summary>Rig yaw that makes the headset's current heading match the body's heading.</summary>
        public static float RecenterYaw(float bodyYawDeg, float hmdLocalYawDeg) =>
            LookMath.WrapDegrees(bodyYawDeg - hmdLocalYawDeg);

        /// <summary>
        /// Tracking-space eye height to map onto the avatar's eyes. With a floor-level tracking space the
        /// measured height goes through <see cref="HeightCalibration"/> (plausibility check and seated
        /// defaults). A device-relative space starts at the head, so its Y is used as is (NaN counts as 0).
        /// </summary>
        public static float EyeHeight(bool floorTracking, HeightMode mode, float hmdTrackingY)
        {
            if (!floorTracking)
                return float.IsNaN(hmdTrackingY) ? 0f : hmdTrackingY;
            const float reference = HeightCalibration.DefaultStandingEyeHeight;
            return reference - HeightCalibration.YOffset(mode, hmdTrackingY, reference);
        }

        /// <summary>
        /// <c>VRRig.HeightOffset</c> from a recenter: metres added to tracking-space Y so the measured eye
        /// height (see <see cref="EyeHeight"/>) lands on the avatar's standing eye height.
        /// </summary>
        public static float HeightOffset(bool floorTracking, HeightMode mode, float hmdTrackingY) =>
            HeightCalibration.DefaultStandingEyeHeight
            - EyeHeight(floorTracking, mode, hmdTrackingY);

        /// <summary>Tracking-space Y that maps onto the avatar's eyes for a given height offset.</summary>
        public static float TrackedEyeHeight(float heightOffset) =>
            HeightCalibration.DefaultStandingEyeHeight - heightOffset;

        /// <summary>
        /// Where the avatar's eyes sit relative to its torso, in the body's facing frame (yaw
        /// <paramref name="facingYawDeg"/>): X metres forward, Y metres up. Sideways is dropped (the eyes are centred).
        /// </summary>
        public static Vector2 EyeOffset(Vector3 eye, Vector3 center, float facingYawDeg)
        {
            Vector3 local = RotateYaw(-facingYawDeg, eye - center);
            return new Vector2(local.Z, local.Y);
        }

        /// <summary>
        /// First-person eye point: <paramref name="offset"/> (see <see cref="EyeOffset"/>) in front of and above the
        /// torso along the rig heading <paramref name="yawDeg"/>. The rig heading, not the head's, because the
        /// tracked head already carries its own swing about the neck.
        /// </summary>
        public static Vector3 EyeAnchor(Vector3 center, float yawDeg, Vector2 offset) =>
            center + RotateYaw(yawDeg, new Vector3(0f, offset.Y, offset.X));

        /// <summary>
        /// The shift added to <see cref="EyeAnchor"/> after the rig turns from <paramref name="fromYawDeg"/> to
        /// <paramref name="toYawDeg"/>: a turn pivots on the eye, so the eye stays put while the body (and the eye's
        /// <paramref name="forward"/> offset) swings under it. Horizontal only; a full circle returns the shift.
        /// </summary>
        public static Vector3 TurnShift(
            Vector3 shift,
            float fromYawDeg,
            float toYawDeg,
            float forward
        ) =>
            shift
            - RotateYaw(toYawDeg, new Vector3(0f, 0f, forward))
            + RotateYaw(fromYawDeg, new Vector3(0f, 0f, forward));

        /// <summary>
        /// Rate (1/s) at which a <see cref="TurnShift"/> glides back onto the avatar's eyes: only while the player
        /// moves (<paramref name="moveInput"/> 0..1, stick or room-scale walking), where their own motion masks it.
        /// Standing still it is 0: a drift nobody asked for is vection.
        /// </summary>
        public static float TurnSettleRate(float moveInput, float rate) =>
            moveInput > 0.1f ? rate * MathF.Min(moveInput, 1f) : 0f;

        /// <summary>
        /// The shift that keeps the eye's level position while the body eye drifts from <paramref name="from"/> to
        /// <paramref name="to"/>: after a turn the ragdoll torso swings onto its new heading over several frames, and
        /// that swing must not move the view either. Height still follows the body.
        /// </summary>
        public static Vector3 HoldLevel(Vector3 shift, Vector3 from, Vector3 to) =>
            new Vector3(shift.X + from.X - to.X, 0f, shift.Z + from.Z - to.Z);

        /// <summary>Clear space around a watched character's head and torso centres that the eye never enters.</summary>
        public const float HeadClearance = 0.35f;
        public const float TorsoClearance = 0.55f;

        /// <summary>Ground further below the eye than this (or none) leaves a detached view floating over a void.</summary>
        public const float FloorCueDrop = 8f;

        /// <summary>
        /// Moves <paramref name="eye"/> out of a watched character's body: the union of a sphere of
        /// <see cref="HeadClearance"/> round <paramref name="head"/> and one of <see cref="TorsoClearance"/> round
        /// <paramref name="torso"/>, pushed square to the torso-head line (along the offset it already has), so a
        /// view that follows someone never has their head or body inside the eyes. Unchanged when already clear.
        /// </summary>
        public static Vector3 ClearOfBody(Vector3 eye, Vector3 head, Vector3 torso)
        {
            var axis = head - torso;
            float len = axis.Length();
            float t =
                len > 1e-4f ? Math.Clamp(Vector3.Dot(eye - torso, axis) / (len * len), 0f, 1f) : 0f;
            var closest = torso + axis * t;
            float fromTorso = t * len;
            float fromHead = (1f - t) * len;
            // Distance from the line at which both spheres are cleared.
            float r = MathF.Max(
                MathF.Sqrt(MathF.Max(0f, TorsoClearance * TorsoClearance - fromTorso * fromTorso)),
                MathF.Sqrt(MathF.Max(0f, HeadClearance * HeadClearance - fromHead * fromHead))
            );
            var off = eye - closest;
            float d = off.Length();
            if (d >= r)
                return eye;
            Vector3 dir;
            if (d > 1e-4f)
                dir = off / d;
            else
            {
                // Dead centre: any direction square to the body.
                var along = len > 1e-4f ? axis / len : Vector3.UnitY;
                dir = Vector3.Cross(along, Vector3.UnitX);
                if (dir.LengthSquared() < 1e-6f)
                    dir = Vector3.Cross(along, Vector3.UnitZ);
                dir = Vector3.Normalize(dir);
            }
            return closest + dir * r;
        }

        /// <summary>A detached view needs a floor cue: no ground within <see cref="FloorCueDrop"/> below the eye
        /// (<paramref name="groundBelow"/> negative when the ray hit nothing).</summary>
        public static bool NeedsFloorCue(float groundBelow) =>
            groundBelow < 0f || groundBelow > FloorCueDrop;

        /// <summary>
        /// Chase-view point <paramref name="distance"/> metres behind <paramref name="target"/> along the rig's
        /// heading and <paramref name="height"/> metres above it. Uses the rig yaw, never the target's, so the
        /// view does not rotate on its own.
        /// </summary>
        public static Vector3 ThirdPersonPoint(
            Vector3 target,
            float yawDeg,
            float distance,
            float height
        ) =>
            target
            + RotateYaw(yawDeg, new Vector3(0f, 0f, -distance))
            + new Vector3(0f, height, 0f);

        /// <summary>
        /// <paramref name="to"/> pulled toward <paramref name="from"/> so it stops <paramref name="margin"/> metres
        /// short of a wall hit <paramref name="hitDistance"/> metres along the line (infinity: no wall). Keeps the
        /// chase view on the watched body's side of rock.
        /// </summary>
        public static Vector3 PullIn(Vector3 from, Vector3 to, float hitDistance, float margin)
        {
            float length = Vector3.Distance(from, to);
            if (!(hitDistance < length) || length < 1e-6f)
                return to;
            return from + (to - from) * (MathF.Max(0f, hitDistance - margin) / length);
        }

        /// <summary>
        /// Whether a still cutscene viewpoint fades and re-places itself at the cutscene camera. A cut (the camera
        /// jumped more than 3 m this frame) re-places at once; a shot that glided more than 3 m away re-places only
        /// after a 3 s dwell, so a camera that keeps moving cannot strobe the view black.
        /// </summary>
        public static bool ReplaceStillView(float frameJump, float drift, float sinceSwitch) =>
            frameJump > 3f || (drift > 3f && sinceSwitch >= 3f);

        /// <summary>
        /// Frame-rate independent exponential follow with separate horizontal and vertical rates
        /// (1/s; 0 = hold, negative = snap), then clamped so the result never lags more than
        /// <paramref name="maxLag"/> metres behind the target.
        /// </summary>
        public static Vector3 Follow(
            Vector3 current,
            Vector3 target,
            float dt,
            float horizontalRate,
            float verticalRate,
            float maxLag
        )
        {
            float h = Blend(horizontalRate, dt);
            float v = Blend(verticalRate, dt);
            var p = new Vector3(
                current.X + (target.X - current.X) * h,
                current.Y + (target.Y - current.Y) * v,
                current.Z + (target.Z - current.Z) * h
            );
            Vector3 lag = p - target;
            float d = lag.Length();
            if (maxLag >= 0f && d > maxLag)
                p = target + lag * (maxLag / d);
            return p;
        }

        /// <summary>Vertical follow rate (1/s) of the first-person eye anchor on the ground: the torso is a driven
        /// rigidbody that bobs at step rate (about 2 Hz), which a 20/s follow passes almost whole. Jumps and falls
        /// keep the full rate.</summary>
        public const float GroundedVerticalRate = 5f;

        /// <summary>While room-scale walks the body under a still head (a follow and its settle), the view's height
        /// holds: the walk's stride bob is not the player's (run 39: 0.045 m within the far lean). Slow enough to
        /// filter a 2 Hz bob, quick enough to reach a new ground height within a couple of seconds.</summary>
        public const float FollowVerticalRate = 0.5f;

        /// <summary>The eye anchor's vertical follow rate for the view's own <paramref name="rate"/>.</summary>
        public static float ViewVerticalRate(float rate, bool grounded, bool followWalking) =>
            !grounded ? rate
            : followWalking ? MathF.Min(rate, FollowVerticalRate)
            : MathF.Min(rate, GroundedVerticalRate);

        static float Blend(float rate, float dt)
        {
            if (rate < 0f)
                return 1f;
            if (rate == 0f || dt <= 0f)
                return 0f;
            return 1f - MathF.Exp(-rate * dt);
        }
    }
}

namespace PeakXR.Core
{
    /// <summary>
    /// Level mass centre of a ragdoll: the point the first-person view rides. Under control the game's body parts are
    /// slippery and only pull on each other (drives with equal and opposite reactions) while drag damps each one, so
    /// bending, leaning or swinging the arms cannot move this point; only walking, pushes and slopes do. A single
    /// part does move: bending forward pushes the hips back and carries the torso forward.
    /// </summary>
    public struct MassCentre
    {
        Vector3 _sum;
        float _mass;

        public void Add(Vector3 position, float mass)
        {
            _sum += position * mass;
            _mass += mass;
        }

        /// <summary>The mass centre's X and Z at <paramref name="height"/>; the origin's when nothing was added.</summary>
        public readonly Vector3 Root(float height)
        {
            var c = _mass > 0f ? _sum / _mass : Vector3.Zero;
            return new Vector3(c.X, height, c.Z);
        }
    }
}
