using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// The path a thrown item will fly, for the throw aim cue: stepped the way the physics engine moves a rigidbody
/// (velocity gains gravity, then loses its linear damping, then moves the body), so the drawn arc bends like the
/// real flight.
/// </summary>
public static class ThrowPath
{
    /// <summary>Fills <paramref name="points"/> with the item's position every <paramref name="step"/> seconds from
    /// <paramref name="start"/>.</summary>
    public static void Trace(
        Vector3 start,
        Vector3 velocity,
        Vector3 gravity,
        float damping,
        float step,
        Span<Vector3> points
    )
    {
        var p = start;
        var v = velocity;
        float keep = 1f / (1f + step * MathF.Max(0f, damping));
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = p;
            v = (v + gravity * step) * keep;
            p += v * step;
        }
    }
}
