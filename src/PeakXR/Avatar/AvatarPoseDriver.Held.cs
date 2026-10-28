using PeakXR.Core;
using UnityEngine;

namespace PeakXR.Avatar;

/// <summary>
/// Held-item side of the driver: the item's frame from the tracked hand, the single-hand weld, the free carry that
/// follows the controller when the ragdoll arm cannot, and the item passing through its holder's body. Local
/// physics only on every client; nothing here is sent.
/// </summary>
public partial class AvatarPoseDriver
{
    /// <summary>
    /// After <c>CharacterRagdoll.SaveAdditionalTransformPositions</c>: replace the look-based held-item frame with
    /// one carried by the holding hand. Both the hand IK targets and the item hold force derive from this frame,
    /// so the item follows the real hand on this client. Vanilla peers keep computing it from look values.
    /// </summary>
    internal void OnSaveAdditional()
    {
        ItemFrameOverridden = false;
        TrackHandoff();
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
        if (!HandBoneOnItem(item, left, out var bonePos, out var boneRot))
            return false;
        var handBody = HandBody(left);
        handBody.transform.SetPositionAndRotation(bonePos, boneRot);
        DestroyItemJoint();
        DestroyHandJoint(BodypartType.Hand_L, item.rig);
        DestroyHandJoint(BodypartType.Hand_R, item.rig);
        itemJoint = handBody.gameObject.AddComponent<FixedJoint>();
        itemJoint.connectedBody = item.rig;
        itemJointLeft = left;
        return true;
    }

    /// <summary>Where the <paramref name="left"/> hand bone sits on <paramref name="item"/> as it lies now: the
    /// controller-held grip, mapped back (bone = item * X^-1 * T).</summary>
    private bool HandBoneOnItem(Item item, bool left, out Vector3 bonePos, out Quaternion boneRot)
    {
        bonePos = default;
        boneRot = Quaternion.identity;
        var rig = left ? leftRig : rightRig;
        if (!ItemInController(rig, out var xPos, out var xRot))
            return false;
        var invRot = Quaternion.Inverse(xRot);
        var itemToCtrlPos = invRot * -xPos;
        var ctrlPos = item.transform.position + item.transform.rotation * itemToCtrlPos;
        var ctrlRot = item.transform.rotation * invRot;
        rig.BoneFromController(ctrlPos, ctrlRot, out bonePos, out boneRot);
        return true;
    }

    /// <summary>Seconds after an equip's attach before the local weld gate judges the arm (the attach settles in 0.5 s,
    /// CharacterItems.cs:617 and ReconcileWeld).</summary>
    private const float AttachSettle = 0.5f;

    /// <summary>Arm shortfall (m) past which vanilla's hold spring stops pulling a welded item toward its controller
    /// (<see cref="FollowHeld"/>): the ghost hand's own fade-in start.</summary>
    private const float SpringReachSlack = 0.05f;

    private static System.Reflection.MethodBase vanillaAttach;

    /// <summary>CharacterItems.AttachItem (CharacterItems.cs:641, private), resolved on first use.</summary>
    private static System.Reflection.MethodBase VanillaAttach =>
        vanillaAttach ??= Framework.GameMember.Method(typeof(CharacterItems), "AttachItem");

    /// <summary>How far the <paramref name="left"/> arm (shoulder to wrist of its IK chain) cannot reach
    /// <paramref name="bone"/> right now, metres.</summary>
    private float ArmShortfall(bool left, Vector3 bone) =>
        PhysicsShoulder(left, out var shoulder, out float arm)
            ? WeldGate.Shortfall(shoulder.N(), bone.N(), arm)
            : 0f;

    /// <summary>The <paramref name="left"/> shoulder of the IK chain moved into the physics body's frame (the chain
    /// is animated, the controller bone is in the world: <see cref="AnimatedOffset"/> apart), and that chain's arm
    /// length.</summary>
    private bool PhysicsShoulder(bool left, out Vector3 shoulder, out float arm)
    {
        shoulder = default;
        arm = 0f;
        var ik = left ? Owner.refs.ikLeft : Owner.refs.ikRight;
        if (ik == null || ik.data.root == null || ik.data.mid == null || ik.data.tip == null)
            return false;
        var root = ik.data.root.position;
        var mid = ik.data.mid.position;
        arm = Vector3.Distance(root, mid) + Vector3.Distance(mid, ik.data.tip.position);
        shoulder = root - AnimatedOffset();
        return true;
    }

