using System;

namespace PeakXR.Core
{
    /// <summary>
    /// Comfort vignette intensity driven by artificial (stick-driven) linear and angular speed.
    /// Call <see cref="Update"/> once per frame.
    /// </summary>
    public sealed class ComfortVignette
    {
        public bool Enabled = true;

        /// <summary>Maximum intensity, 0..1.</summary>
        public float Strength = 0.6f;

        /// <summary>Linear speed (m/s) where the vignette starts and where it reaches full strength.</summary>
        public float LinearMin = 1f,
            LinearMax = 4f;

        /// <summary>Angular speed (deg/s) where the vignette starts and where it reaches full strength.</summary>
        public float AngularMin = 30f,
            AngularMax = 120f;

        /// <summary>Exponential smoothing rates (1/s) while closing and while opening.</summary>
        public float AttackRate = 8f,
            ReleaseRate = 4f;

        /// <summary>0 = no vignette, 1 = fully closed.</summary>
        public float Intensity { get; private set; }

        /// <summary>1 - <see cref="Intensity"/>: 1 = fully open view.</summary>
        public float Aperture => 1f - Intensity;

        public float Update(float linearSpeed, float angularSpeed, float dt)
        {
            if (!Enabled)
            {
                Intensity = 0f;
                return 0f;
            }

            float drive = MathF.Max(
                Ramp(linearSpeed, LinearMin, LinearMax),
                Ramp(MathF.Abs(angularSpeed), AngularMin, AngularMax)
            );
            float target = Math.Clamp(Strength, 0f, 1f) * drive;
            float rate = target > Intensity ? AttackRate : ReleaseRate;
            float alpha = rate > 0f ? 1f - MathF.Exp(-rate * dt) : 1f;
            Intensity += (target - Intensity) * alpha;
            return Intensity;
        }

        public void Reset() => Intensity = 0f;

        static float Ramp(float v, float min, float max)
        {
            if (max <= min)
                return v >= min ? 1f : 0f;
            return Math.Clamp((v - min) / (max - min), 0f, 1f);
        }
    }
}
