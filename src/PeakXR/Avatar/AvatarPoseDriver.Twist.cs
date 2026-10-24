using System.Collections.Generic;
using PeakXR.Core;
using UnityEngine;

namespace PeakXR.Avatar;

/// <summary>
/// Wrist roll of tracked hands (<see cref="ArmTwist"/>). The arm IK gives the hand bone the controller's full rotation
/// and leaves the forearm at the animation's roll, and the scout has no twist bone, so a rolled wrist twisted the
/// skin at one joint and, near a half turn, the ragdoll's wrist drive (<c>Bodypart.FollowRotation_Joint</c>,
/// Bodypart.cs:175-181, joint limits RigCreator.cs:171-185) and its axis-alignment torque (Bodypart.cs:183-191, whose
/// cross product has no direction at 180 degrees) wound the arm one way and back around the hand. Here the roll is
/// shared with the forearm in the animated pose, before <c>SaveAnimationData</c> (CharacterRagdoll.cs:170-174)
/// hands it to the ragdoll; both joints stay within a natural quarter turn.
/// </summary>
public partial class AvatarPoseDriver
{
    private Transform forearmL;
    private Transform forearmR;
    private Transform wristL;
    private Transform wristR;

    /// <summary>The hand's bind-pose rotation in its forearm's frame: the untwisted wrist.</summary>
    private Quaternion wristRestL = Quaternion.identity;
    private Quaternion wristRestR = Quaternion.identity;

    /// <summary>Last tick's unwrapped wrist twist (radians) per hand, so the roll keeps its side past a half turn.</summary>
    private float twistL;
    private float twistR;

    private void InitTwist(
        Dictionary<BodypartType, Bodypart> parts,
        Transform handL,
        Transform handR,
        SkinnedMeshRenderer skin
    )
    {
        wristL = handL;
        wristR = handR;
        forearmL = parts.TryGetValue(BodypartType.Elbow_L, out var el) ? el.transform : null;
        forearmR = parts.TryGetValue(BodypartType.Elbow_R, out var er) ? er.transform : null;
        wristRestL = RestWrist(forearmL, handL, skin);
        wristRestR = RestWrist(forearmR, handR, skin);
    }

    /// <summary>The hand's rotation in the forearm's frame from the skin's bind pose, else as the prefab has it now.</summary>
    private static Quaternion RestWrist(Transform forearm, Transform hand, SkinnedMeshRenderer skin)
    {
        if (forearm == null)
            return Quaternion.identity;
        if (skin != null && skin.sharedMesh != null)
        {
            var bones = skin.bones;
            var poses = skin.sharedMesh.bindposes;
            int f = System.Array.IndexOf(bones, forearm);
            int h = System.Array.IndexOf(bones, hand);
            if (f >= 0 && h >= 0 && f < poses.Length && h < poses.Length)
                return (poses[f] * poses[h].inverse).rotation;
        }
        return Quaternion.Inverse(forearm.rotation) * hand.rotation;
    }

    /// <summary>Shares one tracked hand's roll with its forearm in the animated pose: the forearm turns about its own
    /// axis (elbow and hand stay put), the hand keeps its rotation up to <see cref="ArmTwist.MaxWrist"/> on each
    /// joint and always its pointing direction. <paramref name="on"/> false forgets the twist history.</summary>
    private void SpreadTwist(bool left, bool on)
    {
        ref float twist = ref left ? ref twistL : ref twistR;
        var forearm = left ? forearmL : forearmR;
        var hand = left ? wristL : wristR;
        if (!on || forearm == null || hand == null)
        {
            twist = 0f;
            return;
        }
        var turn = forearm.rotation;
        var inv = Quaternion.Inverse(turn);
        var axis = inv * (hand.position - forearm.position);
        if (axis.sqrMagnitude < 1e-8f)
            return;
        var rest = left ? wristRestL : wristRestR;
        var relative = inv * hand.rotation * Quaternion.Inverse(rest);
        var wrist = ArmTwist.Spread(relative.N(), axis.normalized.N(), ref twist, out var roll).U();
        turn *= roll.U();
        forearm.rotation = turn;
        hand.rotation = turn * wrist * rest;
    }

    /// <summary>The ragdoll wrist's twist off its rest pose about the forearm (physics bodies), degrees in
    /// [-180, 180]; false before the rig is set up. Read by the self-test (vr.08).</summary>
    internal bool WristTwist(bool left, out float degrees)
    {
        degrees = 0f;
        var forearm = left ? forearmL : forearmR;
        var hand = left ? wristL : wristR;
        if (!rigReady || forearm == null || hand == null)
            return false;
        var f = forearm.GetComponent<Rigidbody>();
        var h = hand.GetComponent<Rigidbody>();
        if (f == null || h == null)
            return false;
        var inv = Quaternion.Inverse(f.rotation);
        var axis = (inv * (h.position - f.position)).normalized;
        var relative = inv * h.rotation * Quaternion.Inverse(left ? wristRestL : wristRestR);
        degrees = ArmTwist.TwistAngle(relative.N(), axis.N()) * Mathf.Rad2Deg;
        return true;
    }
}