    /// <summary>Terrain between the <paramref name="left"/> shoulder and <paramref name="bone"/>: the hand is stuck
    /// against it. Cast only while the hand lags past the release distance.</summary>
    private bool ReachBlocked(bool left, Vector3 bone) =>
        PhysicsShoulder(left, out var shoulder, out _)
        && Physics.Linecast(
            shoulder,
            bone,
            HelperFunctions.terrainMapMask,
            QueryTriggerInteraction.Ignore
        );

    private Rigidbody HandBody(bool left) =>
        Owner.refs.ragdoll.partDict[left ? BodypartType.Hand_L : BodypartType.Hand_R].Rig;

    /// <summary>
    /// Before <c>CharacterItems.HoldItem</c> (CharacterItems.cs:170-179, every physics tick): vanilla pulls the item
    /// toward <c>GetItemHoldPos</c> (:707-717), which <see cref="OnSaveAdditional"/> already points at the controller,
    /// while the weld ties it to the ragdoll hand. That hand only reaches as far as the arm from a shoulder at standing
    /// height, so a stooped controller left the item up to 1.2 m behind (vr50, vr.87). Past that reach the weld lets
    /// go and the item's own velocity carries it onto the controller-held pose; it rejoins the hand when the arm
    /// catches up. Every tracked character, not only the local one: a held item is simulated on each client
    /// (ItemPhysicsSyncer syncs only ground items, ItemPhysicsSyncer.cs:55), so a peer that kept vanilla's weld and
    /// spring for it held the VR player's item and hand where vanilla puts them, 0.3-0.5 m off the hand the VR player's
    /// own client shows (MP run 16: grab, pickup, holster). Local physics only, nothing sent. True skips vanilla's spring.
    /// </summary>
    /// <summary>A peer's copy of a tracked holder carries the held item free of the hand (<see cref="FollowHeld"/>).</summary>
    private bool remoteFree;

