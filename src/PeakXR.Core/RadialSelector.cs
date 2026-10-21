using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>
    /// Radial-menu selection shared by every VR wheel. The game's gamepad wheels pick the slice whose direction is
    /// closest to the stick (EmoteWheel/BackpackWheel.TestSelectSliceGamepad); <see cref="Nearest"/> is that pick,
    /// done in the wheel's own plane so it also holds for a wheel turned in 3D. An instance turns a hand flick
    /// away from the wheel centre, or a thumbstick, into such a direction: the hand needs
    /// <see cref="FlickDistance"/> to start and keeps pointing until it comes back past
    /// <see cref="KeepFraction"/> of that, so small tremors do not flicker the choice. Past <see cref="MaxDistance"/>
    /// (the drawn outer edge) the hand is over empty space and picks nothing.
    /// </summary>
    public sealed class RadialSelector
    {
        /// <summary>Share of <see cref="FlickDistance"/> below which a hand selection is dropped.</summary>
        public const float KeepFraction = 0.6f;

        /// <summary>Squared stick deflection that counts, the game's own threshold (sqrMagnitude &gt;= 0.5).</summary>
        public const float StickThresholdSq = 0.5f;

        /// <summary>Hand travel from the wheel centre, in metres, that starts a selection.</summary>
        public float FlickDistance = 0.05f;

        /// <summary>Share of <see cref="MaxDistance"/> a held selection may overshoot before it is dropped.</summary>
        public const float KeepOvershoot = 1.1f;

        /// <summary>Hand travel past which the hand is beyond the drawn wheel and selects nothing.</summary>
        public float MaxDistance = float.PositiveInfinity;

        /// <summary>True while the hand or stick points at a slice.</summary>
        public bool Active { get; private set; }

        public void Reset() => Active = false;

        /// <param name="handOffset">Hand travel from the wheel centre in the wheel plane, metres.</param>
        /// <param name="stick">Thumbstick of the wheel hand.</param>
        /// <param name="direction">Direction to pick with, at least the game's stick threshold long; zero when none.</param>
        /// <returns><see cref="Active"/>.</returns>
        public bool Update(Vector2 handOffset, Vector2 stick, out Vector2 direction)
        {
            direction = Vector2.Zero;
            if (stick.LengthSquared() >= StickThresholdSq)
                direction = stick;
            else
            {
                float travel = handOffset.Length();
                float needed = Active ? FlickDistance * KeepFraction : FlickDistance;
                float limit = Active ? MaxDistance * KeepOvershoot : MaxDistance;
                if (travel >= needed && travel <= limit && travel > 1e-6f)
                    direction = handOffset / travel;
            }
            Active = direction != Vector2.Zero;
            return Active;
        }

        /// <summary>Index of the enabled direction with the smallest angle to <paramref name="input"/>, or -1.</summary>
        public static int Nearest(Vector2 input, Vector2[] directions, bool[] enabled, int count)
        {
            float length = input.Length();
            if (length < 1e-6f)
                return -1;
            var dir = input / length;
            int best = -1;
            float bestDot = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                if (!enabled[i])
                    continue;
                var d = directions[i];
                float l = d.Length();
                if (l < 1e-6f)
                    continue;
                float dot = Vector2.Dot(dir, d / l);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    best = i;
                }
            }
            return best;
        }
    }
}
