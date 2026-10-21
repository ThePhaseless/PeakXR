using System;
using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>
    /// Yaw of a body-locked panel that lazily follows the head: it stays put while the head turns
    /// within <see cref="Threshold"/>, then eases back in front until it is within <see cref="Settle"/>.
    /// Keeps the HUD readable without swimming with every small head motion.
    /// </summary>
    public sealed class LazyYawFollow
    {
        /// <summary>Degrees the head may turn away before the panel starts following.</summary>
        public float Threshold = 35f;

        /// <summary>Degrees from the head yaw at which following stops.</summary>
        public float Settle = 2f;

        /// <summary>Exponential catch-up rate (1/s).</summary>
        public float Rate = 4f;

        bool _initialized;

        /// <summary>Current panel yaw in degrees, (-180, 180].</summary>
        public float Yaw { get; private set; }
        public bool Catching { get; private set; }

        public float Update(float targetYaw, float dt)
        {
            if (!_initialized)
            {
                Snap(targetYaw);
                return Yaw;
            }

            float delta = LookMath.WrapDegrees(targetYaw - Yaw);
            if (!Catching && MathF.Abs(delta) > Threshold)
                Catching = true;

            if (Catching)
            {
                float alpha = 1f - MathF.Exp(-Rate * dt);
                Yaw = LookMath.WrapDegrees(Yaw + delta * alpha);
                if (MathF.Abs(LookMath.WrapDegrees(targetYaw - Yaw)) <= Settle)
                    Catching = false;
            }
            return Yaw;
        }

        public void Snap(float yaw)
        {
            Yaw = LookMath.WrapDegrees(yaw);
            Catching = false;
            _initialized = true;
        }
    }

    /// <summary>
    /// Eased fade level: moves linearly toward the target at one full sweep per <see cref="Seconds"/>
    /// and reports a smoothstep of that, so fades start and end gently.
    /// </summary>
    public sealed class FadeEaser
    {
        public float Seconds = 0.3f;

        /// <summary>Linear progress, 0..1.</summary>
        public float Linear { get; private set; }

        /// <summary>Eased output, 0 = clear, 1 = black.</summary>
        public float Alpha => Linear * Linear * (3f - 2f * Linear);

        public float Update(float target, float dt)
        {
            target = Math.Clamp(target, 0f, 1f);
            if (Seconds <= 0f)
                Linear = target;
            else
            {
                float step = dt / Seconds;
                Linear =
                    Linear < target
                        ? MathF.Min(target, Linear + step)
                        : MathF.Max(target, Linear - step);
            }
            return Alpha;
        }

        /// <summary>Jump straight to a level, e.g. black right after a hard cut the mod could not fade.</summary>
        public void Snap(float alpha) => Linear = Math.Clamp(alpha, 0f, 1f);
    }

    /// <summary>World-panel placement math in Unity's convention (yaw 0 = +Z, 90 = +X, Y up).</summary>
    public static class PanelMath
    {
        /// <summary>Heading of a head pose in degrees. When looking straight up or down, the head's
        /// up vector says where the face points.</summary>
        public static float YawOf(Vector3 forward, Vector3 up)
        {
            float x = forward.X,
                z = forward.Z;
            if (x * x + z * z < 1e-4f)
            {
                float sign = forward.Y < 0f ? 1f : -1f;
                x = up.X * sign;
                z = up.Z * sign;
            }
            return MathF.Atan2(x, z) * UnityQuat.Rad2Deg;
        }

        /// <summary>Offset from the eyes to a panel <paramref name="distance"/> metres away along
        /// <paramref name="yaw"/>, dropped <paramref name="tiltDeg"/> degrees below eye level.</summary>
        public static Vector3 InFront(float yaw, float distance, float tiltDeg)
        {
            float y = yaw / UnityQuat.Rad2Deg,
                t = tiltDeg / UnityQuat.Rad2Deg;
            float flat = distance * MathF.Cos(t);
            return new Vector3(MathF.Sin(y) * flat, -distance * MathF.Sin(t), MathF.Cos(y) * flat);
        }

        /// <summary>Scale that makes a canvas <paramref name="widthPixels"/> wide span
        /// <paramref name="widthMeters"/>. Falls back to 1 mm per pixel for an empty canvas.</summary>
        public static float MetersPerPixel(float widthMeters, float widthPixels) =>
            widthPixels > 0f ? widthMeters / widthPixels : 0.001f;

        /// <summary>Angular size cap for one canvas pixel on a menu. PEAK's 1080p menus use about 18-24 px body
        /// text; 0.07 degrees per pixel makes 20 px text 1.4 degrees tall (about 24 mm at 1 m), the usual VR
        /// reading size, so small menus grow to it and no further.</summary>
        public const float MaxMenuDegreesPerPixel = 0.07f;

        /// <summary>Tallest menu content as a fraction of the menu width: 1.2 m at the 1.6 m default, about 37
        /// degrees at 1.8 m, so a tall menu is read with the eyes and a small nod.</summary>
        public const float MenuHeightRatio = 0.75f;

        /// <summary>
        /// Metres per canvas pixel for a menu whose visible content spans <paramref name="contentWidth"/> by
        /// <paramref name="contentHeight"/> canvas pixels: the content fits <paramref name="maxWidthMeters"/> wide
        /// (and <see cref="MenuHeightRatio"/> of that tall) but never exceeds the text-size cap at
        /// <paramref name="distance"/>. Full-screen content sizes exactly like <see cref="MetersPerPixel"/>.
        /// </summary>
        public static float MenuMetersPerPixel(
            float contentWidth,
            float contentHeight,
            float maxWidthMeters,
            float distance
        )
        {
            float fitWidth = maxWidthMeters / MathF.Max(contentWidth, 1f);
            float fitHeight = maxWidthMeters * MenuHeightRatio / MathF.Max(contentHeight, 1f);
            float cap = distance * MathF.Tan(MaxMenuDegreesPerPixel / UnityQuat.Rad2Deg);
            return MathF.Min(cap, MathF.Min(fitWidth, fitHeight));
        }
    }
}
