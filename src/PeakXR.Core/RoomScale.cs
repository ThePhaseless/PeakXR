using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>Yaw math on horizontal vectors stored as (x, z), in Unity's convention: positive yaw turns
/// +Z (forward) toward +X (right). Stick vectors (x right, y forward) use the same layout.</summary>
public static class YawMath
{
    public static Vector2 Rotate(Vector2 v, float degrees)
    {
        float r = degrees * UnityQuat.Deg2Rad;
        float c = MathF.Cos(r),
            s = MathF.Sin(r);
        return new Vector2(v.X * c + v.Y * s, -v.X * s + v.Y * c);
    }

    /// <summary>Yaw in degrees of a horizontal direction; 0 for a zero vector.</summary>
    public static float Yaw(Vector2 v) => MathF.Atan2(v.X, v.Y) * UnityQuat.Rad2Deg;
}

/// <summary>
/// The tracking-space <see cref="Anchor"/> the camera feature keeps over the character's eyes, so physically moving
/// in the room moves the view exactly like the real head (no artificial camera motion). The body walks after the head
/// (<see cref="StanceWalk"/>, which owns when and how far); while that travel is its own (<see cref="OwnMotion"/>), the anchor takes the eye anchor's travel
/// (<see cref="Absorb"/>), so the view stays where the head is while the body arrives under it; outside forces move both. When the head gets
/// past <see cref="RecenterDistance"/> from a body that cannot follow (blocked, climbing, room-scale walking off), the
/// caller fades out and calls <see cref="Recenter"/>.
/// </summary>
public sealed class RoomScaleFollow
{
    /// <summary>Head offset (m) from the anchor at which the view is re-centred under a fade.</summary>
    public float RecenterDistance = 1f;

    /// <summary>Tracking-space (x, z) point kept over the character's eyes.</summary>
    public Vector2 Anchor;

    /// <summary>Largest body travel (m) in one update that the anchor always takes; a bigger step than this and
    /// <see cref="MaxFollowSpeed"/> allow is a warp or a knock, and the view goes with it.</summary>
    public float MaxFollowStep = 0.15f;

    /// <summary>Body speed (m/s) a walk cannot pass: over a long frame the anchor takes this much. Run 20, vr.45
    /// t166.94: a 0.17 s frame at about 1.4 m/s moved the body 0.22 m past a fixed step limit, and the view jumped.</summary>
    public float MaxFollowSpeed = 3f;

    /// <summary>Takes the eye anchor's travel (tracking space) into <see cref="Anchor"/>: all of it, gait sway and
    /// coast included (runs 35, 36), so the view stays put. The camera calls it in the frame it moves the eye anchor,
    /// so a walking body never shows as a frame of view motion (run 32).</summary>
    /// <returns>False when the travel was too far for a walk and stays in the view.</returns>
    public bool Absorb(Vector2 bodyDelta, float dt)
    {
        if (bodyDelta.Length() > MathF.Max(MaxFollowStep, MaxFollowSpeed * dt))
            return false;
        Anchor += bodyDelta;
        return true;
    }

    public void Recenter(Vector2 head) => Anchor = head;

    /// <summary>
    /// The anchor after the rig yaws by <paramref name="degrees"/> around the head, so the head's world
    /// position does not move: world = body + R(yaw) * (p - anchor) must stay equal at p = head.
    /// </summary>
    public static Vector2 AnchorAfterYaw(Vector2 anchor, Vector2 head, float degrees) =>
        head - YawMath.Rotate(head - anchor, -degrees);
}

/// <summary>
/// Sequences a room-scale recenter under a fade, so the view never jumps in sight: the fade target goes black,
/// and after <see cref="FadeTime"/>, once the drawn fade is black (<see cref="FadeCut"/>), <see cref="Update"/>
/// returns true once (snap the anchor then) and the target clears. A started fade always ends in a recenter.
/// </summary>
public sealed class RecenterFade
{
    /// <summary>Seconds the screen gets to go dark before the snap.</summary>
    public float FadeTime = 0.25f;

    /// <summary>The fade actually drawn (0 clear, 1 black), set by the caller each frame; 1 by default.</summary>
    public float DrawnFade = 1f;

    float _timer = -1f;

    /// <summary>Fade request: 1 = black, 0 = clear.</summary>
    public float Target => _timer >= 0f ? 1f : 0f;

    /// <param name="needed">The offset is past the recenter distance.</param>
    /// <returns>True on the frame to recenter.</returns>
    public bool Update(bool needed, float dt)
    {
        if (_timer < 0f)
        {
            if (needed)
                _timer = 0f;
            return false;
        }
        _timer += dt;
        if (_timer < FadeTime || !FadeCut.Ready(DrawnFade, _timer))
            return false;
        _timer = -1f;
        return true;
    }

    public void Reset() => _timer = -1f;
}
