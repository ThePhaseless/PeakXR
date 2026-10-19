namespace PeakXR.UI.Common;

/// <summary>Conversions between Unity vectors and the System.Numerics vectors used by PeakXR.Core.</summary>
internal static class VectorConvert
{
    public static System.Numerics.Vector3 ToNumerics(this UnityEngine.Vector3 v) => new(v.x, v.y, v.z);

    public static UnityEngine.Vector3 ToUnity(this System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
}
