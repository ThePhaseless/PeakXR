using System;
using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>Distances that shape a panel docked at a lobby kiosk. Defaults suit a waist-high kiosk
    /// with its screen near the top and a player standing about an arm's length away.</summary>
    public sealed class LobbyDockLimits
    {
        /// <summary>How far from the kiosk centre, toward the player, the panel floats (m), so it sits
        /// in front of the kiosk body instead of inside it.</summary>
        public float StandOff = 0.45f;

        /// <summary>Closest the panel may come to the eyes, measured horizontally (m).</summary>
        public float MinDistance = 0.55f;

        /// <summary>Farthest the panel may be from the eyes, measured horizontally (m).</summary>
        public float MaxDistance = 1.3f;

        /// <summary>Panel centre below the kiosk top, roughly where its screen is (m).</summary>
        public float ScreenDrop = 0.2f;

        /// <summary>Lowest the panel centre may sit below eye level (m).</summary>
        public float MaxBelowEye = 0.6f;

        /// <summary>Highest the panel centre may sit above eye level (m).</summary>
        public float MaxAboveEye = 0.05f;
    }

    /// <summary>
    /// Pose of a world panel docked at a lobby kiosk (boarding pass, custom run options, invite note).
    /// The panel floats over the kiosk on the side the player stands on, at the kiosk screen height,
    /// facing the eyes, and is clamped to a readable distance. Unity convention: Y up, yaw 0 = +Z.
    /// </summary>
    public static class LobbyDock
    {
        /// <summary>Kiosk bounds bigger than this on any axis are not a kiosk (m); the pivot is used.</summary>
        public const float MaxKioskSize = 4f;

        /// <summary>Kiosk top above its pivot when its bounds are unusable (m).</summary>
        public const float FallbackHeight = 1.4f;

        /// <summary>Kiosk centre and top from its renderer bounds. Empty or implausibly large bounds
        /// fall back to the pivot with <see cref="FallbackHeight"/>.</summary>
        public static void Anchor(
            Vector3 pivot,
            Vector3 boundsMin,
            Vector3 boundsMax,
            out Vector3 center,
            out float topY
        )
        {
            var size = boundsMax - boundsMin;
            bool usable =
                size.X >= 0f
                && size.Z >= 0f
                && size.Y > 0.05f
                && size.X <= MaxKioskSize
                && size.Y <= MaxKioskSize
                && size.Z <= MaxKioskSize;
            if (usable)
            {
                center = (boundsMin + boundsMax) * 0.5f;
                topY = boundsMax.Y;
            }
            else
            {
                center = pivot;
                topY = pivot.Y + FallbackHeight;
            }
        }

        /// <summary>
        /// Panel centre for a kiosk at <paramref name="kioskCenter"/> seen from <paramref name="head"/>.
        /// <paramref name="facing"/> is the unit direction from the eyes to the panel (the canvas forward).
        /// When the head is straight above the kiosk centre, <paramref name="kioskForward"/> picks the side.
        /// </summary>
        public static Vector3 Pose(
            Vector3 kioskCenter,
            float kioskTopY,
            Vector3 kioskForward,
            Vector3 head,
            LobbyDockLimits limits,
            out Vector3 facing
        )
        {
            if (limits == null)
                throw new ArgumentNullException(nameof(limits));

            var toHead = new Vector2(head.X - kioskCenter.X, head.Z - kioskCenter.Z);
            float reach = toHead.Length();
            Vector2 side;
            if (reach > 1e-3f)
                side = toHead / reach;
            else
            {
                reach = 0f;
                side = new Vector2(kioskForward.X, kioskForward.Z);
                float length = side.Length();
                side = length > 1e-3f ? side / length : Vector2.UnitY;
            }

            // Distance along the kiosk-to-head line; the head is at `reach`, so the panel's horizontal
            // distance to the eyes is reach - along, kept inside [MinDistance, MaxDistance].
            float maxDistance = MathF.Max(limits.MaxDistance, limits.MinDistance);
            float along = Math.Clamp(
                limits.StandOff,
                reach - maxDistance,
                reach - limits.MinDistance
            );
            float y = Math.Clamp(
                kioskTopY - limits.ScreenDrop,
                head.Y - limits.MaxBelowEye,
                head.Y + MathF.Max(limits.MaxAboveEye, -limits.MaxBelowEye)
            );
            var position = new Vector3(
                kioskCenter.X + side.X * along,
                y,
                kioskCenter.Z + side.Y * along
            );

            facing = position - head;
            facing =
                facing.LengthSquared() > 1e-6f
                    ? Vector3.Normalize(facing)
                    : new Vector3(-side.X, 0f, -side.Y);
            return position;
        }
    }
}
