using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Where a player's head points when they look at something they reach for: most of the way, not exactly (the eyes
/// take the last part), and within a comfortable neck. The self-test's scripted head uses it so a cue at the acting
/// hand is judged from a view a player would have, not from a head locked level (run 48: rings 55-110 degrees off).
/// </summary>
public static class HeadGlance
{
    /// <summary>Most of the angle left to the eyes (deg); a quarter of a smaller turn.</summary>
    public const float MaxLeftToEyes = 20f;

    /// <summary>Comfortable neck range (deg): looking up, looking down, and turning either way from the heading.</summary>
    public const float MaxUp = 60f,
        MaxDown = 60f,
        MaxYaw = 70f;

    /// <summary>
    /// The view direction for a head whose level heading is <paramref name="heading"/> looking toward a point at
    /// <paramref name="toTarget"/> from the eye. Unit length; <paramref name="heading"/> itself for a target dead ahead.
    /// </summary>
    public static Vector3 Aim(Vector3 heading, Vector3 toTarget)
    {
        var fwd = Vector3.Normalize(new Vector3(heading.X, 0f, heading.Z));
        float len = toTarget.Length();
        if (len < 1e-5f)
            return fwd;
        var to = toTarget / len;
        float total = MathF.Acos(Math.Clamp(Vector3.Dot(fwd, to), -1f, 1f));
        if (total < 1e-4f)
            return fwd;
        float keep = MathF.Min(MaxLeftToEyes * Rad, total * 0.25f);
        var want = Slerp(fwd, to, total, (total - keep) / total);

        float pitch = Math.Clamp(
            MathF.Asin(Math.Clamp(want.Y, -1f, 1f)),
            -MaxDown * Rad,
            MaxUp * Rad
        );
        var flat = new Vector3(want.X, 0f, want.Z);
        float yaw =
            flat.LengthSquared() < 1e-8f
                ? 0f
                : MathF.Atan2(Vector3.Cross(fwd, flat).Y, Vector3.Dot(fwd, flat));
        yaw = Math.Clamp(yaw, -MaxYaw * Rad, MaxYaw * Rad);
        // Positive yaw turns toward up x heading, the same side the cross above measured it on.
        var side = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, fwd));
        var level = fwd * MathF.Cos(yaw) + side * MathF.Sin(yaw);
        return Vector3.Normalize(level * MathF.Cos(pitch) + Vector3.UnitY * MathF.Sin(pitch));
    }

    const float Rad = MathF.PI / 180f;

    static Vector3 Slerp(Vector3 a, Vector3 b, float angle, float t)
    {
        float s = MathF.Sin(angle);
        if (s < 1e-4f)
            return Vector3.Normalize(Vector3.Lerp(a, b, t));
        return Vector3.Normalize(
            a * (MathF.Sin((1f - t) * angle) / s) + b * (MathF.Sin(t * angle) / s)
        );
    }
}
