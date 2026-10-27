using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Whether a player can read a label from where their eye is. The self-test's screenshots re-aim the camera at
/// what they show, so "drawn" there says nothing about the player's view: this decides it from poses.
/// </summary>
public static class Legibility
{
    /// <summary>Smallest legible text height, degrees (Microsoft mixed-reality typography: 0.35-0.4).</summary>
    public const float MinTextDegrees = 0.4f;

    /// <summary>Nearest a label may sit: a glance at a hand or wrist; nearer, the eyes cross to read it.</summary>
    public const float NearestMeters = 0.25f;

    /// <summary>Farthest off the view direction a label still reads without a head turn, inside a ~100 degree
    /// headset field of view with margin.</summary>
    public const float MaxOffViewDegrees = 45f;

    /// <summary>Steepest angle between the label's face and the line of sight before the text squashes.</summary>
    public const float MaxTiltDegrees = 60f;

    /// <summary>
    /// Null when a label <paramref name="height"/> metres tall at <paramref name="centre"/>, whose readable face
    /// looks along <paramref name="facing"/> (a Unity UI's forward: away from the viewer), reads from
    /// <paramref name="eye"/> looking along <paramref name="forward"/>; else the first reason it does not.
    /// </summary>
    public static string Check(
        Vector3 eye,
        Vector3 forward,
        Vector3 centre,
        Vector3 facing,
        float height,
        out float degrees,
        out float distance,
        out float offView,
        out float tilt
    )
    {
        var to = centre - eye;
        distance = to.Length();
        degrees = 2f * MathF.Atan2(height * 0.5f, distance) * 180f / MathF.PI;
        offView = Angle(forward, to);
        tilt = Angle(facing, to);
        if (distance < NearestMeters)
            return $"too near the eye ({distance:0.00} m)";
        if (offView > MaxOffViewDegrees)
            return $"out of view ({offView:0} deg off the view)";
        if (tilt > MaxTiltDegrees)
            return $"seen edge-on or from behind ({tilt:0} deg)";
        if (degrees < MinTextDegrees)
            return $"too small to read ({degrees:0.00} deg)";
        return null;
    }

    /// <summary>
    /// Whether a mark <paramref name="sizeDegrees"/> across at <paramref name="point"/> lies wholly inside a
    /// headset field <paramref name="fovDegrees"/> wide, seen from <paramref name="eye"/> looking along
    /// <paramref name="forward"/>: its centre's angle off the view (<paramref name="offView"/>) plus half its size
    /// stays within half the field. Pass the narrower (vertical) field so the test holds in every direction.
    /// </summary>
    public static bool InFieldOfView(
        Vector3 eye,
        Vector3 forward,
        Vector3 point,
        float fovDegrees,
        float sizeDegrees,
        out float offView
    )
    {
        offView = Angle(forward, point - eye);
        return offView + sizeDegrees * 0.5f <= fovDegrees * 0.5f;
    }

    static float Angle(Vector3 a, Vector3 b)
    {
        float n = a.Length() * b.Length();
        if (n < 1e-8f)
            return 180f;
        return MathF.Acos(Math.Clamp(Vector3.Dot(a, b) / n, -1f, 1f)) * 180f / MathF.PI;
    }
}
