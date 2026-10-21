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

        /// <summary>A graphic covering this fraction of the canvas both ways is a screen effect or page backing.</summary>
        public const float FullCanvas = 0.9f;

        /// <summary>A flat fill behind text covering at least this fraction of the canvas area is a backing.</summary>
        public const float BackingArea = 0.2f;

        /// <summary>
        /// Whether a graphic exists only to sit behind content on a flat screen: anything covering the whole canvas
        /// (fades, dimmers, page backgrounds), or a large flat colour fill (no sprite) holding text, like the credits
        /// strip. Sprite art and small fills are kept.
        /// </summary>
        /// <param name="widthFraction">Graphic width as a fraction of the canvas width.</param>
        /// <param name="heightFraction">Graphic height as a fraction of the canvas height.</param>
        /// <param name="flatFill">Plain colour, no sprite or texture.</param>
        /// <param name="holdsText">Text is drawn inside it (a child).</param>
        public static bool IsBacking(
            float widthFraction,
            float heightFraction,
            bool flatFill,
            bool holdsText
        ) =>
            (widthFraction >= FullCanvas && heightFraction >= FullCanvas)
            || (flatFill && holdsText && widthFraction * heightFraction >= BackingArea);

        /// <summary>Whether text of this colour needs a light outline to read over the scene (dark text) rather than
        /// a dark one (light text). Rec. 709 luminance.</summary>
        public static bool LightOutline(float r, float g, float b) =>
            0.2126f * r + 0.7152f * g + 0.0722f * b < 0.5f;

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
