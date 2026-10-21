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
