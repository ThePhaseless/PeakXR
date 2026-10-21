using PeakXR.Core;
using UnityEngine;

namespace PeakXR.Avatar;

/// <summary>
/// Apply side of the driver. Runs from Harmony postfixes on the character's own animation pass
/// (see <see cref="AvatarPatches"/>), so it works the same for the local VR player and for remote
/// characters fed by the network. Nothing here assumes local-only components.
///
/// Spaces: the game animates a hidden copy of the skeleton and pulls the ragdoll toward it. IK targets live in
/// that animated space, which is the physical world shifted by (animated hip - physical hip); rotations are
/// shared (doc 6.3).
/// </summary>
public partial class AvatarPoseDriver
{
    /// <summary>Scout rig finger chains: thumb, index, middle, ring (absent on the scout), pinky;
    /// three joints each. The right thumb uses a different naming pattern.</summary>
    private static readonly string[][] FingerNamesLeft =
    {
        new[] { "Thumb_1_L", "Thumb_2_L", "Thumb_3_L" },
        new[] { "Index_1_L", "Index_2_L", "Index_3_L" },
        new[] { "Middle_1_L", "Middle_2_L", "Middle_3_L" },
        new[] { "Ring_1_L", "Ring_2_L", "Ring_3_L" },
        new[] { "Pinky_1_L", "Pinky_2_L", "Pinky_3_L" },
    };

    private static readonly string[][] FingerNamesRight =
    {
        new[] { "Thumb_R_1", "Thumb_R_2", "Thumb_R_3" },
        new[] { "Index_1_R", "Index_2_R", "Index_3_R" },
        new[] { "Middle_1_R", "Middle_2_R", "Middle_3_R" },
        new[] { "Ring_1_R", "Ring_2_R", "Ring_3_R" },
        new[] { "Pinky_1_R", "Pinky_2_R", "Pinky_3_R" },
    };

    private bool rigTried;
    private bool rigReady;
    private Bodypart hip;
    private Bodypart head;
    private Bodypart shoulderL;
    private Bodypart shoulderR;
    private Transform bodyFrame;

    /// <summary>The prefab's elbow hint weights (0 on the scout), put back when a hand stops being tracked.</summary>
    private float hintWeightL;
    private float hintWeightR;
    private bool hintOnL;
    private bool hintOnR;
    private HandRig leftRig;
    private HandRig rightRig;
    private readonly ItemHoldInfo hold = new();
    private readonly ArmBlend armBlend = new();
    private FixedJoint itemJoint;
    private bool itemJointLeft;

    /// <summary>The item whose vanilla two-hand weld was last checked for replacement; one try per item.</summary>
    private Item vanillaWeldChecked;

    /// <summary>True while this tick's held-item frame comes from the tracked hand; read by the hold torque patch.</summary>
    internal bool ItemFrameOverridden { get; private set; }

    /// <summary>How much the tracked hands drive the arms now (0 while the game ragdolls the body, eased back in).
    /// Read by the self-test.</summary>
    internal float ArmWeight => armBlend.Weight;

    /// <summary>True when the rig has finger bones that the driver can curl.</summary>
    public bool HasFingerBones => rigReady && (leftRig.HasFingers || rightRig.HasFingers);

    private Character Owner =>
        Character != null ? Character : (Character = GetComponent<Character>());