    internal bool FollowHeld(Item item)
    {
        HeldItemGap = -1f;
        if (
            item == null
            || item.rig == null
            || armBlend.Weight <= 0f
            || !TryGetItemWorldPose(out var target, out var targetRot)
        )
        {
            // No hand-carried pose (arms ragdolled, no tracking): back to vanilla's weld, body collisions restored.
            haveLastTarget = false;
            if (weldGate.Released || remoteFree)
            {
                weldGate.Reset();
                remoteFree = false;
                EndPassBody();
                // Ours when the pose allows it, else the game's own two-hand weld (CharacterItems.AttachItem,
                // CharacterItems.cs:641; its prefix falls through to vanilla while our attach declines).
                if (item != null && item.rig != null && !AttachToHoldingHand(item))
                    VanillaAttach?.Invoke(Owner.refs.items, new object[] { item });
            }
            return false;
        }
        if (!ReferenceEquals(item, followItem))
        {
            followItem = item;
            weldGate.Reset();
            remoteFree = false;
            EndPassBody();
            haveLastTarget = false;
        }
        float dt = Time.fixedDeltaTime;
        var body = item.rig;
        HeldItemGap = Vector3.Distance(body.position, target);
        bool left = Pose.ItemInLeftHand;
        // The arm, not the item: the ragdoll hand against where its controller puts the hand bone, and against where
        // the item's grip puts it.
        var hand = HandBody(left).position;
        var rig = left ? leftRig : rightRig;
        rig.BoneFromController(
            left ? Pose.LeftHandPosition : Pose.RightHandPosition,
            left ? Pose.LeftHandRotation : Pose.RightHandRotation,
            out var ctrlBone,
            out _
        );
        float toItem = HandBoneOnItem(item, left, out var itemBone, out _)
            ? Vector3.Distance(hand, itemBone)
            : float.PositiveInfinity;
        bool wasFree = Owner.IsLocal ? weldGate.Released : remoteFree;
        float toController = Vector3.Distance(hand, ctrlBone);
        float shortfall = ArmShortfall(left, ctrlBone);
        // A peer always carries the item free of the hand on the holder's controller pose, its hand on the arm IK:
        // judging the weld by its own lagging copy of the arm welded it when the holder's client had let go, and the
        // weld dragged the hand 0.3 m under where the holder shows it (MP run 17, pickup).
        // The local gate waits for the equip's own attach to settle (CharacterItems.cs:600-618, sinceItemAttach): at
        // spawn the arm is still on its way and was 0.71 m off, which let the passport go (MP run 20).
        bool free =
            !Owner.IsLocal
            || (
                (Owner.data.sinceItemAttach > AttachSettle || weldGate.Released)
                && weldGate.Update(
                    toController,
                    toItem,
                    dt,
                    shortfall,
                    toController > weldGate.Release && ReachBlocked(left, ctrlBone)
                )
            );
        remoteFree = !Owner.IsLocal;
        if (free != wasFree && Owner.IsLocal)
        {
            // Rare edges, kept in the log: why a held item left or rejoined the hand.
            Plugin.Log.LogInfo(
                System.FormattableString.Invariant(
                    $"[avatar.local] {item.name} {(free ? "left" : "rejoined")} the hand weld: hand {toController:0.00} m from its controller, {toItem:0.00} m from the item's grip, crouching {Owner.data.isCrouching}"
                )
            );
        }
        if (!free)
        {
            if (wasFree)
                AttachToHoldingHand(item);
            // Welded or free, the held item passes through its holder's body: crouched at a stoop, a welded apple
            // met the knees and held the hand 0.31 m off its controller while the free one, already passing the
            // body, let the same arm reach 0.12 m (vr65-66 vr.230).
            if (!ReferenceEquals(passItem, item) && EquipSettled)
            {
                PassBody(item);
                Plugin.Log.LogInfo(
                    $"[avatar.local] {item.name} held: passes the body, kinematic now {body.isKinematic} (its weight is cancelled each tick it is not), weld {(itemJoint != null ? "ours" : "the game's")}"
                );
            }
            // Welded, the item hangs its whole weight (prefab mass 5, against a hand part far lighter) on the arm's
            // muscles: the hand sat 0.2-0.25 m under its controller while the empty hand reached it (MP run 23).
            // Cancel the item's gravity so the arm carries only its own; vanilla's spring and torque still place it.
            // Local physics only, as the weld itself. Whoever welded it: right after an equip or a draw from a slot,
            // before ReconcileWeld swaps it for ours, the game's two-hand weld hangs it on both arms (MP run 24, holster).
            if (body.useGravity && !body.isKinematic)
                body.AddForce(-Physics.gravity, ForceMode.Acceleration);
            lastTarget = target;
            haveLastTarget = true;
            // Vanilla's spring is an outside force on the item (CharacterItems.cs:172). Toward a controller the arm
            // cannot reach, the weld hands it to the arm and the whole body: a held item lifted its holder toward the
            // raised hand, taller than standing (tester, OpenXR Touch). Out of reach the arm alone carries the item.
            return Owner.IsLocal && shortfall > SpringReachSlack;
        }
        // Free: no joint may hold it, whoever made it.
        if (!wasFree || itemJoint != null)
        {
            DestroyItemJoint();
            DestroyHandJoint(BodypartType.Hand_L, body);
            DestroyHandJoint(BodypartType.Hand_R, body);
        }
        if (!ReferenceEquals(passItem, item) && EquipSettled)
            PassBody(item);
        var handVelocity = haveLastTarget && dt > 0f ? (target - lastTarget) / dt : Vector3.zero;
        lastTarget = target;
        haveLastTarget = true;
        if (ItemFollow.Snaps(HeldItemGap))
        {
            // Too far to chase: put it in its place, moving with the hand.
            body.position = target;
            body.rotation = targetRot;
            body.linearVelocity = handVelocity;
            body.angularVelocity = Vector3.zero;
        }
        else
        {
            body.linearVelocity = ItemFollow
                .Velocity(body.position.N(), target.N(), handVelocity.N(), dt)
                .U();
            body.angularVelocity = ItemFollow.Spin(body.rotation.N(), targetRot.N(), dt).U();
        }
        // Diagnostics only while the lean trace runs (rig.camera's trace window).
        if (Rig.RigDriver.LeanTraceDue(ref freeLogAt))
        {
            // Whether a free item that lags sits against the world: its target inside terrain or props.
            bool blocked = Physics.CheckSphere(
                target,
                0.03f,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            );
            Plugin.Log.LogInfo(
                System.FormattableString.Invariant(
                    $"[avatar.local] {item.name} free of the hand: {HeldItemGap:0.00} m from its place, speed {body.linearVelocity.magnitude:0.0} m/s, target in geometry {blocked}"
                )
            );
        }
        return true;
    }

    /// <summary>The equip has re-enabled the item's colliders (CharacterItems.cs:612-631, sinceItemAttach reset at
    /// :617): ignore pairs set while they are disabled are dropped by the physics engine.</summary>
    private bool EquipSettled => Owner.data.sinceItemAttach > AttachSettle;

