using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Where a radial wheel opened at a hand is drawn, and where the hand is on it. A wheel centred on the hand has
/// the hand and forearm through it; drawn a little toward the eye (on the eye-to-hand line, so it still looks
/// centred on the hand) it sits in front of them in depth. The hand then picks where the eye sees it on the
/// wheel: its position projected along the eye ray onto the wheel plane.
/// </summary>
public static class WheelPlane
{
    /// <summary>How far toward the eye the wheel is drawn from the opening hand, in metres.</summary>
    public const float EyeOffset = 0.06f;

    /// <summary>The offset never takes more than this share of the eye-to-hand distance.</summary>
    public const float MaxOffsetFraction = 0.25f;

    // A hand less than this share of the wheel's depth in front of the eye is read straight onto the plane.
    private const float MinDepthFraction = 0.25f;

    /// <summary>The wheel centre: <paramref name="offset"/> metres from <paramref name="hand"/> toward
    /// <paramref name="eye"/>, capped at <see cref="MaxOffsetFraction"/> of their distance.</summary>
    public static Vector3 Centre(Vector3 eye, Vector3 hand, float offset)
    {
        var toEye = eye - hand;
        float distance = toEye.Length();
        if (distance < 1e-5f)
            return hand;
        float d = MathF.Min(MathF.Max(offset, 0f), distance * MaxOffsetFraction);
        return hand + toEye * (d / distance);
    }

    /// <summary>
    /// <paramref name="hand"/> in the wheel plane (x along <paramref name="right"/>, y along <paramref name="up"/>,
    /// from <paramref name="centre"/>), as the eye sees it. A hand beside or behind the eye has no such point and
    /// is projected straight onto the plane.
    /// </summary>
    public static Vector2 HandOffset(
        Vector3 eye,
        Vector3 hand,
        Vector3 centre,
        Vector3 right,
        Vector3 up
    )
    {
        var normal = Vector3.Cross(right, up);
        float planeDepth = Vector3.Dot(centre - eye, normal);
        if (planeDepth < 0f)
        {
            normal = -normal;
            planeDepth = -planeDepth;
        }
        float handDepth = Vector3.Dot(hand - eye, normal);
        var seen =
            planeDepth > 1e-5f && handDepth > planeDepth * MinDepthFraction
                ? eye + (hand - eye) * (planeDepth / handDepth)
                : hand;
        var local = seen - centre;
        return new Vector2(Vector3.Dot(local, right), Vector3.Dot(local, up));
    }
}
