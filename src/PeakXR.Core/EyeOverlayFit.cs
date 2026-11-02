using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Where a head-locked quad goes for one MultiPass eye. A quad centred on the head, 6 cm away on the default near plane,
/// shows its pattern about 1 tangent apart between the eyes (0.063 / 0.06, issue #5). Ahead of each eye and parallel
/// to the head, the same head-space direction hits the same UV in both eyes, on any cant and any frustum, whatever the
/// quad's size.
/// </summary>
public static class EyeOverlayFit
{
    /// <summary><paramref name="distance"/> ahead of <paramref name="eye"/> along the head's forward.</summary>
    public static Vector3 Position(Vector3 eye, Quaternion head, float distance) =>
        eye + Vector3.Transform(Vector3.UnitZ, head) * distance;
}