    private bool EnsureRig()
    {
        if (rigTried)
            return rigReady;
        var c = Owner;
        if (c == null || c.refs?.ragdoll == null || c.refs.ragdoll.partDict.Count == 0)
            return false;
        rigTried = true;

        var parts = c.refs.ragdoll.partDict;
        if (
            !parts.TryGetValue(BodypartType.Hip, out hip)
            || !parts.TryGetValue(BodypartType.Hand_L, out var handL)
            || !parts.TryGetValue(BodypartType.Hand_R, out var handR)
        )
        {
            Plugin.Log.LogWarning(
                $"[avatar] {c.name}: no hip or hand bodyparts, pose driver disabled for it"
            );
            return false;
        }
        parts.TryGetValue(BodypartType.Head, out head);
        parts.TryGetValue(BodypartType.Arm_L, out shoulderL);
        parts.TryGetValue(BodypartType.Arm_R, out shoulderR);
        bodyFrame = c.refs.rigCreator != null ? c.refs.rigCreator.transform : c.transform;
        if (c.refs.ikLeft != null)
            hintWeightL = c.refs.ikLeft.data.hintWeight;
        if (c.refs.ikRight != null)
            hintWeightR = c.refs.ikRight.data.hintWeight;

        var smr = c.refs.mainRenderer;
        leftRig = new HandRig(handL.transform, true, FingerNamesLeft, smr);
        rightRig = new HandRig(handR.transform, false, FingerNamesRight, smr);
        if (!leftRig.HasFingers && !rightRig.HasFingers)
            Plugin.Log.LogInfo($"[avatar] {c.name}: no finger bones found, finger curls skipped");
        rigReady = true;
        return true;
    }

    private Vector3 AnimatedOffset() => hip.transform.position - hip.Rig.position;

    private void LateUpdate()
    {
        var c = Owner;
        if (HasFreshPose && c != null)
        {
            var d = c.data;
            bool down = d.passedOut || d.fullyPassedOut || d.dead || d.isCarried;
            armBlend.Update(
                ArmBlend.Tracked(down, d.fallSeconds, d.currentRagdollControll),
                Time.deltaTime
            );
        }
        else
        {
            armBlend.Reset();
        }
        // Animation only runs in FixedUpdate; refresh fingers every rendered frame so curls stay smooth at 90 Hz+.
        if (rigReady && HasFreshPose)
            ApplyFingers();
        else if (rigReady)
            RestoreFingers();
        if (
            (hintOnL || hintOnR)
            && (
                !HasFreshPose
                || ArmsSuspended
                || !AvatarConfig.Instance.ArmIK
                || armBlend.Weight <= 0f
            )
        )
        {
            SetHint(true, false, default, 0f);
            SetHint(false, false, default, 0f);
        }
    }

    /// <summary>After <c>CharacterAnimations.ConfigureIK</c>: bones are in the animated pose and IK targets were
    /// just written by vanilla. Blend the hand targets toward the tracked hands by <see cref="ArmWeight"/>, add head
    /// look, curl fingers.</summary>
    internal void OnConfigureIK()
    {
        if (!HasFreshPose)
        {
            if (rigReady)
                RestoreFingers();
            return;
        }
        if (!EnsureRig())
            return;
        var refs = Owner.refs;
        var cfg = AvatarConfig.Instance;

        float w = armBlend.Weight;
        if (
            cfg.ArmIK
            && !ArmsSuspended
            && w > 0f
            && refs.IKHandTargetLeft != null
            && refs.IKHandTargetRight != null
        )
        {
            var offset = AnimatedOffset();
            var pose = Pose;
            if (pose.HasLeftHand)
            {
                leftRig.BoneFromController(
                    pose.LeftHandPosition,
                    pose.LeftHandRotation,
                    out var p,
                    out var r
                );
                BlendTarget(refs.IKHandTargetLeft, p + offset, r, w);
            }
            SetHint(true, pose.HasLeftHand, refs.IKHandTargetLeft.position, w);
            if (pose.HasRightHand)
            {
                rightRig.BoneFromController(
                    pose.RightHandPosition,
                    pose.RightHandRotation,
                    out var p,
                    out var r
                );
                BlendTarget(refs.IKHandTargetRight, p + offset, r, w);
            }
            SetHint(false, pose.HasRightHand, refs.IKHandTargetRight.position, w);
        }

        // The local player's head is hidden and the camera hangs off it, so only other characters get head look.
        if (cfg.HeadLook && head != null && Pose.HasHead && !Owner.IsLocal)
        {
            var look = Owner.data.lookValues;
            var delta = GripMath.HeadDelta(Pose.HeadRotation.N(), look.x, look.y, 70f).U();
            head.transform.rotation = delta * head.transform.rotation;
        }

        ApplyFingers();
    }

