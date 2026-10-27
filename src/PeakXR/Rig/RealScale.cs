using PeakXR.Core;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Rig;

/// <summary>
/// The one conversion between the world and the player's real body under <c>Camera.WorldScale</c>
/// (<see cref="VRRig.Scale"/> world units per real metre). Tracking space is already real; this is for numbers that
/// live in the world but mean the player's own body or reading comfort: a gesture at your real mouth or chest, a
/// menu at a real arm's length, an assist judged on how far your real arm is out. World things (items, rock, the
/// avatar) stay in world units. Allocation-free.
/// </summary>
internal static class RealScale
{
    /// <summary>World units per real metre now (1 without a rig).</summary>
    public static float Of => VRRig.Instance != null ? VRRig.Instance.Scale : 1f;

    /// <summary>Real metres as world units.</summary>
    public static float World(float metres) => WorldScale.ToWorld(metres, Of);

    /// <summary>World units as real metres.</summary>
    public static float Real(float units) => WorldScale.ToReal(units, Of);

    /// <summary><paramref name="point"/> moved toward <paramref name="pivot"/> (the eye) so its offset is in real
    /// metres: run a real-body gesture (hand to mouth, item to chest) in real units around the head.</summary>
    public static Vector3 Real(Vector3 point, Vector3 pivot) => pivot + (point - pivot) / Of;

    /// <summary>The inverse of <see cref="Real(Vector3, Vector3)"/>: a point placed in real metres around
    /// <paramref name="pivot"/> (the mouth below the eyes) where it is in the world.</summary>
    public static Vector3 World(Vector3 realPoint, Vector3 pivot) =>
        pivot + (realPoint - pivot) * Of;
}
