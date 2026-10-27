using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Hand-over-hand rope and vine climbing. Both game climbs move a <c>percent</c> along the line at a rate set by
/// stick Y (CharacterRopeHandling.cs:63-64, CharacterVineClimbing.cs:115-116), so the arm-pulled body velocity
/// is projected on the line and divided by that rate. Works from the line's own shape (position per percent),
/// so it needs no assumption about which end is up.
/// </summary>
public static class RopeClimb
{
    /// <param name="bodyVelocity">Wanted body velocity, world space, m/s.</param>
    /// <param name="positionPerPercent">World change of the climb point per unit of percent at the current spot.</param>
    /// <param name="percentPerSecondAtFullStick">The game's percent rate for stick Y = +1 (signed).</param>
    /// <param name="negativeStickMultiplier">The game's extra rate for negative stick (rope 3, vine 1).</param>
    /// <param name="deadzone">Stick magnitude below which the result is zero.</param>
    /// <returns>Stick Y in [-1, 1].</returns>
    public static float StickAlong(
        Vector3 bodyVelocity,
        Vector3 positionPerPercent,
        float percentPerSecondAtFullStick,
        float negativeStickMultiplier,
        float deadzone
    )
    {
        float lenSq = positionPerPercent.LengthSquared();
        if (lenSq < 1e-8f || !(MathF.Abs(percentPerSecondAtFullStick) > 1e-8f))
            return 0f;
        float wantedRate = Vector3.Dot(bodyVelocity, positionPerPercent) / lenSq;
        float y = wantedRate / percentPerSecondAtFullStick;
        if (y < 0f && negativeStickMultiplier > 0f)
            y /= negativeStickMultiplier;
        if (MathF.Abs(y) < deadzone)
            return 0f;
        return y > 1f ? 1f
            : y < -1f ? -1f
            : y;
    }

    /// <summary>Squared distance from <paramref name="p"/> to the segment a-b: the grip check against a rope or
    /// vine axis, one segment at a time.</summary>
    public static float SegmentDistanceSq(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        float len = ab.LengthSquared();
        float t = len > 1e-12f ? Vector3.Dot(p - a, ab) / len : 0f;
        t =
            t < 0f ? 0f
            : t > 1f ? 1f
            : t;
        return Vector3.DistanceSquared(p, a + ab * t);
    }
}
