using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>
    /// Picks a slice of a radial menu from a hand flick or a thumbstick. The game's gamepad wheel picks the
    /// slice whose direction is closest to the stick (EmoteWheel/BackpackWheel.TestSelectSliceGamepad); this
    /// does the same, and also accepts the hand moving away from where the wheel opened, so a wheel can be
    /// used without touching the stick. The hand needs <see cref="FlickDistance"/> to select and keeps the
    /// selection until it comes back past a fraction of that, so small tremors do not flicker the choice.
    /// </summary>
    public sealed class RadialSelector
    {
        /// <summary>Share of <see cref="FlickDistance"/> below which a selection made by the hand is dropped.</summary>
        public const float KeepFraction = 0.6f;

        /// <summary>Hand travel from the wheel centre, in metres, that selects a slice.</summary>
        public float FlickDistance = 0.05f;

        /// <summary>Squared stick deflection that counts, the game's own threshold (sqrMagnitude &gt;= 0.5).</summary>
        public float StickThresholdSq = 0.5f;

        public int Selected { get; private set; } = -1;

        /// <summary>True when the last <see cref="Update"/> changed <see cref="Selected"/>.</summary>
        public bool Changed { get; private set; }

        public void Reset()
        {
            Selected = -1;
            Changed = false;
        }

        /// <param name="handOffset">Hand travel from the wheel centre in the wheel plane, metres.</param>
        /// <param name="stick">Thumbstick of the wheel hand.</param>
        /// <param name="directions">Slice directions in the same plane.</param>
        /// <param name="enabled">Slices that may be picked.</param>
        /// <param name="count">Number of valid entries in the arrays.</param>
        public int Update(
            Vector2 handOffset,
            Vector2 stick,
            Vector2[] directions,
            bool[] enabled,
            int count
        )
        {
            int next = -1;
            if (stick.LengthSquared() >= StickThresholdSq)
                next = Nearest(stick, directions, enabled, count);
            else
            {
                float travel = handOffset.Length();
                float needed = Selected >= 0 ? FlickDistance * KeepFraction : FlickDistance;
                if (travel >= needed && travel > 1e-6f)
                    next = Nearest(handOffset, directions, enabled, count);
            }
            Changed = next != Selected;
            Selected = next;
            return next;
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
