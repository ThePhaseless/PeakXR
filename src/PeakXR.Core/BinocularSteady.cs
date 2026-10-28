using System;

namespace PeakXR.Core;

/// <summary>
/// Steadies the binocular lens aim: the zoomed image spans far more of the view than the lens camera sees, so hand
/// tremor is multiplied by the magnification, and on a head-fixed image that shake is a comfort hazard. The aim is
/// eased critically damped with a smoothing time that grows with zoom, and snaps at no zoom so a low zoom stays as
/// responsive as the hand.
/// </summary>
public static class BinocularSteady
{
    /// <summary>Smoothing seconds per magnification step above 1x.</summary>
    public const float SecondsPerZoom = 0.03f;

    /// <summary>Most smoothing at any zoom, so the view never trails the hand by much.</summary>
    public const float MaxSmoothTime = 0.25f;

    /// <summary>Angular magnification of a lens camera of <paramref name="lensFov"/> degrees drawn across
    /// <paramref name="viewFov"/> degrees of the eye's view.</summary>
    public static float Magnification(float lensFov, float viewFov) =>
        MathF.Tan(viewFov * 0.5f * MathF.PI / 180f) / MathF.Tan(lensFov * 0.5f * MathF.PI / 180f);

    /// <summary>Critically damped smoothing time (s) for the lens aim; 0 snaps.</summary>
    public static float SmoothTime(float lensFov, float viewFov)
    {
        if (lensFov <= 0f)
            return MaxSmoothTime;
        float t = (Magnification(lensFov, viewFov) - 1f) * SecondsPerZoom;
        return t <= 0f ? 0f : MathF.Min(t, MaxSmoothTime);
    }
}
