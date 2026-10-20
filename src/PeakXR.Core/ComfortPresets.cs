using System;
using System.Numerics;

namespace PeakXR.Core
{
    public enum TurnMode
    {
        Snap,
        Smooth,
    }

    public enum ComfortPreset
    {
        Comfortable,
        Moderate,
        Intense,

        /// <summary>The individual settings were edited and match no preset.</summary>
        Custom,
    }

    /// <summary>The settings a comfort preset controls.</summary>
    public struct ComfortProfile
    {
        public TurnMode Turn;
        public float SnapAngle;
        public float SmoothTurnSpeed;
        public bool Vignette;
        public float VignetteStrength;

        /// <summary>Seconds for the move stick input to ramp from rest to full (0 = off).</summary>
        public float MoveSmoothing;
    }

    /// <summary>
    /// Comfort presets as bundles of individual settings. Selecting a preset writes the settings; editing a
    /// setting afterwards turns the preset into <see cref="ComfortPreset.Custom"/> unless it still matches one.
    /// </summary>
    public static class ComfortPresets
    {
        public static ComfortProfile Get(ComfortPreset preset)
        {
            switch (preset)
            {
                case ComfortPreset.Comfortable:
                    // Snap-only turning, strong vignette, gentle acceleration.
                    return new ComfortProfile
                    {
                        Turn = TurnMode.Snap,
                        SnapAngle = 30f,
                        SmoothTurnSpeed = 90f,
                        Vignette = true,
                        VignetteStrength = 0.8f,
                        MoveSmoothing = 0.3f,
                    };
                case ComfortPreset.Intense:
                    return new ComfortProfile
                    {
                        Turn = TurnMode.Smooth,
                        SnapAngle = 30f,
                        SmoothTurnSpeed = 150f,
                        Vignette = false,
                        VignetteStrength = 0.5f,
                        MoveSmoothing = 0f,
                    };
                default:
                    // Moderate: the design defaults (snap 30 deg, medium vignette).
                    return new ComfortProfile
                    {
                        Turn = TurnMode.Snap,
                        SnapAngle = 30f,
                        SmoothTurnSpeed = 120f,
                        Vignette = true,
                        VignetteStrength = 0.5f,
                        MoveSmoothing = 0.15f,
                    };
            }
        }

        /// <summary>The preset <paramref name="profile"/> matches, ignoring settings its turn and vignette modes do
        /// not use; <see cref="ComfortPreset.Custom"/> when none matches.</summary>
        public static ComfortPreset Identify(ComfortProfile profile)
        {
            for (var p = ComfortPreset.Comfortable; p < ComfortPreset.Custom; p++)
            {
                if (Matches(Get(p), profile))
                    return p;
            }
            return ComfortPreset.Custom;
        }

        static bool Matches(ComfortProfile a, ComfortProfile b)
        {
            if (
                a.Turn != b.Turn
                || a.Vignette != b.Vignette
                || !Near(a.MoveSmoothing, b.MoveSmoothing)
            )
                return false;
            if (
                a.Turn == TurnMode.Snap
                    ? !Near(a.SnapAngle, b.SnapAngle)
                    : !Near(a.SmoothTurnSpeed, b.SmoothTurnSpeed)
            )
                return false;
            return !a.Vignette || Near(a.VignetteStrength, b.VignetteStrength);
        }

        static bool Near(float a, float b) => MathF.Abs(a - b) < 1e-3f;
    }

    /// <summary>
    /// Reduced-acceleration smoothing for the move stick: the input vector moves toward the stick at a limited
    /// rate, so the force-driven body speeds up gently. Stopping ramps twice as fast so the player does not
    /// slide on after letting go.
    /// </summary>
    public sealed class InputRamp
    {
        /// <summary>Seconds from rest to full input; 0 or less passes the stick through.</summary>
        public float RiseTime;

        public Vector2 Current { get; private set; }

        public Vector2 Update(Vector2 target, float dt)
        {
            if (RiseTime <= 0f)
                return Current = target;
            bool speedingUp = target.Length() > Current.Length();
            float maxStep = dt / (speedingUp ? RiseTime : RiseTime * 0.5f);
            var delta = target - Current;
            float len = delta.Length();
            Current = len <= maxStep ? target : Current + delta * (maxStep / len);
            return Current;
        }

        public void Reset() => Current = Vector2.Zero;
    }

    /// <summary>Opening of the comfort vignette ring, as half-angles from the view centre in degrees.</summary>
    public static class VignetteShape
    {
        /// <summary>Clear-view half-angle at intensity 0 (outside a typical headset field of view).</summary>
        public const float OpenAngle = 70f;

        /// <summary>Clear-view half-angle at full intensity.</summary>
        public const float ClosedAngle = 22f;

        /// <summary>Width of the soft edge between clear and black.</summary>
        public const float Feather = 18f;

        public static float InnerAngle(float intensity)
        {
            float t = intensity < 0f ? 0f : (intensity > 1f ? 1f : intensity);
            return OpenAngle + (ClosedAngle - OpenAngle) * t;
        }
    }
}
