namespace PeakXR.Core
{
    public enum HeightMode
    {
        Standing,
        Seated,
    }

    /// <summary>Vertical tracking-space offset that puts the player's eyes at the avatar's eye height.</summary>
    public static class HeightCalibration
    {
        public const float DefaultStandingEyeHeight = 1.65f;
        public const float DefaultSeatedEyeHeight = 1.2f;
        public const float MinPlausibleEyeHeight = 0.5f;
        public const float MaxPlausibleEyeHeight = 2.5f;

        /// <summary>
        /// Offset (metres) to add to tracking-space Y. <paramref name="measuredEyeHeight"/> is the real eye height
        /// above the floor in the chosen mode: standing eye height when standing, seated head height when seated.
        /// Either way it maps to the avatar's standing <paramref name="avatarEyeHeight"/>. A measurement outside
        /// [0.5, 2.5] m (or NaN, e.g. headset not tracked yet) falls back to the mode's default.
        /// </summary>
        public static float YOffset(HeightMode mode, float measuredEyeHeight, float avatarEyeHeight)
        {
            float eye = measuredEyeHeight;
            if (!(eye >= MinPlausibleEyeHeight && eye <= MaxPlausibleEyeHeight))
                eye = mode == HeightMode.Seated ? DefaultSeatedEyeHeight : DefaultStandingEyeHeight;
            return avatarEyeHeight - eye;
        }
    }
}
