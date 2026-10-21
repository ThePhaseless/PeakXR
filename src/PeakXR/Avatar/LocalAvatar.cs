using PeakXR.Core;
using PeakXR.Rig;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Avatar;

/// <summary>
/// Builds the local VR player's <see cref="AvatarPose"/> from <see cref="VRRig"/> and <see cref="VRInput"/> every
/// frame and feeds the character's <see cref="AvatarPoseDriver"/>. Runs late so the rig has its pose for this
/// frame; the driver applies it on the next physics tick like any other character's pose.
/// While the view is detached from the body (third person, spectate, cutscene) the controllers are near the view,
/// metres from the body, so the hands hold their last tracked place on the body instead (for the local IK and for
/// peers alike).
/// </summary>
[DefaultExecutionOrder(1000)]
internal sealed class LocalAvatar : MonoBehaviour
{
    private Character character;
    private AvatarPoseDriver driver;
    private readonly FingerCurlSmoother leftSmoother = new();
    private readonly FingerCurlSmoother rightSmoother = new();
    private readonly ItemHoldInfo hold = new();
    private string lastBodyMode;
    private bool lastActive;

    /// <summary>Last fully tracked hands in the body frame (physical hip, look yaw), held while the view is detached.</summary>
    private BodyHand heldLeft;
    private BodyHand heldRight;

    private struct BodyHand
    {
        public bool Valid;
        public Vector3 Position;
        public Quaternion Rotation;
    }

    /// <summary>Raw finger curls from a controller's analog and touch inputs (no grip presets).</summary>
    public static FingerCurls ControllerCurls(bool left)
    {
        ref var h = ref VRInput.Hand(left);
        bool thumb = h.PrimaryTouch || h.SecondaryTouch || h.StickTouch || h.Primary || h.Secondary;
        return FingerCurlMapper.Map(h.Trigger, h.TriggerTouch, h.Grip, thumb, h.SkeletalCurls);
    }

    private void Awake()
    {
        character = GetComponent<Character>();
        driver = AvatarPoseDriver.For(character);
    }

    private void LateUpdate()
    {
        bool active = VRSession.Active && character == Character.localCharacter;
        RefreshBodyVisibility(active);
        var rig = VRRig.Instance;
        if (!active || rig == null || rig.Head == null)
            return;

        var pose = new AvatarPose
        {
            ItemInLeftHand = LocalHold.ItemInLeftHand,
            TwoHandHold = LocalHold.TwoHand,
        };
        if (VRInput.HeadTracked)
        {
            pose.HasHead = true;
            pose.HeadPosition = rig.Head.position;
            pose.HeadRotation = rig.Head.rotation;
        }

        var item = character.data.currentItem;
        bool holding = hold.Bind(item);
        var grip = holding ? hold.Profile.Grip : GripPreset.Cylinder;
        float dt = Time.deltaTime;

        if (VRInput.Left.Tracked && rig.LeftHand != null)
        {
            pose.HasLeftHand = true;
            pose.LeftHandPosition = rig.LeftHand.position;
            pose.LeftHandRotation = rig.LeftHand.rotation;
            var target = HandPose.Fingers(ControllerCurls(true), Role(holding, true), grip);
            pose.LeftFingers = leftSmoother.Update(target, dt);
        }
        if (VRInput.Right.Tracked && rig.RightHand != null)
        {
            pose.HasRightHand = true;
            pose.RightHandPosition = rig.RightHand.position;
            pose.RightHandRotation = rig.RightHand.rotation;
            var target = HandPose.Fingers(ControllerCurls(false), Role(holding, false), grip);
            pose.RightFingers = rightSmoother.Update(target, dt);
        }

        if (!holding)
            pose.ItemInLeftHand = pose.TwoHandHold = false;
        HoldHandsOnBody(ref pose);
        driver.SetPose(pose);
    }

    /// <summary>On the body: remember the hands in the body frame while the arms are fully tracked. Detached: put the
    /// remembered hands back on the body where it is now.</summary>
    private void HoldHandsOnBody(ref AvatarPose pose)
    {
        var hip = character.refs.hip;
        if (hip == null || hip.Rig == null)
            return;
        var root = hip.Rig.position;
        var yaw = Quaternion.Euler(0f, character.data.lookValues.x, 0f);
        if (RigDriver.ViewOnBody)
        {
            if (driver.ArmWeight < 1f)
                return;
            var inv = Quaternion.Inverse(yaw);
            heldLeft = new BodyHand
            {
                Valid = pose.HasLeftHand,
                Position = inv * (pose.LeftHandPosition - root),
                Rotation = inv * pose.LeftHandRotation,
            };
            heldRight = new BodyHand
            {
                Valid = pose.HasRightHand,
                Position = inv * (pose.RightHandPosition - root),
                Rotation = inv * pose.RightHandRotation,
            };
            return;
        }
        pose.HasLeftHand = heldLeft.Valid;
        pose.LeftHandPosition = root + yaw * heldLeft.Position;
        pose.LeftHandRotation = yaw * heldLeft.Rotation;
        pose.HasRightHand = heldRight.Valid;
        pose.RightHandPosition = root + yaw * heldRight.Position;
        pose.RightHandRotation = yaw * heldRight.Rotation;
    }

    private static HandRole Role(bool holding, bool left)
    {
        if (!holding)
            return HandRole.Free;
        if (LocalHold.ItemInLeftHand == left)
            return HandRole.Holding;
        return LocalHold.TwoHand ? HandRole.Support : HandRole.Free;
    }

    /// <summary>Re-run the game's body hiding when VR starts or the setting changes, so the
    /// <c>HideTheBody.Toggle</c> postfix applies the current mode.</summary>
    private void RefreshBodyVisibility(bool active)
    {
        var mode = AvatarConfig.Instance.OwnBody;
        if (active == lastActive && ReferenceEquals(mode, lastBodyMode))
            return;
        lastActive = active;
        lastBodyMode = mode;
        if (character.refs.hideTheBody != null)
            character.refs.hideTheBody.Refresh();
    }
}
