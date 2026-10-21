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
}
