using System;

namespace PeakXR.Core
{
    /// <summary>
    /// Hand-over-hand grace: a grip climb survives a short moment with no hand on the wall, so letting go with
    /// one hand just before the other grabs does not drop the player. A lunge extends the window so the hands
    /// can catch the wall after the leap.
    /// </summary>
    public sealed class ClimbGrace
    {
        /// <summary>Seconds without any gripping hand before the climb ends.</summary>
        public float Grace = 0.25f;

        float _timer;
        float _window = -1f;

        /// <returns>True while the climb should continue.</returns>
        public bool Update(bool anyGrip, float dt)
        {
            if (anyGrip)
            {
                _timer = 0f;
                _window = -1f;
                return true;
            }
            _timer += dt;
            return _timer <= (_window > Grace ? _window : Grace);
        }

        /// <summary>Lengthens the current no-grip window to at least <paramref name="seconds"/>.</summary>
        public void Extend(float seconds)
        {
            if (seconds > _window)
                _window = seconds;
        }

        public void Reset()
        {
            _timer = 0f;
            _window = -1f;
        }
    }

    /// <summary>What a climbing hand feels at its hold, from the surface's climb modifier.</summary>
    public enum HoldKind
    {
        Normal,

        /// <summary>Cheap to hold: the game shows the ladder reticle.</summary>
        Easy,

        /// <summary>Only slides down.</summary>
        Slippery,

        /// <summary>Applies a status (damage, poison, petrify) while climbed.</summary>
        Hazard,
    }

    /// <summary>The game's wall-grab rules, mirrored for hand-hold feedback before a grab.</summary>
    public static class ClimbRules
    {
        /// <summary><c>CharacterClimbing.AcceptableGrabAngle</c>: surfaces 50 to 170 degrees from up.</summary>
        public static bool GrabAngleOk(float angleFromUpDeg)
        {
            float d = angleFromUpDeg - 90f;
            return d > 0f ? d <= 80f : -d <= 40f;
        }

        /// <summary>Ladder reticle threshold from <c>GUIManager</c> (staminaUsageMultiplier at most 0.34).</summary>
        public const float EasyStaminaMultiplier = 0.34f;

        public static HoldKind Kind(
            bool hasModifier,
            bool onlySlideDown,
            bool appliesStatus,
            float staminaUsageMultiplier
        )
        {
            if (!hasModifier)
                return HoldKind.Normal;
            if (appliesStatus)
                return HoldKind.Hazard;
            if (onlySlideDown)
                return HoldKind.Slippery;
            return staminaUsageMultiplier <= EasyStaminaMultiplier
                ? HoldKind.Easy
                : HoldKind.Normal;
        }

        /// <summary>
        /// <c>CharacterClimbing.SampleWall</c> (CharacterClimbing.cs:470) lets go of a surface flatter than 50
        /// degrees from up while the stick is pushed: that is how a climber tops out onto a ledge.
        /// </summary>
        public static bool IsLedgeTop(float angleFromUpDeg) => angleFromUpDeg < 50f;

        /// <summary>
        /// Downward slide speed along the wall in m/s, from <c>CharacterClimbing.GetRequestedPostition</c>
        /// (CharacterClimbing.cs:297, :312): a slide-only surface always pulls 3 units down, plus the climber's
        /// downward <c>playerSlide</c> scaled by the surface speed.
        /// </summary>
        /// <param name="playerSlideY">Vertical <c>playerSlide</c>; negative slides down.</param>
        /// <param name="climbSpeed">Climb speed times its modifier (m/s per unit).</param>
        public static float SlideSpeed(
            float playerSlideY,
            bool onlySlideDown,
            float speedMultiplier,
            float climbSpeed
        )
        {
            float units =
                (onlySlideDown ? 3f : 0f) + MathF.Max(0f, -playerSlideY) * speedMultiplier;
            return units * climbSpeed;
        }
    }

    /// <summary>
    /// Climb stamina felt in the gripping hands: pulses that quicken as stamina runs low, a strong beat when it
    /// is gone, and a fast buzz while sliding down the wall. Call once per frame; a non-zero result is the
    /// amplitude of a pulse to play now.
    /// </summary>
    public sealed class StaminaHaptics
    {
        /// <summary>Stamina fraction under which pulses start.</summary>
        public float LowStamina = 0.3f;

        /// <summary>Slide speed (m/s) above which the slip buzz plays.</summary>
        public float SlideSpeed = 1f;

        float _timer;

        public float Update(float staminaFraction, bool outOfStamina, float slideSpeed, float dt)
        {
            float interval,
                amplitude;
            if (slideSpeed > SlideSpeed)
            {
                interval = 0.05f;
                amplitude = MathF.Min(1f, 0.3f + slideSpeed / 20f);
            }
            else if (outOfStamina)
            {
                interval = 0.15f;
                amplitude = 0.7f;
            }
            else if (staminaFraction < LowStamina)
            {
                float t = MathF.Max(0f, staminaFraction) / LowStamina;
                interval = 0.25f + 0.75f * t;
                amplitude = 0.5f - 0.3f * t;
            }
            else
            {
                _timer = 0f;
                return 0f;
            }

            _timer -= dt;
            if (_timer > 0f)
                return 0f;
            _timer = interval;
            return amplitude;
        }

        public void Reset() => _timer = 0f;
    }
}
