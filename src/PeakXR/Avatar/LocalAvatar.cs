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
/// metres from the body, so the head and hands hold their last tracked place on the body instead (for the local IK
/// and for peers alike).
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

    /// <summary>The W/Character shader's first-person ghost (HideTheBody.cs:31).</summary>
    private static readonly int BodyGhostId = Shader.PropertyToID("_VertexGhost");
    private readonly System.Collections.Generic.List<Material> materials = new();
    private bool loggedReghost;
    private string ghostedName;

    /// <summary>Last fully tracked head and hands in the body frame (physical hip, look yaw), held while the view is
    /// detached.</summary>
    private BodyPoint heldHead;
    private BodyPoint heldLeft;
    private BodyPoint heldRight;

    private struct BodyPoint
    {
        public bool Valid;
        public Vector3 Position;
        public Quaternion Rotation;

        public BodyPoint(bool valid, Vector3 p, Quaternion r, Vector3 root, Quaternion inv)
        {
            Valid = valid;
            Position = inv * (p - root);
            Rotation = inv * r;
        }

        public bool ToWorld(Vector3 root, Quaternion yaw, out Vector3 p, out Quaternion r)
        {
            p = root + yaw * Position;
            r = yaw * Rotation;
            return Valid;
        }
    }

    /// <summary>Raw finger curls from a controller's analog and touch inputs (no grip presets).</summary>
    public static FingerCurls ControllerCurls(bool left)
    {
        ref var h = ref VRInput.Hand(left);
        // The Touch thumbrest counts: a thumb resting there read as lifted, so every plain grip with the index off the
        // trigger was a finger gun whose interact waited for a ping that never came.
        bool thumb =
            h.PrimaryTouch
            || h.SecondaryTouch
            || h.StickTouch
            || h.ThumbrestTouch
            || h.Primary
            || h.Secondary;
        return FingerCurlMapper.Map(h.Trigger, h.TriggerTouch, h.Grip, thumb, h.SkeletalCurls);
    }

    private void Awake()
    {
        character = GetComponent<Character>();
    }

    private void LateUpdate()
    {
        bool active = VRSession.Active && character == Character.localCharacter;
        RefreshBodyVisibility(active);
        var rig = VRRig.Instance;
        if (!active || rig == null || rig.Head == null)
            return;
        // Created on the first VR frame, not in Awake: when XR fails to start this component stays inert and the
        // character keeps vanilla IK and held items (the driver takes over CharacterItems.HoldItem).
        if (driver == null)
            driver = AvatarPoseDriver.For(character);

        var pose = new AvatarPose
        {
            ItemInLeftHand = LocalHold.ItemInLeftHand,
            TwoHandHold = LocalHold.TwoHand,
        };
        if (VRInput.HeadTracked)
        {
            pose.HasHead = true;
            // The head as it stands over the body: without the view's follow lag, turn shift and crouch lift, which
            // are not the player leaning (peers get the same lean).
            pose.HeadPosition = rig.Head.position - RigDriver.ViewOffset;
            // The lean aims the animated eyes, which sit apart from the physical eyes the view is calibrated on
            // (run 28: 0.12 m, a 7.5 degree back bend standing on the spot). Send the head's offset from the
            // calibrated eyes applied to the animated ones, so the calibrated spot is upright here and for peers.
            // Both ends on this frame's pose: the animated eye is from the last physics step, so it rides the hip.
            if (
                RigDriver.LeanEye is { } calibrated
                && driver.UnleanedEye is { } eyeFromHip
                && character.refs.hip != null
            )
                pose.HeadPosition += BodyLean
                    .HeadBias(
                        eyeFromHip.N(),
                        character.refs.hip.Rig.transform.position.N(),
                        calibrated.N()
                    )
                    .U();
            pose.HeadRotation = rig.Head.rotation;
        }

        var item = character.data.currentItem;
        bool holding = hold.Bind(item);
        var grip = holding ? hold.Profile.Grip : GripPreset.Cylinder;
        // Fingers on the item stop at its drawn surface: the presets close on a handle, and on the game's big props
        // (an apple 0.42 m across) they sank into it.
        float radius = holding ? 0.5f * hold.Thickness * HeldItemScale.Factor(item) : 0f;
        float wrap = holding ? HandPose.WrapCurl(radius) : 1f;
        // The smaller drawn fingers stop sooner, on this client only (DrawnHands.Curls); peers get the pose's curls.
        float drawnWrap = holding ? HandScale.Wrap(radius, DrawnHands.Factor) : 1f;
        DrawnHands.WrapLeft = Role(holding, true) == HandRole.Free ? 1f : drawnWrap;
        DrawnHands.WrapRight = Role(holding, false) == HandRole.Free ? 1f : drawnWrap;
        float dt = Time.deltaTime;

        if (VRInput.Left.Tracked && rig.LeftHand != null)
        {
            pose.HasLeftHand = true;
            pose.LeftHandPosition = rig.LeftHand.position;
            pose.LeftHandRotation = rig.LeftHand.rotation;
            var target = HandPose.Fingers(ControllerCurls(true), Role(holding, true), grip, wrap);
            pose.LeftFingers = leftSmoother.Update(target, dt);
        }
        if (VRInput.Right.Tracked && rig.RightHand != null)
        {
            pose.HasRightHand = true;
            pose.RightHandPosition = rig.RightHand.position;
            pose.RightHandRotation = rig.RightHand.rotation;
            var target = HandPose.Fingers(ControllerCurls(false), Role(holding, false), grip, wrap);
            pose.RightFingers = rightSmoother.Update(target, dt);
        }

        if (!holding)
            pose.ItemInLeftHand = pose.TwoHandHold = false;
        HoldHandsOnBody(ref pose);
        driver.SetPose(pose);
    }

    /// <summary>On the body: remember the head and hands in the body frame while the arms are fully tracked.
    /// Detached: put them back on the body where it is now.</summary>
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
            heldHead = new(pose.HasHead, pose.HeadPosition, pose.HeadRotation, root, inv);
            heldLeft = new(
                pose.HasLeftHand,
                pose.LeftHandPosition,
                pose.LeftHandRotation,
                root,
                inv
            );
            heldRight = new(
                pose.HasRightHand,
                pose.RightHandPosition,
                pose.RightHandRotation,
                root,
                inv
            );
            return;
        }
        pose.HasHead = heldHead.ToWorld(root, yaw, out pose.HeadPosition, out pose.HeadRotation);
        pose.HasLeftHand = heldLeft.ToWorld(
            root,
            yaw,
            out pose.LeftHandPosition,
            out pose.LeftHandRotation
        );
        pose.HasRightHand = heldRight.ToWorld(
            root,
            yaw,
            out pose.RightHandPosition,
            out pose.RightHandRotation
        );
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
    /// <c>HideTheBody.Toggle</c> postfix applies the current mode. Also when the hidden body carries the game's
    /// first-person ghost again outside Vanilla mode: that ghost leaves only hands and feet (<see cref="BodyGhost"/>).</summary>
    private void RefreshBodyVisibility(bool active)
    {
        var mode = AvatarConfig.Instance.OwnBody;
        var hide = character.refs.hideTheBody;
        bool reghosted = active && hide != null && OutfitGhosted(hide, mode);
        if (active == lastActive && ReferenceEquals(mode, lastBodyMode) && !reghosted)
            return;
        if (reghosted && !loggedReghost)
        {
            loggedReghost = true;
            Plugin.Log.LogWarning(
                $"[avatar.local] own body ghosted again in mode {mode} ({ghostedName}); body hiding re-applied"
            );
        }
        lastActive = active;
        lastBodyMode = mode;
        if (hide != null)
            hide.Refresh();
    }

    /// <summary>Whether the hidden local body or its costumes still ghost (<c>_VertexGhost</c> above 0, set by
    /// HideTheBody.SetShowing, HideTheBody.cs:104-112) in a mode that shows them. Allocation-free.</summary>
    private bool OutfitGhosted(HideTheBody hide, string mode)
    {
        if (
            string.Equals(mode, AvatarConfig.ShowVanilla, System.StringComparison.Ordinal)
            || AvatarPatches.BodyShowing(hide)
        )
            return false;
        if (Ghosted(hide.body))
            return true;
        for (int i = 0; i < hide.costumes.Length; i++)
            if (Ghosted(hide.costumes[i]))
                return true;
        return false;
    }

    private bool Ghosted(Renderer r)
    {
        if (r == null)
            return false;
        r.GetSharedMaterials(materials);
        for (int i = 0; i < materials.Count; i++)
        {
            var m = materials[i];
            if (m != null && m.HasProperty(BodyGhostId) && m.GetFloat(BodyGhostId) > 0f)
            {
                if (!loggedReghost)
                    ghostedName = r.name + "/" + m.name;
                return true;
            }
        }
        return false;
    }
}
