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
        public static Vector2 FromBodyVelocity(Vector3 bodyVelocity, Vector3 wallNormal, float climbSpeed, float deadzone)
        {
            if (climbSpeed <= 1e-5f || wallNormal.LengthSquared() < 1e-8f)
                return Vector2.Zero;
            var n = Vector3.Normalize(wallNormal);
            var up = WorldUp - n * Vector3.Dot(WorldUp, n);
            if (up.LengthSquared() < 1e-8f)
                return Vector2.Zero;
            up = Vector3.Normalize(up);
            var right = -Vector3.Normalize(Vector3.Cross(up, n));

            var input = new Vector2(Vector3.Dot(bodyVelocity, right), Vector3.Dot(bodyVelocity, up)) / climbSpeed;
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
    }
}
