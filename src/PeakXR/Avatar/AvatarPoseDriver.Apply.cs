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
    private HandRig leftRig;
    private HandRig rightRig;
    private readonly ItemHoldInfo hold = new();
    private FixedJoint itemJoint;
    private bool itemJointLeft;

    /// <summary>The item whose vanilla two-hand weld was last checked for replacement; one try per item.</summary>
    private Item vanillaWeldChecked;

    /// <summary>True while this tick's held-item frame comes from the tracked hand; read by the hold torque patch.</summary>
    internal bool ItemFrameOverridden { get; private set; }

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
        // Animation only runs in FixedUpdate; refresh fingers every rendered frame so curls stay smooth at 90 Hz+.
        if (rigReady && HasFreshPose)
            ApplyFingers();
        else if (rigReady)
            RestoreFingers();
    }

    /// <summary>After <c>CharacterAnimations.ConfigureIK</c>: bones are in the animated pose and IK targets were
    /// just written by vanilla. Overwrite the hand targets, add head look, curl fingers.</summary>
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

        if (
            cfg.ArmIK
            && !ArmsSuspended
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
                refs.IKHandTargetLeft.SetPositionAndRotation(p + offset, r);
            }
            if (pose.HasRightHand)
            {
                rightRig.BoneFromController(
                    pose.RightHandPosition,
                    pose.RightHandRotation,
                    out var p,
                    out var r
                );
                refs.IKHandTargetRight.SetPositionAndRotation(p + offset, r);
            }
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

    /// <summary>After <c>CharacterAnimations.HandleIK</c>: arms follow their own controllers in every state.</summary>
    internal void OnHandleIK()
    {
        if (!HasFreshPose || !AvatarConfig.Instance.ArmIK || ArmsSuspended)
            return;
        var refs = Owner.refs;
        if (refs.ikRig == null || refs.ikLeft == null || refs.ikRight == null)
            return;
        if (refs.ikRig.weight <= 0f)
        {
            // Vanilla had IK off: the constraint weights are leftovers from the last held item.
            refs.ikLeft.weight = 0f;
            refs.ikRight.weight = 0f;
        }
        refs.ikRig.weight = 1f;
        if (Pose.HasLeftHand)
            refs.ikLeft.weight = 1f;
        if (Pose.HasRightHand)
            refs.ikRight.weight = 1f;
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
        if (!TryGetItemWorldPose(out var itemPos, out var itemRot))
            return;
        var refs = Owner.refs;
        var offset = refs.animationHipTransform.position - hip.Rig.position;
        refs.animationItemTransform.SetPositionAndRotation(itemPos + offset, itemRot);
        ItemFrameOverridden = true;
    }

    /// <summary>
    /// Replaces vanilla's two-hand weld (<c>CharacterItems.AttachItem</c>) with a weld to the holding hand only,
    /// so the other hand stays free. Returns false to let vanilla attach.
    /// </summary>
    internal bool AttachToHoldingHand(Item item)
    {
        if (
            !HasFreshPose
            || ArmsSuspended
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
        if (item == null || ArmsSuspended)
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