    /// <summary>
    /// A held item, welded or free, passes through its holder's own body, as a real hand carries a torch past its own
    /// hip: the scout's colliders are far larger than a person (the 0.38 m head sphere, a wide torso), so a free torch
    /// held low beside the body was pinned 0.2-0.35 m off its hand (vr52, vr.88) and a welded apple at a crouched
    /// stoop met the knees (vr66, vr.230). Local physics only; undone when the item leaves the hand
    /// (<see cref="EndPassBody"/>).
    /// </summary>
    private void PassBody(Item item)
    {
        EndPassBody();
        if (bodyColliders == null)
        {
            var all = new System.Collections.Generic.List<Collider>();
            foreach (var part in Owner.refs.ragdoll.partList)
            {
                if (part == null)
                    continue;
                foreach (var col in part.GetComponentsInChildren<Collider>())
                {
                    if (!col.isTrigger)
                        all.Add(col);
                }
            }
            bodyColliders = all.ToArray();
        }
        passItem = item;
        passItemColliders = item.GetComponentsInChildren<Collider>();
        SetPass(true);
    }

    /// <summary>Restores collisions between the last free item and the body. Also run when the item is unattached
    /// (dropped, thrown, switched), from the UnAttachItem patch.</summary>
    internal void EndPassBody()
    {
        if (passItem != null)
            SetPass(false);
        passItem = null;
        passItemColliders = null;
    }

    private void SetPass(bool ignore)
    {
        if (passItemColliders == null || bodyColliders == null)
            return;
        foreach (var a in passItemColliders)
        {
            if (a == null || a.isTrigger)
                continue;
            foreach (var b in bodyColliders)
            {
                if (b != null)
                    Physics.IgnoreCollision(a, b, ignore);
            }
        }
    }

