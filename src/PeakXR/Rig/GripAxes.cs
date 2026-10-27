using PeakXR.Core;
using UnityEngine;

namespace PeakXR.Rig;

/// <summary>World directions of a controller grip pose (<see cref="GripFrame"/>), for the Unity side: the fingers,
/// the forearm, the thumb side and the palm, never the grip's raw forward or up, which run along the handle and back
/// toward the forearm. Allocation-free.</summary>
internal static class GripAxes
{
    private static readonly Vector3 FingersLocal = U(GripFrame.Fingers);
    private static readonly Vector3 ThumbLocal = U(GripFrame.ThumbSide);

    /// <summary>Wrist to knuckles: where the open hand points.</summary>
    public static Vector3 Fingers(this Transform grip) => grip.rotation * FingersLocal;

    public static Vector3 Fingers(Quaternion grip) => grip * FingersLocal;

    /// <summary>Little finger to thumb: up out of a fist held like a thumbs-up.</summary>
    public static Vector3 ThumbSide(this Transform grip) => grip.rotation * ThumbLocal;

    /// <summary>A grip-local point or direction given in the hand frame (x the palm axis, y toward the thumb, z along
    /// the fingers).</summary>
    public static Vector3 FromHand(Vector3 v) => new(v.x, -v.z, v.y);

    private static Vector3 U(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
}
