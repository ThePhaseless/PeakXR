using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// A held item with both hands on it sits between them: the middle of its two grips on the middle of the two hands,
/// its grip-to-grip axis along the hand-to-hand axis, and its roll about that axis read from both hands. When the
/// real hands are farther apart or closer than the grips, each hand is off its grip by half the difference, so neither
/// hand is exact while the other carries all the error. The roll is each hand's own "up" captured at the join
/// (<see cref="Capture"/>) and summed, so turning both wrists rolls the item with them and turning one rolls it half
/// as far. The main hand still drives use and handoff (<see cref="HoldHands"/>); only the pose changes.
/// </summary>
public static class TwoHandPose
{
    /// <summary>Hands closer than this (metres) give no usable axis: the last one holds.</summary>
    public const float MinSpacing = 0.03f;

    /// <summary>How far from the item's second grip (metres) a closing hand snaps onto it, the same reach the grip
    /// takes it at (PhysicalInteraction.SecondGripReach).</summary>
    public const float SecondGripReach = 0.25f;

    /// <summary>
    /// The item's pose with its grips <paramref name="grip1"/> and <paramref name="grip2"/> (item frame) held by hands
    /// at <paramref name="hand1"/> and <paramref name="hand2"/>. <paramref name="up1"/> and <paramref name="up2"/> are
    /// each hand's captured up in the world now; <paramref name="upLocal"/> is the item direction across its grip axis
    /// that follows them. <paramref name="axis"/> and <paramref name="up"/> carry the last good hand-to-hand axis and
    /// up: they are kept when the hands meet or their ups run along the axis, and updated otherwise.
    /// </summary>
    public static void Solve(
        Vector3 hand1,
        Vector3 hand2,
        Vector3 up1,
        Vector3 up2,
        Vector3 grip1,
        Vector3 grip2,
        Vector3 upLocal,
        ref Vector3 axis,
        ref Vector3 up,
        out Vector3 pos,
        out Quaternion rot
    )
    {
        var span = hand2 - hand1;
        if (span.Length() >= MinSpacing)
            axis = Vector3.Normalize(span);
        var across = Across(up1 + up2, axis);
        if (across.LengthSquared() > 1e-4f)
            up = Vector3.Normalize(across);
        else
        {
            // No roll to read: the last up, kept square to the axis.
            var kept = Across(up, axis);
            up = kept.LengthSquared() > 1e-8f ? Vector3.Normalize(kept) : AnyAcross(axis);
        }
        var a = grip2 - grip1;
        a = a.LengthSquared() > 1e-8f ? Vector3.Normalize(a) : new Vector3(1f, 0f, 0f);
        rot = Quaternion.Normalize(
            UnityQuat.Mul(Basis(axis, up), Quaternion.Inverse(Basis(a, UpLocal(a, upLocal))))
        );
        pos = (hand1 + hand2) * 0.5f - UnityQuat.Rotate(rot, (grip1 + grip2) * 0.5f);
    }

    /// <summary>
    /// At the join: the world up across <paramref name="axis"/> (the hands' axis) that keeps
    /// <paramref name="upLocal"/> of the item, held at <paramref name="itemRot"/> by one hand, where that hold showed
    /// it (an item's front stays toward the eyes), and that up in each hand's own frame
    /// (<paramref name="v1"/>, <paramref name="v2"/>), which <see cref="Solve"/> turns with the hands.
    /// </summary>
    public static void Capture(
        Quaternion itemRot,
        Vector3 upLocal,
        Vector3 axis,
        Quaternion hand1,
        Quaternion hand2,
        out Vector3 v1,
        out Vector3 v2,
        out Vector3 up
    )
    {
        var across = Across(UnityQuat.Rotate(itemRot, upLocal), axis);
        up = across.LengthSquared() > 1e-8f ? Vector3.Normalize(across) : AnyAcross(axis);
        v1 = UnityQuat.Rotate(Quaternion.Inverse(hand1), up);
        v2 = UnityQuat.Rotate(Quaternion.Inverse(hand2), up);
    }

    /// <summary>The unit direction across <paramref name="axisLocal"/> nearest <paramref name="preferred"/> (an
    /// item's front, else its up); any direction across it when the two are parallel.</summary>
    public static Vector3 UpLocal(Vector3 axisLocal, Vector3 preferred)
    {
        var across = Across(preferred, axisLocal);
        return across.LengthSquared() > 1e-6f ? Vector3.Normalize(across) : AnyAcross(axisLocal);
    }

    /// <summary>Where the second hand holds the item (item frame): its grip when the hand closed within
    /// <paramref name="reach"/> of it, else where the hand closed, on the item's body.</summary>
    public static Vector3 SecondGrip(Vector3 hand, bool hasGrip, Vector3 grip, float reach) =>
        hasGrip && Vector3.DistanceSquared(hand, grip) <= reach * reach ? grip : hand;

    /// <summary>How near (metres) the free hand comes to a held item's second grip before the grip shows its
    /// "would work here" ring: a short reach past where the grip takes it (<see cref="SecondGripReach"/>).</summary>
    public const float CueReach = 0.45f;

    /// <summary>The second grip shows its ring: the item takes a second hand, the other hand is open and doing
    /// nothing, has not joined yet and is within <see cref="CueReach"/> of the grip.</summary>
    public static bool ShowSecondGrip(
        bool takesSecondHand,
        bool handFree,
        bool joined,
        float distance
    ) => takesSecondHand && handFree && !joined && distance <= CueReach;

    /// <summary>How far the item has gone from its one-hand pose to the two-hand one, <paramref name="since"/> seconds
    /// after the join: eased over <see cref="HoldHands.EaseSeconds"/>, 0 before it.</summary>
    public static float Weight(float since) =>
        since <= 0f ? 0f : HoldHands.Ease(since, HoldHands.EaseSeconds);

    /// <summary>The pose <paramref name="w"/> of the way from (<paramref name="aPos"/>, <paramref name="aRot"/>) to
    /// (<paramref name="bPos"/>, <paramref name="bRot"/>).</summary>
    public static void Blend(
        Vector3 aPos,
        Quaternion aRot,
        Vector3 bPos,
        Quaternion bRot,
        float w,
        out Vector3 pos,
        out Quaternion rot
    )
    {
        w = Math.Clamp(w, 0f, 1f);
        pos = Vector3.Lerp(aPos, bPos, w);
        rot = Quaternion.Normalize(Quaternion.Slerp(aRot, bRot, w));
    }

    private static Vector3 Across(Vector3 v, Vector3 axis) => v - Vector3.Dot(v, axis) * axis;

    private static Vector3 AnyAcross(Vector3 axis)
    {
        var c = Vector3.Cross(axis, new Vector3(0f, 1f, 0f));
        if (c.LengthSquared() < 1e-4f)
            c = Vector3.Cross(axis, new Vector3(0f, 0f, 1f));
        return Vector3.Normalize(c);
    }

    /// <summary>The rotation taking +X to <paramref name="x"/> and +Y to <paramref name="y"/> (orthonormal).</summary>
    private static Quaternion Basis(Vector3 x, Vector3 y)
    {
        var z = Vector3.Cross(x, y);
        var m = new Matrix4x4(
            x.X,
            x.Y,
            x.Z,
            0f,
            y.X,
            y.Y,
            y.Z,
            0f,
            z.X,
            z.Y,
            z.Z,
            0f,
            0f,
            0f,
            0f,
            1f
        );
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
    }
}
