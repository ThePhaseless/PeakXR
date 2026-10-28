using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>Thumbstick shaping: radial deadzone with rescale, outer saturation and a response curve.</summary>
public static class Stick
{
    /// <summary>
    /// Zero inside <paramref name="inner"/>; magnitude rescaled from [inner, outer] to [0, 1], then raised to
    /// <paramref name="exponent"/>. Direction is preserved.
    /// </summary>
    public static Vector2 Apply(Vector2 v, float inner, float outer, float exponent = 1f)
    {
        float mag = v.Length();
        if (mag <= inner || mag <= 0f)
            return Vector2.Zero;
        return v * (Shape(mag, inner, outer, exponent) / mag);
    }

    /// <summary>One-axis version of <see cref="Apply"/>; keeps the sign.</summary>
    public static float Axis(float x, float inner, float outer, float exponent = 1f)
    {
        float mag = MathF.Abs(x);
        if (mag <= inner)
            return 0f;
        return MathF.Sign(x) * Shape(mag, inner, outer, exponent);
    }

    static float Shape(float mag, float inner, float outer, float exponent)
    {
        float range = outer - inner;
        float t = range > 0f ? Math.Clamp((mag - inner) / range, 0f, 1f) : 1f;
        return exponent == 1f ? t : MathF.Pow(t, exponent);
    }
}
