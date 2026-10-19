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
}
