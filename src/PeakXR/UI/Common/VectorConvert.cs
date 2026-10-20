namespace PeakXR.UI.Common;

/// <summary>Conversions between Unity vectors and the System.Numerics vectors used by PeakXR.Core.</summary>
internal static class VectorConvert
{
    public static System.Numerics.Vector3 ToNumerics(this UnityEngine.Vector3 v) =>
        new(v.x, v.y, v.z);

    public static UnityEngine.Vector3 ToUnity(this System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>Yaw of a direction in degrees (0 = +Z, 90 = +X).</summary>
    public static float HeadingDegrees(this UnityEngine.Vector3 forward) =>
        UnityEngine.Mathf.Atan2(forward.x, forward.z) * UnityEngine.Mathf.Rad2Deg;
}
