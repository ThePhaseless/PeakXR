using PeakXR.Core;
using UnityEngine;

namespace PeakXR.Avatar;

/// <summary>
/// Held-item side of the driver: the item's frame from the tracked hand (between both hands while two hold it), the
/// single-hand weld, the free carry that follows the controller when the ragdoll arm cannot, and the item passing
/// through its holder's body. Local physics only on every client; nothing here is sent.
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
        // A weld whose anchor can move: the item rides out past an arm too short for its controller (HeldShift).
        var joint = handBody.gameObject.AddComponent<ConfigurableJoint>();
        joint.autoConfigureConnectedAnchor = false;
        joint.connectedBody = item.rig;
        joint.anchor = Vector3.zero;
        joint.connectedAnchor = item.rig.transform.InverseTransformPoint(bonePos);
        joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
        joint.angularXMotion =
            joint.angularYMotion =
            joint.angularZMotion =
                ConfigurableJointMotion.Locked;
        itemJoint = joint;
        itemJointLeft = left;
        return true;
    }

    /// <summary>This driver's own weld holds <paramref name="item"/> (the game's are FixedJoints on the hands).</summary>
    internal bool Welds(Rigidbody item) => itemJoint != null && itemJoint.connectedBody == item;

    /// <summary>The weld's anchor on the <paramref name="left"/> hand body: the item's grip out along the shoulder
    /// to <paramref name="bone"/> line by what the arm cannot reach (Core <see cref="ArmReach.HeldShift"/>, #23), so the
    /// item sits on its controller and the drawn arm stretched to it holds it (<see cref="DrawnHands"/>). Eased out
    /// while both hands hold it (it sits between them) and back in when one lets go (Core
    /// <see cref="ArmReach.HeldShiftWeight"/>), and eased out while the local item hand grips the world (a handle,
    /// piton, rope or vine: ClimbHands.Adopt), where it is drawn on its ragdoll hand.</summary>
    private Vector3 HeldShift(bool left, Vector3 bone)
    {
        // shortcut: a peer's grip on the world is not sent, so its item keeps the shift; send a pose flag if one shows.
        anchoredWeight = Mathf.MoveTowards(
            anchoredWeight,
            Owner.IsLocal && Interact.PhysicalInteraction.WorldGripHolds(left) ? 1f : 0f,
            Time.fixedDeltaTime / HoldHands.EaseSeconds
        );
        float weight = ArmReach.HeldShiftWeight(
            twoHandOn,
            twoWeight,
            Pose.TwoHandHold,
            HoldHands.Ease(Time.time - handoffAt, HoldHands.EaseSeconds),
            anchoredWeight
        );
        return weight > 0f && PhysicsShoulder(left, out var shoulder, out float arm)
            ? HandBody(left)
                .transform.InverseTransformVector(
                    ArmReach.HeldShift(shoulder.N(), bone.N(), arm).U() * weight
                )
            : Vector3.zero;
    }

    /// <summary>Where the held item, as it lies now, puts the <paramref name="left"/> hand bone on its grip, while that
    /// hand holds it alone: the drawn hand stretches to it (<see cref="DrawnHands"/>). False for the other hand, a
    /// two-hand hold or no item, and while the hand does not carry the item (<see cref="ItemFrameOverridden"/> false:
    /// <c>Avatar.RetargetItems</c> off, the arms released), its grip then lying where the game's own hold puts it.</summary>
    internal bool HeldGripBone(bool left, out Vector3 bone)
    {
        bone = default;
        var item = Owner.data.currentItem;
        var pose = Pose;
        return ItemFrameOverridden
            && item != null
            && !pose.TwoHandHold
            && pose.ItemInLeftHand == left
            && EnsureRig()
            && hold.Bind(item)
            && HandBoneOnItem(item, left, out bone, out _);
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

    /// <summary>CharacterItems.AttachItem (CharacterItems.cs:641, private), resolved by
    /// <see cref="AvatarPatches.Resolve"/> at install so a renamed member fails the feature, not a physics tick.</summary>
    internal static System.Reflection.MethodBase VanillaAttach;

    /// <summary>How far the <paramref name="left"/> arm (shoulder to wrist of its IK chain) cannot reach
    /// <paramref name="bone"/> right now, metres.</summary>
    internal float ArmShortfall(bool left, Vector3 bone) =>
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

    /// <summary>The world holds <paramref name="item"/> back from moving <paramref name="toTarget"/> onto its
    /// controller-held pose: its centre pressed into terrain or a map prop, or a sphere of its size swept that way meets
    /// one, or the sweep meets another character's body. Held items collide with all of these (Item.cs:726), so a free
    /// carry would stop there too, and past <see cref="ItemFollow.SnapGap"/> it would jump inside what stopped it (#17,
    /// #119).</summary>
    private bool ItemHeldBack(Item item, Vector3 toTarget)
    {
        float dist = toTarget.magnitude;
        if (dist < 1e-3f)
            return false;
        var centre = item.Center();
        var size = item.mainRenderer != null ? item.mainRenderer.bounds.extents : Vector3.zero;
        // Just inside the item's narrowest half-width: a wall it only touches is met by the sweep, not skipped as an
        // overlap at its start.
        float radius = Mathf.Clamp(
            0.9f * Mathf.Min(size.x, Mathf.Min(size.y, size.z)),
            ItemProbeMin,
            ItemProbeMax
        );
        heldBackBy = null;
        if (
            Physics.CheckSphere(
                centre,
                0.5f * radius,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
        )
            return true;
        if (
            Physics.SphereCast(
                centre,
                radius,
                toTarget / dist,
                out var hit,
                dist,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
        )
        {
            heldBackBy = hit.collider;
            return true;
        }
        return OtherBodyInTheWay(centre, radius, toTarget / dist, dist);
    }

    /// <summary>What the last <see cref="ItemHeldBack"/> sweep met, for the log; null when the item's centre sits in
    /// terrain, which an overlap check cannot name.</summary>
    private Collider heldBackBy;

    /// <summary>A kept weld's start has been logged and its end not yet (<see cref="LogKeptWeld"/>).</summary>
    private bool keptWeldLogged;

    /// <summary>Rare edges, kept in the log: when a weld keeps an item held back and when it stops, ended only once
    /// the arm's limit has lapsed (<see cref="HeldBlockedFresh"/>), so a probe flickering along a wall logs once. The
    /// numbers tell a real wall from a probe that read a settling item as held back (#121).</summary>
    private void LogKeptWeld(
        Item item,
        bool kept,
        float toController,
        float shortfall,
        float toItem
    )
    {
        bool holding = kept || Time.fixedTime - heldBlockedAt <= HeldBlockedFresh;
        if (holding == keptWeldLogged)
            return;
        keptWeldLogged = holding;
        string state = holding
            ? "kept in the hand, held back by "
                + (heldBackBy != null ? heldBackBy.name : "terrain around its centre")
            : "no longer held back";
        Plugin.Log.LogInfo(
            System.FormattableString.Invariant(
                $"[avatar.local] {item.name} {state}: hand {toController:0.00} m from its controller (arm {shortfall:0.00} m short), {toItem:0.00} m from the item's grip, item {HeldItemGap:0.00} m from its place, {Owner.data.sinceItemAttach:0.00} s after its attach"
            )
        );
    }

    /// <summary>The sweep meets a body <see cref="ItemFollow.BodyHoldsItemBack"/> counts. It also lists colliders it
    /// starts inside; the holder's own body is nearly always among them.</summary>
    private bool OtherBodyInTheWay(Vector3 centre, float radius, Vector3 direction, float dist)
    {
        int hits = Physics.SphereCastNonAlloc(
            centre,
            radius,
            direction,
            BodyHits,
            dist,
            HelperFunctions.CharacterMask,
            QueryTriggerInteraction.Ignore
        );
        for (int i = 0; i < hits; i++)
        {
            var body = BodyHits[i].collider.GetComponentInParent<Character>();
            if (
                body != null
                && ItemFollow.BodyHoldsItemBack(
                    body,
                    Owner,
                    Owner.data.carriedPlayer,
                    Owner.data.carrier
                )
            )
            {
                heldBackBy = BodyHits[i].collider;
                return true;
            }
        }
        return false;
    }

    /// <summary>Room for the parts of a few bodies around a held item.</summary>
    private static readonly RaycastHit[] BodyHits = new RaycastHit[32];

    private const float ItemProbeMin = 0.02f;
    private const float ItemProbeMax = 0.15f;

    /// <summary>How far the world holds the welded item's hand off its controller past the arm's reach, which hand, and
    /// when (physics time) it last did: <see cref="ReachGap(bool, out float)"/> ghosts that hand for it.</summary>
    private float heldBlockedLag;
    private bool heldBlockedLeft;
    private float heldBlockedAt = float.NegativeInfinity;

    /// <summary>Where that hand's ragdoll body was then (physics frame): its arm reaches no further past it
    /// (<see cref="HeldBackReach"/>).</summary>
    private Vector3 heldBlockedHand;

    /// <summary><paramref name="bone"/> (the controller's hand bone, physics frame) held to
    /// <see cref="ItemFollow.HeldBackReach"/> while the weld keeps this hand's item against the world.</summary>
    private Vector3 HeldBackReach(bool left, Vector3 bone) =>
        left == heldBlockedLeft && Time.fixedTime - heldBlockedAt <= HeldBlockedFresh
            ? ItemFollow.HeldBackReach(heldBlockedHand.N(), bone.N()).U()
            : bone;

    /// <summary>Seconds a held-back item's lag counts for the ghost after the last physics tick that saw it.</summary>
    private const float HeldBlockedFresh = 0.1f;

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
            out var ctrlBoneRot
        );
        // Both hands on it: the arm reaches for the hand's grip on the item between them.
        TwoHandGrip(left, ref ctrlBone, ref ctrlBoneRot);
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
        // Cast only while the hand lags or the item is free. Walking or not, as vanilla keeps a held item against a
        // wall: run 18 let the apple go as the controller rose with the stick idle (vr.246, #17). Kept, the arm stops
        // pressing for the controller (heldBlockedHand, OnConfigureIK), so the item's contact never lifts the body:
        // the torch held on the cannon pushed the body up onto it (vr.87, #116). Not on a freshly given item, as the
        // gate below waits: a hit would stop an arm still on its way to it (vr.244, #121).
        bool itemBlocked =
            Owner.IsLocal
            && weldGate.ProbesHeldBack(toController - shortfall, EquipSettled)
            && ItemHeldBack(item, target - body.position);
        bool free =
            !Owner.IsLocal
            || (
                (Owner.data.sinceItemAttach > AttachSettle || weldGate.Released)
                && weldGate.Update(
                    toController,
                    toItem,
                    dt,
                    shortfall,
                    toController > weldGate.Release && ReachBlocked(left, ctrlBone),
                    itemBlocked
                )
            );
        remoteFree = !Owner.IsLocal;
        if (itemBlocked && !free)
        {
            // The weld keeps the item on a hand the world holds back: the ghost shows where the controller is.
            heldBlockedLag = toController - shortfall;
            heldBlockedLeft = left;
            heldBlockedAt = Time.fixedTime;
            heldBlockedHand = hand;
        }
        if (Owner.IsLocal)
            LogKeptWeld(item, itemBlocked && !free, toController, shortfall, toItem);
        if (free != wasFree && Owner.IsLocal)
        {
            // Rare edges, kept in the log: why a held item left or rejoined the hand.
            Plugin.Log.LogInfo(
                System.FormattableString.Invariant(
                    $"[avatar.local] {item.name} {(free ? "left" : "rejoined")} the hand weld: hand {toController:0.00} m from its controller (arm {shortfall:0.00} m short), {toItem:0.00} m from the item's grip, crouching {Owner.data.isCrouching}, held back by the world or a body {itemBlocked}"
                )
            );
        }
        if (!free)
        {
            if (wasFree)
                AttachToHoldingHand(item);
            // Only on a real move: a write may rebuild the joint's frame from the bodies' poses now (#23).
            if (itemJoint != null && itemJointLeft == left)
            {
                var anchor = HeldShift(left, ctrlBone);
                if (ArmReach.AnchorMoved(itemJoint.anchor.N(), anchor.N()))
                    itemJoint.anchor = anchor;
            }
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
            // Held back by the world, nothing presses it on what holds it (#116).
            return Owner.IsLocal && (shortfall > SpringReachSlack || itemBlocked);
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
        // Diagnostics only while the lean trace runs (rig.camera's trace window), and only when something changed: a
        // free item that sits still logged the same line 84 times.
        if (Rig.RigDriver.LeanTraceDue(ref freeLogAt))
        {
            // Whether a free item that lags sits against the world: its target inside terrain or props.
            bool blocked = Physics.CheckSphere(
                target,
                0.03f,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            );
            float lag = toController - shortfall;
            if (
                !wasFree
                || blocked != freeLoggedBlocked
                || Mathf.Abs(HeldItemGap - freeLoggedGap) >= FreeLogStep
                || Mathf.Abs(lag - freeLoggedLag) >= FreeLogStep
            )
            {
                freeLoggedBlocked = blocked;
                freeLoggedGap = HeldItemGap;
                freeLoggedLag = lag;
                Plugin.Log.LogInfo(
                    System.FormattableString.Invariant(
                        $"[avatar.local] {item.name} free of the hand: {HeldItemGap:0.00} m from its place, speed {body.linearVelocity.magnitude:0.0} m/s, target in geometry {blocked}, hand {toController:0.00} m from its controller (arm {shortfall:0.00} m short)"
                    )
                );
            }
        }
        return true;
    }

    /// <summary>The equip has re-enabled the item's colliders (CharacterItems.cs:612-631, sinceItemAttach reset at
    /// :617): ignore pairs set while they are disabled are dropped by the physics engine.</summary>
    private bool EquipSettled => Owner.data.sinceItemAttach > AttachSettle;

    /// <summary>A free item's trace line repeats only once its gap or the hand's lag moved this far (m).</summary>
    private const float FreeLogStep = 0.05f;
    private float freeLoggedGap;
    private float freeLoggedLag;
    private bool freeLoggedBlocked;

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
            // From both hands' pose when they held it, else where the item is.
            bool fromTwo = twoHandOn && haveItemTarget;
            twoHandOn = false;
            twoWeight = 0f;
            handoffPos =
                inv
                * (
                    (fromTwo ? itemTargetPos : item.transform.position)
                    - (left ? pose.LeftHandPosition : pose.RightHandPosition)
                );
            handoffRot = inv * (fromTwo ? itemTargetRot : item.transform.rotation);
            handoffAt = Time.time;
            if (itemJoint != null)
            {
                DestroyItemJoint();
                handoffReweld = true;
            }
        }
        else if (!same)
        {
            // Another item: no ease, weld or two-hand hold left over from the last one.
            handoffAt = -10f;
            handoffReweld = false;
            twoHandOn = false;
            twoWeight = 0f;
            haveItemTarget = false;
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
        else
            TwoHand(item, pose, left, rig, ctrlPos, ctrlRot, ref pos, ref rot);
        haveItemTarget = true;
        itemTargetPos = pos;
        itemTargetRot = rot;
        return true;
    }

    /// <summary>How far the item hand's grip on the world has eased the weld's shift out (<see cref="HeldShift"/>).</summary>
    private float anchoredWeight;

    // The two-hand hold (Core TwoHandPose), set at the join from the one-hand pose and kept while both hands grip.
    private bool twoHandOn;
    private float twoHandAt = -10f;
    private float twoWeight;

    /// <summary>The second hand's grip in the item frame, as drawn (HeldItemScale about the main grip).</summary>
    private Vector3 twoGrip2;
    private Vector3 twoUpLocal;
    private Vector3 twoUp1;
    private Vector3 twoUp2;
    private Vector3 twoAxis;
    private Vector3 twoUp;

    /// <summary>The second hand bone's rotation in the item frame at the join.</summary>
    private Quaternion twoHand2Rot = Quaternion.identity;

    /// <summary>The last item pose <see cref="TryGetItemWorldPose"/> gave: where a release eases from.</summary>
    private bool haveItemTarget;
    private Vector3 itemTargetPos;
    private Quaternion itemTargetRot = Quaternion.identity;

    /// <summary>
    /// Both hands on the item (not a launcher, which lies along the two-hand axis instead): the item sits between
    /// them (Core <see cref="TwoHandPose"/>), its grips' middle on the hands' middle, its grip axis along theirs and its
    /// roll from both hands, eased in from the one-hand pose. The second hand's grip is the item's <c>Hand_L</c> (as
    /// this hold has it) when it closed near there, else where it closed on the body. The second hand letting go eases
    /// the item back onto the main hand's grip. Local physics only, as the one-hand pose.
    /// </summary>
    private void TwoHand(
        Item item,
        AvatarPose pose,
        bool left,
        HandRig rig,
        Vector3 ctrlPos,
        Quaternion ctrlRot,
        ref Vector3 pos,
        ref Quaternion rot
    )
    {
        if (!pose.TwoHandHold || hold.Aimed || (left ? !pose.HasRightHand : !pose.HasLeftHand))
        {
            twoWeight = 0f;
            if (!twoHandOn)
                return;
            twoHandOn = false;
            if (!haveItemTarget)
                return;
            // The second hand let go: from where both hands had it onto the main hand's grip, no jump.
            var inv = Quaternion.Inverse(ctrlRot);
            handoffPos = inv * (itemTargetPos - ctrlPos);
            handoffRot = inv * itemTargetRot;
            handoffAt = Time.time;
            pos = itemTargetPos;
            rot = itemTargetRot;
            return;
        }
        var other = left ? rightRig : leftRig;
        var otherRot = left ? pose.RightHandRotation : pose.LeftHandRotation;
        rig.BoneFromController(ctrlPos, ctrlRot, out var b1, out _);
        other.BoneFromController(
            left ? pose.RightHandPosition : pose.LeftHandPosition,
            otherRot,
            out var b2,
            out var b2Rot
        );
        var grip1 = hold.HoldPivot(left);
        if (
            !twoHandOn
            && !JoinTwoHand(item, left, grip1, b1, b2, b2Rot, ctrlRot, otherRot, pos, rot)
        )
            return;
        var axis = twoAxis.N();
        var up = twoUp.N();
        TwoHandPose.Solve(
            b1.N(),
            b2.N(),
            (ctrlRot * twoUp1).N(),
            (otherRot * twoUp2).N(),
            grip1.N(),
            twoGrip2.N(),
            twoUpLocal.N(),
            ref axis,
            ref up,
            out var tp,
            out var tr
        );
        twoAxis = axis.U();
        twoUp = up.U();
        twoWeight = TwoHandPose.Weight(Time.time - twoHandAt);
        TwoHandPose.Blend(pos.N(), rot.N(), tp, tr, twoWeight, out var bp, out var br);
        pos = bp.U();
        rot = br.U();
    }

    /// <summary>The second hand has just closed on the item held in one hand at <paramref name="pos"/>/
    /// <paramref name="rot"/>: where it holds it, the up each hand carries (an item's front stays where the one-hand
    /// hold showed it, toward the eyes) and the hand's turn on the item. False when it closed on the main grip itself,
    /// which gives no axis: the hold stays in one hand.</summary>
    private bool JoinTwoHand(
        Item item,
        bool left,
        Vector3 grip1,
        Vector3 b1,
        Vector3 b2,
        Quaternion b2Rot,
        Quaternion ctrlRot,
        Quaternion otherRot,
        Vector3 pos,
        Quaternion rot
    )
    {
        var handLocal = Quaternion.Inverse(rot) * (b2 - pos);
        bool hasGrip = hold.GripLeft != null;
        var gripL = hasGrip
            ? grip1
                + (hold.HoldLocal(hold.GripLeft.localPosition, left, primaryGrip: false) - grip1)
                    * HeldItemScale.Factor(item)
            : Vector3.zero;
        twoGrip2 = TwoHandPose
            .SecondGrip(handLocal.N(), hasGrip, gripL.N(), TwoHandPose.SecondGripReach)
            .U();
        var a = twoGrip2 - grip1;
        if (a.magnitude < TwoHandPose.MinSpacing)
            return false;
        twoUpLocal = TwoHandPose
            .UpLocal(a.normalized.N(), (hold.HasFront ? hold.Front : Vector3.up).N())
            .U();
        var span = b2 - b1;
        twoAxis = span.magnitude >= TwoHandPose.MinSpacing ? span.normalized : (rot * a).normalized;
        TwoHandPose.Capture(
            rot.N(),
            twoUpLocal.N(),
            twoAxis.N(),
            ctrlRot.N(),
            otherRot.N(),
            out var v1,
            out var v2,
            out var up0
        );
        twoUp1 = v1.U();
        twoUp2 = v2.U();
        twoUp = up0.U();
        // The second hand keeps the turn it closed with, on the item where the two hands will hold it.
        var axis = twoAxis.N();
        var up = twoUp.N();
        TwoHandPose.Solve(
            b1.N(),
            b2.N(),
            (ctrlRot * twoUp1).N(),
            (otherRot * twoUp2).N(),
            grip1.N(),
            twoGrip2.N(),
            twoUpLocal.N(),
            ref axis,
            ref up,
            out _,
            out var r0
        );
        twoHand2Rot = Quaternion.Inverse(r0.U()) * b2Rot;
        twoHandOn = true;
        twoHandAt = Time.time;
        return true;
    }

    /// <summary>
    /// Where a two-hand hold puts the <paramref name="left"/> hand bone (physics space): on its grip of the item
    /// between the hands, the main hand where its one-hand grip is, the second hand on <see cref="twoGrip2"/>; eased in
    /// with the hold. Left alone otherwise, so the arm IK, the weld gate and the drawn hands all meet the item.
    /// </summary>
    internal void TwoHandGrip(bool left, ref Vector3 bone, ref Quaternion boneRot)
    {
        if (
            !twoHandOn
            || twoWeight <= 0f
            || !TryGetItemWorldPose(out var itemPos, out var itemRot)
            || !twoHandOn
        )
            return;
        Vector3 p;
        Quaternion r;
        if (left == Pose.ItemInLeftHand)
        {
            var rig = left ? leftRig : rightRig;
            if (!ItemInController(rig, out var xPos, out var xRot))
                return;
            var ctrlRot = itemRot * Quaternion.Inverse(xRot);
            rig.BoneFromController(itemPos - ctrlRot * xPos, ctrlRot, out p, out r);
        }
        else
        {
            p = itemPos + itemRot * twoGrip2;
            r = itemRot * twoHand2Rot;
        }
        bone = Vector3.Lerp(bone, p, twoWeight);
        boneRot = Quaternion.Slerp(boneRot, r, twoWeight);
    }

    /// <summary>Self-test (vr.14b): the pose the hold last gave the held item (between both hands while two hold it),
    /// before physics carries it there; false without one.</summary>
    internal bool HeldTarget(out Vector3 pos, out Quaternion rot)
    {
        pos = itemTargetPos;
        rot = itemTargetRot;
        return haveItemTarget;
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
        lastAimTilt = 0f;
        if (hold.Barrel && AvatarConfig.Instance.AimHoldAlignment)
        {
            // Its barrel or blade along the hand's aim, turned about the fist (Core GripMath.AlongAim): the game's
            // Hand_R puts it 54-109 degrees off a real controller's aim pose (offline, from the prefabs), and shots go
            // along it (HandAim.TryBarrel), not where the controller points. The turn moves the fist's tube least,
            // the item held the other way round when that is less (the blowgun's barrel leaves the bottom of the fist).
            var p = pos.N();
            var r = rot.N();
            var turn = GripMath.AlongAim(AimInGrip(rig.IsLeft).N(), ref p, ref r);
            pos = p.U();
            rot = r.U();
            lastAimTilt = GripMath.FistTilt(turn);
        }
        return true;
    }

    /// <summary>A peer whose pose carries no aim (a build before the aim offsets, #26): its aim is taken as the grip
    /// turned by GripFrame.AimFromGrip, within about 15 degrees of a real controller's (40-60 degrees off the grip's
    /// +Z).</summary>
    private static readonly Vector3 PeerAimInGrip = GripFrame.AimFromGrip.U() * Vector3.forward;

    /// <summary>Degrees the last <see cref="ItemInController"/> turned the grip out of the fist (its +Z, the fist's
    /// tube) to lay the barrel along the aim; 0 when it did not.</summary>
    private float lastAimTilt;

    /// <summary>The <paramref name="left"/> hand's aim direction in its grip frame, from the pose: the tracked aim pose
    /// against its grip (<see cref="AvatarPose.HasAim"/>, the local player's and a modded peer's alike), else
    /// <see cref="PeerAimInGrip"/>.</summary>
    private Vector3 AimInGrip(bool left)
    {
        var pose = Pose;
        return pose.HasAim
            ? (left ? pose.LeftAimInGrip : pose.RightAimInGrip) * Vector3.forward
            : PeerAimInGrip;
    }

    /// <summary>Self-test: the held barrel item's one-hand rotation in the <paramref name="left"/> controller's frame
    /// (its barrel or blade along +Z) and how far that hold turns its grip out of the fist, degrees (0 with
    /// <c>Interaction.AimHoldAlignment</c> off). False when the held item has no barrel.</summary>
    internal bool BarrelInController(bool left, out Quaternion rot, out float tilt)
    {
        tilt = 0f;
        if (!HeldItemInController(left, out _, out rot) || !hold.Barrel)
            return false;
        tilt = lastAimTilt;
        return true;
    }
}
