using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Converts a pose between world space and the character frame used on the wire: origin at the
/// physical hip, rotated by the look yaw. Both values are already synced by vanilla
/// (<c>hipLocation</c> and <c>lookValues.x</c>), so sender and receiver rebuild the same frame
/// for a character and hands stay attached to the body while it walks or turns.
/// <para>The receiver plays poses back late (<see cref="PoseBuffer.Delay"/>), while the yaw the game syncs is
/// current. A VR player's look yaw follows the head, so the pose is rebuilt with the yaw of the playback moment
/// (<see cref="YawHistory"/>, sampled at the same now minus delay), not the current one: otherwise hands swing ahead
/// on every head turn and jump on snap turns. The hip stays current on purpose, so hands keep up with a walking
/// body.</para>
/// </summary>
public static class PoseFrame
{
    static readonly Vector3 Up = new Vector3(0f, 1f, 0f);

    public static PosePacket ToLocal(in PosePacket world, Vector3 root, float yawDeg)
    {
        var inv = Quaternion.CreateFromAxisAngle(Up, -yawDeg * UnityQuat.Deg2Rad);
        var p = world;
        p.HeadPosition = UnityQuat.Rotate(inv, world.HeadPosition - root);
        p.HeadRotation = UnityQuat.Mul(inv, world.HeadRotation);
        p.LeftHandPosition = UnityQuat.Rotate(inv, world.LeftHandPosition - root);
        p.LeftHandRotation = UnityQuat.Mul(inv, world.LeftHandRotation);
        p.RightHandPosition = UnityQuat.Rotate(inv, world.RightHandPosition - root);
        p.RightHandRotation = UnityQuat.Mul(inv, world.RightHandRotation);
        return p;
    }

    public static PosePacket ToWorld(in PosePacket local, Vector3 root, float yawDeg)
    {
        var yaw = Quaternion.CreateFromAxisAngle(Up, yawDeg * UnityQuat.Deg2Rad);
        var p = local;
        p.HeadPosition = root + UnityQuat.Rotate(yaw, local.HeadPosition);
        p.HeadRotation = UnityQuat.Mul(yaw, local.HeadRotation);
        p.LeftHandPosition = root + UnityQuat.Rotate(yaw, local.LeftHandPosition);
        p.LeftHandRotation = UnityQuat.Mul(yaw, local.LeftHandRotation);
        p.RightHandPosition = root + UnityQuat.Rotate(yaw, local.RightHandPosition);
        p.RightHandRotation = UnityQuat.Mul(yaw, local.RightHandRotation);
        return p;
    }
}

/// <summary>Blending and change detection for <see cref="PosePacket"/>s.</summary>
public static class PoseBlend
{
    /// <summary>
    /// Interpolates two poses. Parts present in both are blended; a part present in only one side
    /// takes that side's value. Flags and sequence switch from <paramref name="a"/> to
    /// <paramref name="b"/> at the halfway point.
    /// </summary>
    public static PosePacket Lerp(in PosePacket a, in PosePacket b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        var p = t < 0.5f ? a : b;
        BlendPart(
            a.HasHead,
            b.HasHead,
            a.HeadPosition,
            b.HeadPosition,
            a.HeadRotation,
            b.HeadRotation,
            t,
            out p.HeadPosition,
            out p.HeadRotation
        );
        BlendPart(
            a.HasLeftHand,
            b.HasLeftHand,
            a.LeftHandPosition,
            b.LeftHandPosition,
            a.LeftHandRotation,
            b.LeftHandRotation,
            t,
            out p.LeftHandPosition,
            out p.LeftHandRotation
        );
        BlendPart(
            a.HasRightHand,
            b.HasRightHand,
            a.RightHandPosition,
            b.RightHandPosition,
            a.RightHandRotation,
            b.RightHandRotation,
            t,
            out p.RightHandPosition,
            out p.RightHandRotation
        );
        if (a.HasFingers && b.HasFingers)
        {
            for (int i = 0; i < FingerCurls.Count; i++)
            {
                p.LeftFingers[i] = a.LeftFingers[i] + (b.LeftFingers[i] - a.LeftFingers[i]) * t;
                p.RightFingers[i] = a.RightFingers[i] + (b.RightFingers[i] - a.RightFingers[i]) * t;
            }
        }
        else if (b.HasFingers)
        {
            p.LeftFingers = b.LeftFingers;
            p.RightFingers = b.RightFingers;
        }
        else
        {
            p.LeftFingers = a.LeftFingers;
            p.RightFingers = a.RightFingers;
        }
        return p;
    }

    static void BlendPart(
        bool hasA,
        bool hasB,
        Vector3 pa,
        Vector3 pb,
        Quaternion ra,
        Quaternion rb,
        float t,
        out Vector3 pos,
        out Quaternion rot
    )
    {
        if (hasA && hasB)
        {
            pos = Vector3.Lerp(pa, pb, t);
            rot = Quaternion.Normalize(Quaternion.Slerp(ra, rb, t));
        }
        else if (hasB)
        {
            pos = pb;
            rot = rb;
        }
        else
        {
            pos = pa;
            rot = ra;
        }
    }

    /// <summary>
    /// True when <paramref name="b"/> differs from <paramref name="a"/> enough to be worth sending:
    /// different flags, a present part moved more than <paramref name="posEps"/> metres or turned more than
    /// <paramref name="rotEpsDeg"/> degrees, or a finger curl changed more than <paramref name="fingerEps"/>.
    /// </summary>
    public static bool Differs(
        in PosePacket a,
        in PosePacket b,
        float posEps,
        float rotEpsDeg,
        float fingerEps
    )
    {
        if (a.Flags != b.Flags)
            return true;
        float minDot = MathF.Cos(rotEpsDeg * 0.5f * UnityQuat.Deg2Rad);
        float posEpsSq = posEps * posEps;
        if (
            a.HasHead
            && PartDiffers(
                a.HeadPosition,
                b.HeadPosition,
                a.HeadRotation,
                b.HeadRotation,
                posEpsSq,
                minDot
            )
        )
            return true;
        if (
            a.HasLeftHand
            && PartDiffers(
                a.LeftHandPosition,
                b.LeftHandPosition,
                a.LeftHandRotation,
                b.LeftHandRotation,
                posEpsSq,
                minDot
            )
        )
            return true;
        if (
            a.HasRightHand
            && PartDiffers(
                a.RightHandPosition,
                b.RightHandPosition,
                a.RightHandRotation,
                b.RightHandRotation,
                posEpsSq,
                minDot
            )
        )
            return true;
        if (a.HasFingers)
        {
            for (int i = 0; i < FingerCurls.Count; i++)
            {
                if (
                    MathF.Abs(a.LeftFingers[i] - b.LeftFingers[i]) > fingerEps
                    || MathF.Abs(a.RightFingers[i] - b.RightFingers[i]) > fingerEps
                )
                    return true;
            }
        }
        return false;
    }

    static bool PartDiffers(
        Vector3 pa,
        Vector3 pb,
        Quaternion ra,
        Quaternion rb,
        float posEpsSq,
        float minDot
    )
    {
        if (Vector3.DistanceSquared(pa, pb) > posEpsSq)
            return true;
        float dot = MathF.Abs(Quaternion.Dot(Quaternion.Normalize(ra), Quaternion.Normalize(rb)));
        return dot < minDot;
    }
}
