using System;
using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>
    /// Turns arm-pulled body motion into the stick input the game's wall climbing reads, so physical climbing moves
    /// through the game's own climb code (speed limits, stamina cost, slides) instead of moving the body directly.
    /// Axes match <c>CharacterClimbing.GetRequestedPostition</c>: Y along the wall's up, X along its right.
    /// </summary>
    public static class ClimbInput
    {
        static readonly Vector3 WorldUp = new Vector3(0f, 1f, 0f);

        /// <param name="bodyVelocity">Wanted body velocity, world space, m/s.</param>
        /// <param name="wallNormal">Climb surface normal (points away from the wall).</param>
        /// <param name="climbSpeed">Game climb speed in m/s at full stick.</param>
        /// <param name="deadzone">Stick magnitude below which the result is zero.</param>
        public static Vector2 FromBodyVelocity(
            Vector3 bodyVelocity,
            Vector3 wallNormal,
            float climbSpeed,
            float deadzone
        )
        {
            if (climbSpeed <= 1e-5f || wallNormal.LengthSquared() < 1e-8f)
                return Vector2.Zero;
            var n = Vector3.Normalize(wallNormal);
            var up = WorldUp - n * Vector3.Dot(WorldUp, n);
            if (up.LengthSquared() < 1e-8f)
                return Vector2.Zero;
            up = Vector3.Normalize(up);
            var right = -Vector3.Normalize(Vector3.Cross(up, n));

            var input =
                new Vector2(Vector3.Dot(bodyVelocity, right), Vector3.Dot(bodyVelocity, up))
                / climbSpeed;
            float len = input.Length();
            if (len < deadzone)
                return Vector2.Zero;
            return len > 1f ? input / len : input;
        }

        /// <summary>Exponential smoothing toward <paramref name="target"/>; rate &lt;= 0 snaps.</summary>
        public static Vector2 Smooth(Vector2 current, Vector2 target, float rate, float dt)
        {
            if (rate <= 0f)
                return target;
            float a = 1f - MathF.Exp(-rate * dt);
            return current + (target - current) * a;
        }

        /// <summary>
        /// Cleans the smoothed arm input before the game reads it. Smoothing never decays exactly to zero, and the
        /// game climbs down at full speed for any negative stick y (CharacterClimbing.cs:303), so a residue would
        /// slide the climber down with the hands still. Tiny components become zero, and y goes negative only while
        /// the arms clearly pull down (<paramref name="target"/> below -0.3).
        /// </summary>
        public static Vector2 Settle(Vector2 smoothed, Vector2 target)
        {
            const float deadzone = 0.05f;
            float x = MathF.Abs(smoothed.X) < deadzone ? 0f : smoothed.X;
            float y = MathF.Abs(smoothed.Y) < deadzone ? 0f : smoothed.Y;
            if (y < 0f && target.Y > -0.3f)
                y = 0f;
            return new Vector2(x, y);
        }

        /// <summary>
        /// Climb lunge from a hand thrown upward as it lets go of the wall: the stick direction (wall right, wall
        /// up; unit length) for the game's climb jump, or zero when the release was not fast enough upward.
        /// </summary>
        /// <param name="releaseVelocity">Hand velocity relative to the body at release, world space, m/s.</param>
        /// <param name="wallNormal">Climb surface normal.</param>
        /// <param name="minUpSpeed">Speed along the wall's up needed to count as a lunge.</param>
        public static Vector2 Lunge(Vector3 releaseVelocity, Vector3 wallNormal, float minUpSpeed)
        {
            var stick = FromBodyVelocity(releaseVelocity, wallNormal, 1f, 0f);
            float len = stick.Length();
            if (len < 1e-5f)
                return Vector2.Zero;
            // FromBodyVelocity clamps to unit length; recover the raw up speed from the unclamped projection.
            var n = Vector3.Normalize(wallNormal);
            var up = WorldUp - n * Vector3.Dot(WorldUp, n);
            if (up.LengthSquared() < 1e-8f)
                return Vector2.Zero;
            float upSpeed = Vector3.Dot(releaseVelocity, Vector3.Normalize(up));
            if (upSpeed < minUpSpeed)
                return Vector2.Zero;
            return stick / len;
        }

        /// <summary>
        /// Walking stick input that moves the character along <paramref name="worldDir"/> (flattened): the
        /// inverse of <c>Character.CalculateWorldMovementDir</c> (Character.cs:1311), which walks along the flat
        /// look direction times stick Y plus look-right times stick X. Zero for a vertical direction.
        /// </summary>
        public static Vector2 WalkStick(Vector3 worldDir, Vector3 lookForward, Vector3 lookRight)
        {
            var dir = new Vector3(worldDir.X, 0f, worldDir.Z);
            var fwd = new Vector3(lookForward.X, 0f, lookForward.Z);
            var right = new Vector3(lookRight.X, 0f, lookRight.Z);
            if (
                dir.LengthSquared() < 1e-8f
                || fwd.LengthSquared() < 1e-8f
                || right.LengthSquared() < 1e-8f
            )
                return Vector2.Zero;
            dir = Vector3.Normalize(dir);
            var stick = new Vector2(
                Vector3.Dot(dir, Vector3.Normalize(right)),
                Vector3.Dot(dir, Vector3.Normalize(fwd))
            );
            float len = stick.Length();
            return len > 1f ? stick / len : stick;
        }
    }
}
