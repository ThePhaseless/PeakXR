namespace PeakXR.Core;

/// <summary>
/// Whether a client draws a received ping at all: the distance check of PointPinger.ReceivePoint_Rpc
/// (PointPinger.cs:87-91). Past the range, or the shorter range with terrain between the two heads, no marker exists.
/// </summary>
public static class PingVisibility
{
    /// <param name="distance">Between the pinger's head and the viewer's, in metres.</param>
    /// <param name="blocked">Terrain on the line between the two heads.</param>
    /// <param name="full">The prefab's <c>visibilityFullNoneNoLos.x</c>.</param>
    /// <param name="none">The prefab's <c>visibilityFullNoneNoLos.y</c>.</param>
    /// <param name="noLosMul">The prefab's <c>NoLosVisibilityMul</c>.</param>
    public static bool Shown(float distance, bool blocked, float full, float none, float noLosMul)
    {
        float limit = full + (none - full) * (blocked ? noLosMul : 1f);
        // Mathf.InverseLerp gives 0 when its ends meet, so a span of nothing never drops the marker.
        return limit <= full || distance < limit;
    }
}
