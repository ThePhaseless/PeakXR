using System;
using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>
    /// Depth for the VR title menu. The game's title canvas stays one panel in the scene; its hand-sized button
    /// groups slide along their own sight lines to arm's reach and shrink by the same factor, so from the eye they
    /// keep the game's composition while sitting where a fingertip can press them. All of them end up the same
    /// distance from the eye, which curves them gently around the player.
    /// </summary>
    public static class TitleLayout
    {
        /// <summary>A group whose larger side spans more than this is a reading panel (settings), not a cluster
        /// of buttons to hold at hand: it stays on the panel.</summary>
        public const float HandSizedDegrees = 25f;

        /// <summary>Factor that brings something <paramref name="distance"/> metres from the eye to
        /// <paramref name="reach"/>; never above 1, so nothing is pushed away.</summary>
        public static float PullFactor(float distance, float reach) =>
            distance > reach && distance > 1e-3f ? reach / distance : 1f;

        /// <summary><paramref name="point"/> moved along its sight line to <paramref name="factor"/> times its
        /// distance from <paramref name="eye"/>.</summary>
        public static Vector3 AlongSightLine(Vector3 eye, Vector3 point, float factor) =>
            eye + (point - eye) * factor;

        /// <summary>Whether a group <paramref name="width"/> by <paramref name="height"/> metres seen from
        /// <paramref name="distance"/> is small enough to bring to hand.</summary>
        public static bool IsHandSized(float width, float height, float distance)
        {
            if (distance <= 1e-3f)
                return false;
            float degrees =
                2f * MathF.Atan(MathF.Max(width, height) * 0.5f / distance) * UnityQuat.Rad2Deg;
            return degrees <= HandSizedDegrees;
        }

        /// <summary>Alpha of an entrance fade <paramref name="elapsed"/> seconds in, eased (smoothstep) over
        /// <paramref name="seconds"/>.</summary>
        public static float Entrance(float elapsed, float seconds)
        {
            if (seconds <= 0f)
                return 1f;
            float t = Math.Clamp(elapsed / seconds, 0f, 1f);
            return t * t * (3f - 2f * t);
        }
    }
}
