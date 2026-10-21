using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// How the hand rays look: a ray that starts at or passes the face is a bar across the view (a hand at the mouth
/// pointing up), so it is not drawn; the hit dot keeps a constant angular size with a hover step, so it reads on a
/// button at any distance.
/// </summary>
public static class LaserLook
{
    /// <summary>A ray passing closer than this to the eye centre (metres) crosses the face.</summary>
    public const float FaceRadius = 0.15f;

    /// <summary>Dot diameter in degrees, its floor up close (metres) and its growth while over something usable.</summary>
    public const float CursorDegrees = 0.6f;
    public const float MinCursor = 0.005f;
    public const float HoverScale = 1.4f;

    public static bool CrossesFace(Vector3 from, Vector3 to, Vector3 eye, float radius = FaceRadius)
    {
        var d = to - from;
        float len2 = d.LengthSquared();
        float t = len2 > 1e-8f ? Math.Clamp(Vector3.Dot(eye - from, d) / len2, 0f, 1f) : 0f;
        return Vector3.DistanceSquared(from + d * t, eye) < radius * radius;
    }

    public static float CursorDiameter(float distance, bool hovered) =>
        MathF.Max(MinCursor, SpatialMath.SpanMeters(distance, CursorDegrees))
        * (hovered ? HoverScale : 1f);
}
