using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Where a head-locked quad goes for one MultiPass eye. A quad centred on the head shows its pattern up to half a tangent
/// apart between the eyes, because its 13 cm distance gives it 13 cm of stereo (issue #5). Ahead of each eye and parallel
/// to the head, the same head-space direction hits the same UV in both eyes, on any cant and any frustum, whatever the
/// quad's size.
/// </summary>
public static class EyeOverlayFit
{
    /// <summary><paramref name="distance"/> ahead of <paramref name="eye"/> along the head's forward.</summary>
    public static Vector3 Position(Vector3 eye, Quaternion head, float distance) =>
        eye + Vector3.Transform(Vector3.UnitZ, head) * distance;
}
