using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Where a widget that rides on a hand (the hold-progress ring, the friend feed) goes. Above the hand while the hand
/// is out in front; once the hand comes to the face (eating, drinking, a bugle at the mouth) that spot is between the
/// eyes and the action, a few centimetres away and half the view wide, so the widget moves to a fixed spot low in
/// view on the hand's side, at reading distance. It blends between the two so it never jumps.
/// </summary>
public static class HandWidgetPlace
{
    /// <summary>Hand this close to the eyes (metres): the widget is fully parked.</summary>
    public const float NearFace = 0.25f;

    /// <summary>Hand this far from the eyes or more: the widget is fully on the hand.</summary>
    public const float ClearFace = 0.4f;

    /// <summary>Head-space spot of the parked ring for the right hand (mirrored for the left): about 20 degrees down
    /// and 15 to the side, 0.45 m away.</summary>
    public static readonly Vector3 RingParked = new(0.12f, -0.16f, 0.4f);

    /// <summary>Parked friend feed: further out to the side and higher than the ring, so both can show.</summary>
    public static readonly Vector3 FeedParked = new(0.2f, -0.06f, 0.42f);

    /// <summary><paramref name="up"/> metres above the hand, then <paramref name="toward"/> metres toward the eye.</summary>
    public static Vector3 AboveHand(Vector3 hand, Vector3 eye, float up, float toward)
    {
        var p = hand + Vector3.UnitY * up;
        var back = eye - p;
        float len = back.Length();
        return len > 1e-5f ? p + back * (toward / len) : p;
    }

    /// <summary>
    /// <paramref name="onHand"/> while the hand is clear of the face, else the parked spot
    /// (<paramref name="parked"/> in the head's yaw frame, x mirrored for the left hand). Yaw only: looking down
    /// at the food or tilting the head leaves the ring where it is in the world.
    /// </summary>
    public static Vector3 ClearOfFace(
        Vector3 onHand,
        Vector3 hand,
        Vector3 eye,
        Quaternion head,
        Vector3 parked,
        bool left
    )
    {
        float t = Math.Clamp(
            (Vector3.Distance(hand, eye) - NearFace) / (ClearFace - NearFace),
            0f,
            1f
        );
        if (t >= 1f)
            return onHand;
        if (left)
            parked.X = -parked.X;
        var park = eye + Vector3.Transform(parked, YawOnly(head));
        return Vector3.Lerp(park, onHand, t * t * (3f - 2f * t));
    }

    private static Quaternion YawOnly(Quaternion head)
    {
        var f = Vector3.Transform(Vector3.UnitZ, head);
        // Looking straight up or down: the head's up vector points the way the face is turned.
        if (f.X * f.X + f.Z * f.Z < 1e-4f)
            f = Vector3.Transform(f.Y > 0f ? -Vector3.UnitY : Vector3.UnitY, head);
        return Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.Atan2(f.X, f.Z));
    }
}