    /// <summary>Moves an IK target <paramref name="w"/> of the way from where vanilla (or the last tick) left it to
    /// the tracked pose; at 1 it is the tracked pose.</summary>
    private static void BlendTarget(Transform target, Vector3 pos, Quaternion rot, float w) =>
        target.SetPositionAndRotation(
            Vector3.Lerp(target.position, pos, w),
            Quaternion.Slerp(target.rotation, rot, w)
        );

    /// <summary>
    /// Turns one arm's elbow hint on (pointed down, out and back from the shoulder toward
    /// <paramref name="hand"/>, all in animated space) or back to the prefab weight. The scout's
    /// <c>IK_Arm_Left/Right</c> constraints carry an <c>Elbow</c> hint at weight 0, so without this the solver keeps the
    /// idle animation's bend plane (<c>AnimationRuntimeUtils.SolveTwoBoneIK</c>) and raised hands get sideways elbows.
    /// </summary>
    private void SetHint(bool left, bool on, Vector3 hand, float w)
    {
        var ik = left ? Owner.refs.ikLeft : Owner.refs.ikRight;
        var shoulder = left ? shoulderL : shoulderR;
        if (ik == null || shoulder == null || ik.data.hint == null)
            return;
        ref var data = ref ik.data;
        if (on)
        {
            data.hint.position = ArmMath
                .ElbowHint(
                    shoulder.transform.position.N(),
                    hand.N(),
                    bodyFrame.right.N(),
                    bodyFrame.up.N(),
                    bodyFrame.forward.N(),
                    left
                )
                .U();
            data.hintWeight = Mathf.Lerp(left ? hintWeightL : hintWeightR, 1f, w);
        }
        else if (left ? hintOnL : hintOnR)
            data.hintWeight = left ? hintWeightL : hintWeightR;
        if (left)
            hintOnL = on;
        else
            hintOnR = on;
    }

    /// <summary>The wrist bone pose a tracked hand asks for, in physics (world) space; false when the hand is not
    /// tracked. The self-test compares it with where the ragdoll hand really is.</summary>
    internal bool HandTarget(bool left, out Vector3 pos)
    {
        pos = default;
        var pose = Pose;
        if (!HasFreshPose || !EnsureRig() || (left ? !pose.HasLeftHand : !pose.HasRightHand))
            return false;
        (left ? leftRig : rightRig).BoneFromController(
            left ? pose.LeftHandPosition : pose.RightHandPosition,
            left ? pose.LeftHandRotation : pose.RightHandRotation,
            out pos,
            out _
        );
        return true;
    }

    /// <summary>After <c>CharacterAnimations.HandleIK</c>: arms follow their own controllers, blended with vanilla's
    /// weights by <see cref="ArmWeight"/> so they go limp while the game ragdolls the body.</summary>
    internal void OnHandleIK()
    {
        float w = armBlend.Weight;
        if (!HasFreshPose || !AvatarConfig.Instance.ArmIK || ArmsSuspended || w <= 0f)
            return;
        var refs = Owner.refs;
        if (refs.ikRig == null || refs.ikLeft == null || refs.ikRight == null)
            return;
        bool vanillaOff = refs.ikRig.weight <= 0f;
        if (vanillaOff)
        {
            // Vanilla had IK off: the constraint weights are leftovers from the last held item. The rig weight
            // carries the blend instead.
            refs.ikLeft.weight = 0f;
            refs.ikRight.weight = 0f;
        }
        refs.ikRig.weight = Mathf.Lerp(refs.ikRig.weight, 1f, w);
        if (Pose.HasLeftHand)
            refs.ikLeft.weight = vanillaOff ? 1f : Mathf.Lerp(refs.ikLeft.weight, 1f, w);
        if (Pose.HasRightHand)
            refs.ikRight.weight = vanillaOff ? 1f : Mathf.Lerp(refs.ikRight.weight, 1f, w);
    }

