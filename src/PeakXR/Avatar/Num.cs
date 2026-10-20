using UnityEngine;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;

namespace PeakXR.Avatar;

/// <summary>Unity to System.Numerics conversions for calling the tested math in PeakXR.Core. Same axis
/// conventions on both sides, so these are plain component copies.</summary>
internal static class Num
{
    public static NVector3 N(this Vector3 v) => new(v.x, v.y, v.z);

    public static NQuaternion N(this Quaternion q) => new(q.x, q.y, q.z, q.w);

    public static Vector3 U(this NVector3 v) => new(v.X, v.Y, v.Z);

    public static Quaternion U(this NQuaternion q) => new(q.X, q.Y, q.Z, q.W);
}
