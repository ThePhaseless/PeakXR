using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Widens the stereo eye separation with the rig's scale (<see cref="WorldScale"/>): a scaled tracking space moves the
/// head and hands by s world units per real metre, and the eyes must sit s times the real IPD apart too, or near
/// things look a different size than head motion says. Row-vector view matrices (System.Numerics); the Unity side
/// transposes.
/// </summary>
public static class StereoScale
{
    /// <summary>The <paramref name="eyeView"/> of a camera whose centre view is <paramref name="centerView"/>, with the
    /// eye's offset from the centre (translation only, any cant kept) multiplied by <paramref name="scale"/>.</summary>
    public static Matrix4x4 ScaleEye(Matrix4x4 eyeView, Matrix4x4 centerView, float scale)
    {
        if (!Matrix4x4.Invert(centerView, out var inverse))
            return eyeView;
        var offset = inverse * eyeView;
        offset.M41 *= scale;
        offset.M42 *= scale;
        offset.M43 *= scale;
        return centerView * offset;
    }
}
