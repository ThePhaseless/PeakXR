using System;
using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>
    /// Holds the body's horizontal mass centre still while the avatar leans. Bending moves the slippery ragdoll's
    /// parts against the planted feet, and the contacts push the whole body sideways or toward the bend
    /// (run 30: 0.02-0.03 m creep over a second even with <see cref="BodyLean.LegShift"/>, and 0.04 m on standing back
    /// up), which the view rides. A gentle, clamped spring-damper acceleration (applied to every part, so it moves only
    /// the mass centre, never the pose) pulls it back to where the lean started. It lets go for stick or room-scale
    /// input, past <see cref="MaxOffset"/> (a real push or a slide), and <see cref="ReleaseSeconds"/> after the lean.
    /// </summary>
    public sealed class BodyHold
    {
        /// <summary>Spring (1/s^2) and damper (1/s), about critically damped.</summary>
        public float Stiffness = 20f;
        public float Damping = 9f;

        /// <summary>Largest corrective acceleration (m/s^2): enough for the creep, small beside a shove.</summary>
        public float MaxAccel = 3f;

        /// <summary>Drift (m) beyond which the hold re-anchors instead of pulling the body back.</summary>
        public float MaxOffset = 0.1f;

        /// <summary>Seconds the hold lasts after the lean ends: the body creeps while standing back up.</summary>
        public float ReleaseSeconds = 1f;

        public Vector2 Anchor { get; private set; }
        public bool Holding { get; private set; }

        float _sinceLean = float.MaxValue;

        /// <param name="centre">Horizontal mass centre (x, z).</param>
        /// <param name="velocity">Its horizontal velocity.</param>
        /// <param name="leaning">The avatar is bent by the lean this tick.</param>
        /// <param name="free">Grounded, upright, no move input: nothing else is moving the body on purpose.</param>
        /// <returns>Acceleration (x, z) to add to every part.</returns>
        public Vector2 Update(Vector2 centre, Vector2 velocity, bool leaning, bool free, float dt)
        {
            if (!float.IsFinite(centre.X) || !float.IsFinite(centre.Y))
            {
                Holding = false;
                return Vector2.Zero;
            }
            if (!free)
                _sinceLean = float.MaxValue;
            else if (leaning)
                _sinceLean = 0f;
            else if (_sinceLean < float.MaxValue)
                _sinceLean += dt;
            if (_sinceLean >= ReleaseSeconds || Vector2.Distance(centre, Anchor) > MaxOffset)
            {
                Holding = false;
                Anchor = centre;
                if (_sinceLean >= ReleaseSeconds)
                    return Vector2.Zero;
            }
            if (!Holding)
            {
                Holding = true;
                Anchor = centre;
            }
            if (!float.IsFinite(velocity.X) || !float.IsFinite(velocity.Y))
                velocity = Vector2.Zero;
            var a = -Stiffness * (centre - Anchor) - Damping * velocity;
            float len = a.Length();
            return len > MaxAccel ? a * (MaxAccel / len) : a;
        }
    }
}
