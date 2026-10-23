namespace PeakXR.Core
{
    /// <summary>
    /// Sorting orders of everything PeakXR draws over the world. URP sorts transparents by sorting order before
    /// render queue, so these decide what covers what: the comfort overlays (fade, edge glow, vignette) always win,
    /// and UI at the hands draws over the player's own hands and arms (no depth test) but still under them.
    /// </summary>
    public static class DrawOrder
    {
        /// <summary>The head-locked fade sphere: covers everything.</summary>
        public const short Fade = short.MaxValue;

        /// <summary>The peripheral status rim.</summary>
        public const short Rim = short.MaxValue - 1;

        /// <summary>The comfort vignette.</summary>
        public const short Vignette = short.MaxValue - 3;

        /// <summary>The laser's cursor dot, over its own rim and beam.</summary>
        public const short LaserCursor = short.MaxValue - 4;

        /// <summary>The laser beam and the cursor's dark rim.</summary>
        public const short LaserBeam = short.MaxValue - 5;

        /// <summary>The VR keyboard canvas.</summary>
        public const short Keyboard = 30000;

        /// <summary>UI on or near a hand (labels, rings, wrist display, wheels, belt slots) and world labels: after
        /// smoke and other transparent effects, under the keyboard and every comfort overlay.</summary>
        public const short HandUi = 29000;

        /// <summary>The loading space's floor and walls: world-space UI sorts by order before distance, and the floor's
        /// centre is nearer the eye than the progress bar and skip ring on it.</summary>
        public const short LoadingRoom = -100;
    }
}
