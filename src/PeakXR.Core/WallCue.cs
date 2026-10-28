using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// The cue over the head-in-wall black: without it the view just goes dark and the player cannot tell why or which
/// way to move. An arrow in the view points toward open space on the ground plane, read like a map: up is ahead,
/// down is behind, left and right are to the sides.
/// </summary>
public static class WallCueMath
{
    /// <summary>Clockwise degrees from straight up in the view toward <paramref name="toOpen"/>, judged on the
    /// ground plane against the head's heading; NaN when either has no horizontal part.</summary>
    public static float ArrowDegrees(Vector3 toOpen, Vector3 headForward)
    {
        var a = new Vector2(toOpen.X, toOpen.Z);
        var f = new Vector2(headForward.X, headForward.Z);
        if (a.LengthSquared() < 1e-6f || f.LengthSquared() < 1e-6f)
            return float.NaN;
        // Signed angle from the heading to the open side, positive to the right (Unity is left-handed, y up).
        float cross = f.Y * a.X - f.X * a.Y;
        float dot = f.X * a.X + f.Y * a.Y;
        return MathF.Atan2(cross, dot) * 180f / MathF.PI;
    }
}

/// <summary>A soft sound when the head enters a wall, at most once per <see cref="RepeatSeconds"/>, so standing at
/// the edge of a rock does not chime over and over.</summary>
public sealed class WallCueGate
{
    public float RepeatSeconds = 2f;

    private bool was;
    private float last = float.NegativeInfinity;

    /// <returns>True on the frame to play the sound.</returns>
    public bool Update(bool blocked, float now)
    {
        bool fire = blocked && !was && now - last >= RepeatSeconds;
        if (fire)
            last = now;
        was = blocked;
        return fire;
    }
}