    /// <summary>
    /// After <c>CharacterRagdoll.SaveAdditionalTransformPositions</c>: replace the look-based held-item frame with
    /// one carried by the holding hand. Both the hand IK targets and the item hold force derive from this frame,
    /// so the item follows the real hand on this client. Vanilla peers keep computing it from look values.
    /// </summary>
    internal void OnSaveAdditional()
    {
        ItemFrameOverridden = false;
        ReconcileWeld();
        float w = armBlend.Weight;
        if (w <= 0f || !TryGetItemWorldPose(out var itemPos, out var itemRot))
            return;
        var refs = Owner.refs;
        var offset = refs.animationHipTransform.position - hip.Rig.position;
        BlendTarget(refs.animationItemTransform, itemPos + offset, itemRot, w);
        ItemFrameOverridden = true;
    }

    /// <summary>
    /// Replaces vanilla's two-hand weld (<c>CharacterItems.AttachItem</c>) with a weld to the holding hand only,
    /// so the other hand stays free. Returns false to let vanilla attach.
    /// </summary>
    internal bool AttachToHoldingHand(Item item)
    {
        // The weld snaps the hand body onto the tracked pose: never while the arms are (partly) released.
        if (
            !HasFreshPose
            || ArmsSuspended
            || armBlend.Weight < 1f
            || !AvatarConfig.Instance.RetargetItems
            || !EnsureRig()
            || !hold.Bind(item)
        )
            return false;
        bool left = Pose.ItemInLeftHand;
        var rig = left ? leftRig : rightRig;
        if (!ItemInController(rig, out var xPos, out var xRot))
            return false;

        // bone = item * X^-1 * T
        var invRot = Quaternion.Inverse(xRot);
        var itemToCtrlPos = invRot * -xPos;
        var ctrlPos = item.transform.position + item.transform.rotation * itemToCtrlPos;
        var ctrlRot = item.transform.rotation * invRot;
        rig.BoneFromController(ctrlPos, ctrlRot, out var bonePos, out var boneRot);

        var handBody = Owner
            .refs
            .ragdoll
            .partDict[left ? BodypartType.Hand_L : BodypartType.Hand_R]
            .Rig;
        handBody.transform.SetPositionAndRotation(bonePos, boneRot);
        DestroyItemJoint();
        DestroyHandJoint(BodypartType.Hand_L, item.rig);
        DestroyHandJoint(BodypartType.Hand_R, item.rig);
        itemJoint = handBody.gameObject.AddComponent<FixedJoint>();
        itemJoint.connectedBody = item.rig;
        itemJointLeft = left;
        return true;
    }

    /// <summary>
    /// The weld is chosen once at attach (about four physics ticks after the equip RPC), from a pose that for a
    /// remote player is ~100 ms old, so it can pick the wrong hand; vanilla's two-hand weld can also predate a fresh
    /// pose (late join, entering VR while holding, an equip during a full-body emote). Re-weld when they disagree.
    /// </summary>
    private void ReconcileWeld()
    {
        var item = Owner.data.currentItem;
        if (item == null || ArmsSuspended || armBlend.Weight < 1f)
            return;
        if (itemJoint != null)
        {
            if (itemJointLeft != Pose.ItemInLeftHand)
                AttachToHoldingHand(item);
            return;
        }
        // No weld of ours: vanilla's, once its attach has settled (CharacterItems.cs:617, then :632).
        if (vanillaWeldChecked != item && Owner.data.sinceItemAttach > 0.5f)
        {
            vanillaWeldChecked = item;
            AttachToHoldingHand(item);
        }
    }

    private void DestroyHandJoint(BodypartType part, Rigidbody itemRig)
    {
        var body = Owner.refs.ragdoll.partDict[part].Rig;
        if (body.TryGetComponent(out FixedJoint joint) && joint.connectedBody == itemRig)
            Destroy(joint);
    }

    /// <summary>After <c>CharacterItems.UnAttachItem</c>: vanilla only removes the right-hand joint when a
    /// left one existed, so remove ours explicitly.</summary>
    internal void DestroyItemJoint()
    {
        if (itemJoint != null)
            Destroy(itemJoint);
        itemJoint = null;
    }

