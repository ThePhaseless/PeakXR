using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// The controller grip pose's axes as Unity reports them. OpenXR defines the grip (spec, "Standard pose identifiers"):
/// +X normal to the palm (away from the palm on the left hand, into it on the right), -Z through the tube the curled
/// fingers make, little finger to thumb, +Y by the right-hand rule. Unity flips OpenXR's Z, so in Unity +Z runs along
/// the handle toward the thumb, the fingers (wrist to knuckles) point along -Y and the forearm comes in along +Y.
/// Reading +Z as "where the fingers point" turned every hand 90 degrees: fingers up along the handle, the wrist
/// down below the fist.
/// </summary>
public static class GripFrame
{
    /// <summary>Wrist to knuckles: where the open hand points.</summary>
    public static readonly Vector3 Fingers = new(0f, -1f, 0f);

    /// <summary>From the fist back toward the elbow.</summary>
    public static readonly Vector3 Forearm = new(0f, 1f, 0f);

    /// <summary>Little finger to thumb, through the fist (along the handle).</summary>
    public static readonly Vector3 ThumbSide = Vector3.UnitZ;

    /// <summary>Out of the palm.</summary>
    public static Vector3 Palm(bool left) => new(left ? 1f : -1f, 0f, 0f);

    /// <summary>Out of the back of the hand.</summary>
    public static Vector3 BackOfHand(bool left) => -Palm(left);

    /// <summary>Rotation from a hand frame (+Z along the fingers, +Y toward the thumb side, X the palm axis) into grip
    /// space: 90 degrees about the palm axis.</summary>
    public static readonly Quaternion FromHand = Quaternion.CreateFromAxisAngle(
        Vector3.UnitX,
        MathF.PI / 2f
    );
}
