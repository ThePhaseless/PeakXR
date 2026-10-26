using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Where a radial wheel opened at a hand is drawn. A wheel centred on the hand has the hand and forearm through
/// it; drawn a little toward the eye (on the eye-to-hand line, so it still looks centred on the hand) it sits in
/// front of them in depth. Shrunk by <see cref="DrawScale"/> it covers the same angle as a wheel at the hand, so
/// the hand travel that picks a slice (measured at the hand's depth) still lands where the eye sees that slice.
/// </summary>
public static class WheelPlane
{
    /// <summary>How far toward the eye the wheel is drawn from the opening hand, in metres.</summary>
    public const float EyeOffset = 0.06f;

    /// <summary>The offset never takes more than this share of the eye-to-hand distance.</summary>
    public const float MaxOffsetFraction = 0.25f;

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

    /// <summary>Size of the wheel drawn at <paramref name="centre"/> relative to one at <paramref name="hand"/>
    /// that looks the same from <paramref name="eye"/>.</summary>
    public static float DrawScale(Vector3 eye, Vector3 hand, Vector3 centre)
    {
        float toHand = Vector3.Distance(eye, hand);
        return toHand < 1e-5f ? 1f : Vector3.Distance(eye, centre) / toHand;
    }

    /// <summary>A wheel opened in view rather than at the hand sits this far from the eye, metres: past the near
    /// focus strain zone (closer than about half a metre) and still within a short glance.</summary>
    public const float ViewDistance = 0.75f;

    /// <summary>Its centre this far toward the opening hand's side of the view, degrees.</summary>
    public const float ViewSideDegrees = 18f;

    /// <summary>And this far below the line of sight, degrees.</summary>
    public const float ViewDownDegrees = 12f;

    /// <summary>A wheel at the hand is about this far from the eye; one in view is scaled to look as big.</summary>
    public const float HandDistance = 0.45f;

    /// <summary>Draw scale of a wheel at <see cref="InView"/> that looks as big as one at the hand.</summary>
    public const float ViewDrawScale = ViewDistance / HandDistance;

    /// <summary>
    /// Centre of a wheel opened in front of the face (the backpack chooser), in Unity's convention: along the
    /// head's view, turned <see cref="ViewSideDegrees"/> toward the opening hand and <see cref="ViewDownDegrees"/>
    /// down, <see cref="ViewDistance"/> from the eye. Never rolled with the head.
    /// </summary>
    public static Vector3 InView(Vector3 eye, Vector3 headForward, bool handLeft)
    {
        var up = new Vector3(0f, 1f, 0f);
        var forward =
            headForward.LengthSquared() > 1e-8f
                ? Vector3.Normalize(headForward)
                : new Vector3(0f, 0f, 1f);
        var flat = new Vector3(forward.X, 0f, forward.Z);
        // Looking straight up or down: the yaw is undefined, take any.
        var right =
            flat.LengthSquared() > 1e-6f
                ? Vector3.Normalize(Vector3.Cross(up, flat))
                : new Vector3(1f, 0f, 0f);
        var lift = Vector3.Cross(forward, right);
        float side = MathF.Tan(ViewSideDegrees * MathF.PI / 180f) * (handLeft ? -1f : 1f);
        float down = MathF.Tan(ViewDownDegrees * MathF.PI / 180f);
        var dir = Vector3.Normalize(forward + right * side - lift * down);
        return eye + dir * ViewDistance;
    }
}
