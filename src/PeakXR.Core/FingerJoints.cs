using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Which way each finger joint bends, in a canonical hand frame shared by every rig: fingers +Y, index side +Z, palm
/// +X on a right hand; a left hand is its mirror image across X. A rig maps the frame onto its hand bone from its bind
/// pose (<see cref="HandFrame"/>), and each joint turns from its own rest rotation about <see cref="LocalAxis"/>, so
/// curl 0 is the rest pose whatever the bone axes are. The fingers bend about the knuckle line (the scout's own fist,
/// A_Scout_Idle1, turns every index, middle and pinky joint about it); the thumb first swings in front of the palm
/// (opposition, about the fingers' direction), then folds across it toward the pinky (flexion, about the palm normal).
/// How far a joint bends is <see cref="HandPose.MaxJointAngle"/> (never back past rest: no hyperextension), how far
/// it may turn any other way is <see cref="MaxSplay"/>.
/// </summary>
public static class FingerJoints
{
    static readonly Vector3 KnuckleLine = new(0f, 0f, -1f);
    static readonly Vector3 AlongFingers = new(0f, 1f, 0f);
    static readonly Vector3 PalmOut = new(-1f, 0f, 0f);

    // Sideways range per joint (palm, knuckle, outer joint): the outer joints are hinges.
    static readonly float[] FingerSplay = { 10f, 10f, 0f };
    static readonly float[] ThumbSplay = { 10f, 5f, 0f };

    /// <summary>The joint's bend axis in the canonical frame of the <paramref name="left"/> or right hand.</summary>
    public static Vector3 BendAxis(int finger, int joint, bool left)
    {
        var a =
            finger != 0 ? KnuckleLine
            : joint == 0 ? AlongFingers
            : PalmOut;
        return left ? new Vector3(a.X, -a.Y, -a.Z) : a;
    }

    /// <summary>Most a joint may turn about anything but its bend axis, in degrees.</summary>
    public static float MaxSplay(int finger, int joint) =>
        finger == 0 ? ThumbSplay[joint] : FingerSplay[joint];

    /// <summary>The joint's turn from rest for <paramref name="curl"/>, in the canonical hand frame.</summary>
    public static Quaternion Bend(int finger, int joint, float curl, bool left) =>
        Quaternion.CreateFromAxisAngle(
            BendAxis(finger, joint, left),
            HandPose.JointAngle(finger, joint, curl) * UnityQuat.Deg2Rad
        );

    /// <summary>The canonical hand frame in the hand bone's: <paramref name="fingers"/> where the fingers point at rest,
    /// <paramref name="indexSide"/> from the little finger's knuckle to the index's (squared up to the fingers).</summary>
    public static Quaternion HandFrame(Vector3 fingers, Vector3 indexSide)
    {
        fingers = Vector3.Normalize(fingers);
        return UnityQuat.LookRotation(
            indexSide - fingers * Vector3.Dot(indexSide, fingers),
            fingers
        );
    }

    /// <summary>The bend axis in a joint's own rest frame, for a joint at <paramref name="restInHand"/> in the hand
    /// bone's frame: the joint turns <c>rest * AngleAxis(angle, axis)</c>.</summary>
    public static Vector3 LocalAxis(
        Quaternion handFrame,
        Quaternion restInHand,
        int finger,
        int joint,
        bool left
    ) =>
        Vector3.Normalize(
            UnityQuat.Rotate(
                Quaternion.Inverse(restInHand),
                UnityQuat.Rotate(handFrame, BendAxis(finger, joint, left))
            )
        );

    /// <summary>
    /// Splits a joint's turn from rest (<paramref name="delta"/>, in the frame <paramref name="axis"/> is in) into the
    /// bend about <paramref name="axis"/> (degrees, negative past rest) and how far it turns any other way (degrees).
    /// </summary>
    public static void Measure(Quaternion delta, Vector3 axis, out float flex, out float off)
    {
        if (delta.W < 0f)
            delta = Quaternion.Negate(delta);
        float along = Vector3.Dot(new Vector3(delta.X, delta.Y, delta.Z), axis);
        flex = 2f * MathF.Atan2(along, delta.W) * UnityQuat.Rad2Deg;
        var twist = Quaternion.CreateFromAxisAngle(axis, flex * UnityQuat.Deg2Rad);
        var swing = UnityQuat.Mul(delta, Quaternion.Inverse(twist));
        // atan2, not acos(w): acos loses 0.04 degrees to float rounding next to rest.
        off =
            2f
            * MathF.Atan2(new Vector3(swing.X, swing.Y, swing.Z).Length(), MathF.Abs(swing.W))
            * UnityQuat.Rad2Deg;
    }

    /// <summary>A measured bend inside the joint's limits: not past rest, not past its fist, not turned sideways past
    /// <see cref="MaxSplay"/>, each within <paramref name="tolerance"/> degrees.</summary>
    public static bool Within(int finger, int joint, float flex, float off, float tolerance) =>
        flex >= -tolerance
        && flex <= HandPose.MaxJointAngle(finger, joint) + tolerance
        && off <= MaxSplay(finger, joint) + tolerance;
}