    /// <summary>
    /// The weld is chosen once at attach (about four physics ticks after the equip RPC), from a pose that for a
    /// remote player is ~100 ms old, so it can pick the wrong hand; vanilla's two-hand weld can also predate a fresh
    /// pose (late join, entering VR while holding, an equip during a full-body emote). Re-weld when they disagree.
    /// </summary>
    private void ReconcileWeld()
    {
        var item = Owner.data.currentItem;
        // The flag stays until a weld is back: a re-weld declined now (arms released, a stale pose) is retried, or
        // the item would stay with no joint at all.
        if (
            handoffReweld
            && (
                itemJoint != null
                || (
                    item != null
                    && !HandingOff
                    && !remoteFree
                    && !weldGate.Released
                    && AttachToHoldingHand(item)
                )
            )
        )
            handoffReweld = false;
        if (HandingOff)
            return;
        // A peer carries the item free of the hand, and a local item the gate let go is FollowHeld's: no weld to
        // reconcile. Re-welding a free local item here left it welded while FollowHeld still drove it by velocity,
        // dragging the body so both arms hung 0.2-0.6 m under the tracked hands (MP run 20, the passport).
        if (
            item == null
            || ArmsSuspended
            || armBlend.Weight < 1f
            || remoteFree
            || weldGate.Released
        )
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

    /// <summary>The item is easing into a new primary hand after a handoff.</summary>
    private bool HandingOff => HoldHands.Ease(Time.time - handoffAt, HoldHands.EaseSeconds) < 1f;

    /// <summary>
    /// The primary hand changed under the same item (the local player's handoff, or a peer's arriving in its pose
    /// flags): keep the item where it is in the new primary's frame and ease it onto the new grip, and let go of the
    /// old primary's weld at once, so the hand that opened does not drag the item away.
    /// </summary>
    private void TrackHandoff()
    {
        var item = Owner.data.currentItem;
        var pose = Pose;
        bool left = pose.ItemInLeftHand;
        bool same = ReferenceEquals(item, handoffItem);
        // A stale pose cannot ease the item over: the handoff waits for a fresh one, so handoffLeft keeps the old hand.
        bool fresh = HasFreshPose && (left ? pose.HasLeftHand : pose.HasRightHand);
        if (item != null && same && left != handoffLeft && fresh)
        {
            var inv = Quaternion.Inverse(left ? pose.LeftHandRotation : pose.RightHandRotation);
            handoffPos =
                inv
                * (
                    item.transform.position
                    - (left ? pose.LeftHandPosition : pose.RightHandPosition)
                );
            handoffRot = inv * item.transform.rotation;
            handoffAt = Time.time;
            if (itemJoint != null)
            {
                DestroyItemJoint();
                handoffReweld = true;
            }
        }
        else if (!same)
        {
            // Another item: no ease or weld left over from the last one.
            handoffAt = -10f;
            handoffReweld = false;
        }
        handoffItem = item;
        if (fresh || !same)
            handoffLeft = left;
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
        // Whichever runs first in a tick (FollowHeld or OnSaveAdditional) sees a handoff.
        TrackHandoff();
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
        float handoff = HoldHands.Ease(Time.time - handoffAt, HoldHands.EaseSeconds);
        if (handoff < 1f)
        {
            xPos = Vector3.Lerp(handoffPos, xPos, handoff);
            xRot = Quaternion.Slerp(handoffRot, xRot, handoff);
        }
        rot = ctrlRot * xRot;
        pos = ctrlPos + ctrlRot * xPos;

        if (pose.TwoHandHold && hold.Aimed && (left ? pose.HasRightHand : pose.HasLeftHand))
        {
            // A launcher lies along the two-hand axis, turned about the holding grip: the vanilla grips sit side by
            // side at its stock, so aligning them with the support hand swung a crossbow held at its fore-end 60-90
            // degrees off to the side, while it fires along its own barrel (HandAim.TryBarrel; tester: "the crossbow
            // is somewhere in a random place").
            var support = left ? pose.RightHandPosition : pose.LeftHandPosition;
            var aimed = GripMath
                .TwoHandAim(
                    rot.N(),
                    (rot * Vector3.forward).N(),
                    ctrlPos.N(),
                    support.N(),
                    Interact.HandAim.TwoHandMinSeparation
                )
                .U();
            pos = ctrlPos + aimed * Quaternion.Inverse(rot) * (pos - ctrlPos);
            rot = aimed;
        }
        else if (pose.TwoHandHold && (left ? pose.HasRightHand : pose.HasLeftHand))
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
        // The grips as this hold has them: where the left hold puts them for a left-primary hold
        // (ItemHoldInfo.HoldLocal).
        bool left = Pose.ItemInLeftHand;
        Consider(hold, hold.GripLeft, false, item.transform, anchorLocal, left, ref axis, ref best);
        Consider(hold, hold.GripRight, true, item.transform, anchorLocal, left, ref axis, ref best);
        return axis;

        static void Consider(
            ItemHoldInfo hold,
            Transform grip,
            bool primaryGrip,
            Transform itemTransform,
            Vector3 anchorLocal,
            bool primaryLeft,
            ref Vector3 axis,
            ref float best
        )
        {
            if (grip == null)
                return;
            var local =
                hold.HoldLocal(
                    itemTransform.InverseTransformPoint(grip.position),
                    primaryLeft,
                    primaryGrip
                ) - anchorLocal;
            if (local.magnitude > best)
            {
                best = local.magnitude;
                axis = local;
            }
        }
    }

    /// <summary>The held item's one-hand pose in the <paramref name="left"/> controller's frame, so gestures test the
    /// part of the item that touches (a mouthpiece, the food) on the tracked hand rather than the controller.</summary>
    internal bool HeldItemInController(bool left, out Vector3 pos, out Quaternion rot)
    {
        pos = default;
        rot = Quaternion.identity;
        return EnsureRig()
            && hold.Bind(Owner.data.currentItem)
            && ItemInController(left ? leftRig : rightRig, out pos, out rot);
    }

    /// <summary>
    /// Item pose in a controller's frame: the right hand puts the item's <c>Hand_R</c> grip on its hand bone;
    /// the left hand holds the mirror image of that about the item's left-hold plane (vanilla clients keep showing it
    /// in the right hand).
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
        pos = xp.U();
        rot = xr.U();
        if (rig.IsLeft)
        {
            // The mirror image of the right hold about the item's own left-hold plane, its palm kept on the item
            // (ItemHoldInfo.LeftCorrection): a rigid item mirrored at x = 0 left the hand up to 14 cm off a lopsided
            // model (Shell Big, the checkpoint flag).
            GripMath.MirrorX(ref xp, ref xr);
            hold.LeftCorrection(out var cPos, out var cRot);
            pos = xp.U() + xr.U() * cPos;
            rot = xr.U() * cRot;
        }
        if (hold.HasFront)
        {
            // Its face, dial or cover turned toward the eyes of a hand held up to look at it: a spin about the
            // grip-to-middle line, then a bounded tilt about the grip, the hand staying on the grip (Core ItemFront).
            // Fixed per item and hand: no swimming as the wrist turns. Aimed from where the middle is drawn
            // (HeldItemScale shrinks only the local player's own item), so peers' holds differ by a few degrees at most.
            ItemFront.Turn(
                pos.N(),
                rot.N(),
                hold.HoldPivot(rig.IsLeft).N(),
                ((hold.BoundsMin + hold.BoundsMax) * 0.5f).N(),
                HeldItemScale.Factor(item),
                hold.Front.N(),
                ItemFront.EyePoint(rig.IsLeft),
                ItemFront.MaxTilt,
                out var tp,
                out var tr
            );
            pos = tp.U();
            rot = tr.U();
        }
        return true;
    }
}