    private bool TryGetItemWorldPose(out Vector3 pos, out Quaternion rot)
    {
        pos = default;
        rot = Quaternion.identity;
        var c = Owner;
        var item = c.data.currentItem;
        if (
            !HasFreshPose
            || ArmsSuspended
            || item == null
            || !AvatarConfig.Instance.RetargetItems
            || !EnsureRig()
            || !hold.Bind(item)
        )
            return false;

        var pose = Pose;
        bool left = pose.ItemInLeftHand;
        if (left ? !pose.HasLeftHand : !pose.HasRightHand)
            return false;
        var rig = left ? leftRig : rightRig;
        if (!ItemInController(rig, out var xPos, out var xRot))
            return false;

        var ctrlPos = left ? pose.LeftHandPosition : pose.RightHandPosition;
        var ctrlRot = left ? pose.LeftHandRotation : pose.RightHandRotation;
        rot = ctrlRot * xRot;
        pos = ctrlPos + ctrlRot * xPos;

        if (pose.TwoHandHold && (left ? pose.HasRightHand : pose.HasLeftHand))
        {
            // Pivot the item about the holding hand so the grip-to-grip axis points at the supporting hand.
            rig.BoneFromController(ctrlPos, ctrlRot, out var anchor, out _);
            var anchorLocal = Quaternion.Inverse(rot) * (anchor - pos);
            var axis = SupportAxis(item, anchorLocal);
            var support = left ? pose.RightHandPosition : pose.LeftHandPosition;
            var aligned = GripMath.AlignAxis(rot.N(), axis.N(), (support - anchor).N()).U();
            pos = anchor - aligned * anchorLocal;
            rot = aligned;
        }
        return true;
    }

    /// <summary>Item axis from the holding hand to the farther vanilla grip, else the item's forward.</summary>
    private Vector3 SupportAxis(Item item, Vector3 anchorLocal)
    {
        var axis = Vector3.forward;
        float best = 0.1f;
        Consider(hold.GripLeft, item.transform, anchorLocal, ref axis, ref best);
        Consider(hold.GripRight, item.transform, anchorLocal, ref axis, ref best);
        return axis;

        static void Consider(
            Transform grip,
            Transform itemTransform,
            Vector3 anchorLocal,
            ref Vector3 axis,
            ref float best
        )
        {
            if (grip == null)
                return;
            var local = itemTransform.InverseTransformPoint(grip.position) - anchorLocal;
            if (local.magnitude > best)
            {
                best = local.magnitude;
                axis = local;
            }
        }
    }

    /// <summary>
    /// Item pose in a controller's frame: the right hand puts the item's <c>Hand_R</c> grip on its hand bone;
    /// the left hand holds the mirror image of that (vanilla clients keep showing it in the right hand).
    /// </summary>
    private bool ItemInController(HandRig rig, out Vector3 pos, out Quaternion rot)
    {
        pos = default;
        rot = Quaternion.identity;
        var item = Owner.data.currentItem;
        var grip = hold.GripRight != null ? hold.GripRight : hold.GripLeft;
        if (item == null)
            return false;

        Vector3 gripPos;
        Quaternion gripRot;
        if (grip != null)
        {
            // Same convention as CharacterItems.GetItemPosRight/GetItemRotRight (CharacterItems.cs:879-894).
            gripPos = grip.localPosition;
            gripRot = Quaternion.Inverse(item.transform.rotation) * grip.rotation;
        }
        else
        {
            gripPos = Vector3.zero;
            gripRot = Quaternion.identity;
        }

        rightRig.ControllerToBone(out var tPos, out var tRot);
        GripMath.ItemInHand(gripPos.N(), gripRot.N(), tPos.N(), tRot.N(), out var xp, out var xr);
        if (rig.IsLeft)
            GripMath.MirrorX(ref xp, ref xr);
        pos = xp.U();
        rot = xr.U();
        return true;
    }

    private void ApplyFingers()
    {
        if (!rigReady)
            return;
        if (!AvatarConfig.Instance.Fingers)
        {
            RestoreFingers();
            return;
        }
        var pose = Pose;
        if (pose.HasLeftHand)
            leftRig.Curl(pose.LeftFingers);
        if (pose.HasRightHand)
            rightRig.Curl(pose.RightFingers);
    }

    private void RestoreFingers()
    {
        leftRig?.Restore();
        rightRig?.Restore();
    }
}
