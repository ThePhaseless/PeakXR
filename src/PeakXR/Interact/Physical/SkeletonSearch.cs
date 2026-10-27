using UnityEngine;

namespace PeakXR.Interact.Physical;

/// <summary>
/// The skeleton nearest a point, found without a scene search: each <c>Skelleton.Update</c> (Skelleton.cs:31) reports
/// itself through <see cref="See"/>. The book's read zone and its revive hold each keep one, so both pick the nearest
/// skeleton the same way and both skip one with no hips (<c>Skelleton.Center</c> reads <c>hip</c>, :80-82).
/// </summary>
internal sealed class SkeletonSearch
{
    private Vector3 from;
    private float bestSq;
    private Skelleton found;
    private bool searching;

    /// <summary>Returns last frame's nearest (null when none) and searches this frame around <paramref name="at"/>,
    /// within <paramref name="range"/>.</summary>
    public Skelleton Restart(Vector3 at, float range)
    {
        var last = found;
        found = null;
        from = at;
        bestSq = range * range;
        searching = true;
        return last;
    }

    /// <summary>No search this frame.</summary>
    public void Stop()
    {
        found = null;
        searching = false;
    }

    public void See(Skelleton skeleton)
    {
        if (!searching || skeleton.hip == null)
            return;
        float sq = (skeleton.Center() - from).sqrMagnitude;
        if (sq < bestSq)
        {
            bestSq = sq;
            found = skeleton;
        }
    }
}
