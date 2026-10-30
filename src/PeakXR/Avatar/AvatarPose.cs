using PeakXR.Core;
using UnityEngine;

namespace PeakXR.Avatar;

/// <summary>
/// A tracked body pose for one character, in world space. The local VR player produces one from
/// the rig; remote poses arrive over the network. Either way it is applied by
/// <see cref="AvatarPoseDriver"/>.
/// </summary>
public struct AvatarPose
{
    public bool HasHead;
    public Vector3 HeadPosition;
    public Quaternion HeadRotation;

    public bool HasLeftHand;
    public Vector3 LeftHandPosition;
    public Quaternion LeftHandRotation;
    public FingerCurls LeftFingers;

    public bool HasRightHand;
    public Vector3 RightHandPosition;
    public Quaternion RightHandRotation;
    public FingerCurls RightFingers;

    public bool Seated;

    /// <summary>The held item sits in the left hand (mirrored hold). False means the right hand, as in vanilla.</summary>
    public bool ItemInLeftHand;

    /// <summary>The free hand also grips the held item: the item is aimed between both hands.</summary>
    public bool TwoHandHold;

    /// <summary>The controllers' aim poses relative to their grips are known (<see cref="LeftAimInGrip"/>,
    /// <see cref="RightAimInGrip"/>): the local rig's, or a modded peer's from its pose stream. A held barrel lies
    /// along that aim.</summary>
    public bool HasAim;

    public Quaternion LeftAimInGrip;
    public Quaternion RightAimInGrip;
}
