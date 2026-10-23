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

    /// <summary>Why rig.camera is placing the view again.</summary>
    public enum HeightCause
    {
        /// <summary>A new scene or respawned character: the view is re-placed and re-yawed.</summary>
        SceneLoad,

        /// <summary>The player asked (long menu press, the settings' recenter).</summary>
        Recenter,
        SeatedChange,
    }

    /// <summary>
    /// When the player's height is measured: once per session, on an explicit recenter or a seated-mode change, and
    /// never again by a scene load. A player seated, kneeling or leaning at a load keeps the height they had. The
    /// one exception: a head that stays well above the measured height while standing (measured kneeling, then
    /// stood up) asks for a new measure, taken at the next load, behind its loading screen, not mid-level.
    /// </summary>
    public sealed class HeightSession
    {
        /// <summary>How much taller (m) than the measured head counts as a wrong measure.</summary>
        public const float TallerBy = 0.15f;

        /// <summary>For how long (s) without a break, so standing on tiptoe or a stretch never counts.</summary>
        public const float SustainSeconds = 10f;

        private bool measured;
        private float measuredY;
        private float tallerFor;
        private bool wanted;

        public bool ShouldMeasure(HeightCause cause) =>
            !measured || cause != HeightCause.SceneLoad || wanted;

        /// <summary>The height was just taken at tracking-space head Y <paramref name="headY"/>.</summary>
        public void Measured(float headY)
        {
            measured = true;
            measuredY = headY;
            tallerFor = 0f;
            wanted = false;
        }

        /// <summary>Every frame with a tracked head.</summary>
        public void Watch(float headY, bool seated, float dt)
        {
            if (!measured || seated || !(headY > measuredY + TallerBy))
            {
                tallerFor = 0f;
                return;
            }
            tallerFor += dt;
            if (tallerFor >= SustainSeconds)
                wanted = true;
        }
    }
}
