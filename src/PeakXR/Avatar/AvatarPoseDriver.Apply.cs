using PeakXR.Core;
using UnityEngine;
using UnityEngine.Animations.Rigging;

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
    private Bodypart spine;
    private Bodypart torso;
    private Transform bodyFrame;

    /// <summary>The eyes (CustomizationRefs.EyeRenderers) in the head bone's frame, learned from the rendered pose;
    /// the lean aims this point at the tracked head.</summary>
    private Vector3 eyeInHead;
    private bool haveEyeInHead;
    private float eyeLearnedFor;

    /// <summary>Seconds the lean takes to fade in or out whenever a gate opens or closes, so the body never snaps.</summary>
    private const float LeanFadeSeconds = 0.3f;

    private float leanWeight;

    /// <summary>How much of the lean is on (eased 0..1); rig.camera samples the eyes only at 0.</summary>
    internal float LeanWeight => leanWeight;
    private Quaternion lastSwing = Quaternion.identity;

    /// <summary>How far this tick's lean moved the avatar's eyes (world metres, animated target). rig.camera only
    /// calibrates the eye height while this is small, so a lean never enters the calibration.</summary>
    internal Vector3 EyeLeanShift { get; private set; }

    /// <summary>Seconds of rendered frames <see cref="eyeInHead"/> is averaged over; the eyes are fixed on the head,
    /// so it stops reading renderer bounds after that.</summary>
    private const float EyeLearnSeconds = 3f;

    /// <summary>The prefab's elbow hint weights (0 on the scout), put back when a hand stops being tracked.</summary>
    private float hintWeightL;
    private float hintWeightR;
    private bool hintOnL;
    private bool hintOnR;

    /// <summary>Last tick's elbow bend direction per arm (<see cref="ArmMath.StableHint"/>), zero when the hint is off.</summary>
    private System.Numerics.Vector3 bendL;
    private System.Numerics.Vector3 bendR;
    private HandRig leftRig;
    private HandRig rightRig;
    private readonly ItemHoldInfo hold = new();
    private readonly ArmBlend armBlend = new();
    private FixedJoint itemJoint;
    private bool itemJointLeft;

    /// <summary>The item whose vanilla two-hand weld was last checked for replacement; one try per item.</summary>
    private Item vanillaWeldChecked;

    /// <summary>The held item and its primary side last tick, to see a handoff (the primary hand changed under the
    /// same item, HoldHands.HandOff).</summary>
    private Item handoffItem;
    private bool handoffLeft;

    /// <summary>When the last handoff began, and the item's pose then in the new primary controller's frame: the
    /// item eases from it onto the new grip over <see cref="HoldHands.EaseSeconds"/> instead of popping.</summary>
    private float handoffAt = -10f;
    private Vector3 handoffPos;
    private Quaternion handoffRot = Quaternion.identity;

    /// <summary>The handoff took the old primary's weld away: the new primary welds once the ease ends.</summary>
    private bool handoffReweld;

    /// <summary>True while this tick's held-item frame comes from the tracked hand; read by the hold torque patch.</summary>
    internal bool ItemFrameOverridden { get; private set; }

    /// <summary>Lets the local held item leave a ragdoll hand that cannot reach the controller (<see cref="WeldGate"/>).</summary>
    private readonly WeldGate weldGate = new();

    /// <summary>The held item has left its hand weld to follow the controller (self-test).</summary>
    internal bool HeldReleased => weldGate.Released;
    private Item followItem;
    private Vector3 lastTarget;
    private bool haveLastTarget;

    /// <summary>The local held item's distance from its controller-held pose this physics tick, metres; -1 when it is
    /// not hand-carried. Read by the self-test (vr.87).</summary>
    internal float HeldItemGap { get; private set; } = -1f;

    /// <summary>The free item whose colliders ignore this body's, and those colliders (local physics only).</summary>
    private Item passItem;
    private Collider[] passItemColliders;
    private Collider[] bodyColliders;
    private float freeLogAt;

    /// <summary>How much the tracked hands drive the arms now (0 while the game ragdolls the body, eased back in).
    /// Read by the self-test.</summary>
    internal float ArmWeight => armBlend.Weight;

    /// <summary>Whether tracked hands may drive <paramref name="c"/>'s arms: the game is not ragdolling the body
    /// (<see cref="ArmBlend.Tracked"/>). net.pose also stops streaming poses while this is false.</summary>
    internal static bool ArmsTracked(Character c)
    {
        var d = c.data;
        return d != null
            && ArmBlend.Tracked(
                d.passedOut || d.fullyPassedOut || d.dead || d.isCarried,
                d.fallSeconds,
                d.currentRagdollControll
            );
    }

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
        parts.TryGetValue(BodypartType.Torso, out torso);
        // Bend low in the back so the shoulders travel with the head; the scout's spine is Hip > Mid > Torso.
        if (!parts.TryGetValue(BodypartType.Mid, out spine))
            spine = torso;
        if (c.IsLocal)
            Plugin.Log.LogInfo(
                $"[avatar.local] {c.name}: {parts.Count} bodyparts, lean bends {(spine != null ? spine.partType.ToString() : "nothing")}, physics step {Time.fixedDeltaTime * 1000f:0.0} ms"
            );
        SplitForLegShift(c.refs.ragdoll.partList);
        if (c.IsLocal)
            ArmsPastHead(parts);
        bodyFrame = c.refs.rigCreator != null ? c.refs.rigCreator.transform : c.transform;
        if (c.refs.ikLeft != null)
            hintWeightL = c.refs.ikLeft.data.hintWeight;
        if (c.refs.ikRight != null)
            hintWeightR = c.refs.ikRight.data.hintWeight;

        var smr = c.refs.mainRenderer;
        leftRig = new HandRig(handL.transform, true, FingerNamesLeft, smr);
        rightRig = new HandRig(handR.transform, false, FingerNamesRight, smr);
        InitTwist(parts, handL.transform, handR.transform, smr);
        if (!leftRig.HasFingers && !rightRig.HasFingers)
            Plugin.Log.LogInfo($"[avatar] {c.name}: no finger bones found, finger curls skipped");
        rigReady = true;
        return true;
    }

    /// <summary>
    /// The local VR player's arms pass beside the scout's oversized head instead of bumping it. A controller raised
    /// overhead puts the upper arm about 16 cm from the head's centre, inside the scout's head collider, so the ragdoll
    /// arm was shoved 12-15 degrees off its target and the hand landed 15 cm short (run 48, vr.08), though a person's
    /// head never blocks that reach. Local physics only: peers see the network pose. Only while VR runs
    /// (<see cref="LateUpdate"/> follows the session).
    /// </summary>
    private void ArmsPastHead(System.Collections.Generic.Dictionary<BodypartType, Bodypart> parts)
    {
        if (!parts.TryGetValue(BodypartType.Head, out var headPart))
            return;
        var headColliders = headPart.GetComponentsInChildren<Collider>();
        foreach (var side in ArmRoots)
        {
            if (!parts.TryGetValue(side, out var arm))
                continue;
            foreach (var a in arm.GetComponentsInChildren<Collider>())
            {
                if (a.isTrigger)
                    continue;
                foreach (var h in headColliders)
                {
                    if (!h.isTrigger && h != a)
                        headPairs.Add((a, h));
                }
            }
        }
        SetArmsPastHead(PeakXR.VR.VRSession.Active);
        Plugin.Log.LogInfo(
            $"[avatar.local] arms pass the head while in VR: {headPairs.Count} collider pairs"
        );
    }

    /// <summary>The arm and head collider pairs <see cref="ArmsPastHead"/> ignores: a flat body bumps its head again
    /// once VR stops.</summary>
    private readonly System.Collections.Generic.List<(Collider arm, Collider head)> headPairs =
        new();
    private bool armsPastHeadOn;

    private void SetArmsPastHead(bool ignore)
    {
        armsPastHeadOn = ignore;
        foreach (var (a, h) in headPairs)
        {
            if (a != null && h != null)
                Physics.IgnoreCollision(a, h, ignore);
        }
    }

    /// <summary>The upper arms: their children carry the elbows and hands.</summary>
    private static readonly BodypartType[] ArmRoots = { BodypartType.Arm_L, BodypartType.Arm_R };

    private Vector3 AnimatedOffset() => hip.transform.position - hip.Rig.position;

    /// <summary>The parts the lean carries (under <see cref="spine"/>, minus the controller-driven elbows and hands),
    /// their masses, and their animated positions before this tick's bend.</summary>
    private Transform[] bentParts = System.Array.Empty<Transform>();
    private float[] bentMass = System.Array.Empty<float>();
    private Vector3[] bentBefore = System.Array.Empty<Vector3>();

    /// <summary>The topmost leg bones (neither the hip nor under the spine): shifting them moves the whole legs'
    /// animated pose relative to the hip.</summary>
    private Transform[] legRoots = System.Array.Empty<Transform>();

    /// <summary>The leg roots' local positions without a shift. The shift is set from these each tick rather than
    /// added: an animation that only rotates a bone never writes its position back.</summary>
    private Vector3[] legRest = System.Array.Empty<Vector3>();
    private float bodyMass;
    private float legMass;

    /// <summary>This tick's <see cref="BodyLean.LegShift"/> (world, animated pose). Traced.</summary>
    internal Vector3 LegShiftNow { get; private set; }

    private void SplitForLegShift(System.Collections.Generic.List<Bodypart> parts)
    {
        var bent = new System.Collections.Generic.List<Bodypart>();
        var legs = new System.Collections.Generic.List<Transform>();
        bodyMass = legMass = 0f;
        foreach (var part in parts)
        {
            if (part == null || part.Rig == null)
                continue;
            float mass = part.Rig.mass;
            bodyMass += mass;
            if (part == hip)
                continue;
            if (spine != null && part.transform.IsChildOf(spine.transform))
            {
                if (!IsArmEnd(part.partType))
                    bent.Add(part);
                continue;
            }
            legMass += mass;
            legs.Add(part.transform);
        }
        bentParts = new Transform[bent.Count];
        bentMass = new float[bent.Count];
        bentBefore = new Vector3[bent.Count];
        for (int i = 0; i < bent.Count; i++)
        {
            bentParts[i] = bent[i].transform;
            bentMass[i] = bent[i].Rig.mass;
        }
        legRoots = legs.FindAll(t => !legs.Exists(o => o != t && t.IsChildOf(o))).ToArray();
        legRest = System.Array.ConvertAll(legRoots, t => t.localPosition);
    }

    /// <summary>Puts the leg roots at their rest offset plus <paramref name="shift"/> (world).</summary>
    private void ShiftLegs(Vector3 shift)
    {
        // Unshifted and staying so: leave the animator's leg roots alone rather than write the init capture.
        if (shift == Vector3.zero && LegShiftNow == Vector3.zero)
            return;
        LegShiftNow = shift;
        for (int i = 0; i < legRoots.Length; i++)
        {
            var leg = legRoots[i];
            leg.localPosition =
                legRest[i]
                + (leg.parent != null ? leg.parent.InverseTransformVector(shift) : shift);
        }
    }

    private readonly BodyHold bodyHold = new();

    /// <summary>The lean's <see cref="BodyHold"/> is holding the body this tick. Traced.</summary>
    internal bool BodyHeld => bodyHold.Holding;

    /// <summary>
    /// Local player only (a remote body follows the network): while the lean bends the avatar on the ground with
    /// no move input, <see cref="BodyHold"/> pulls the ragdoll's mass centre back to where the lean started, with the
    /// same acceleration on every part (<c>Bodypart.AddForce</c>, Bodypart.cs:262, applied in the movement loop,
    /// Bodypart.cs:286), so the pose is untouched.
    /// </summary>
    /// <summary>The animator's water blend (<c>CharacterAnimations.AN_INWATER</c>, CharacterAnimations.cs:111).</summary>
    private static readonly int InWaterParam = Animator.StringToHash("InWater");

    /// <summary>This tick's closed <see cref="HoldGate"/>, or null. Traced.</summary>
    private string holdGateWhy;
    private float lastLeanDegrees;

    /// <summary>What the game says about <paramref name="c"/>'s body this tick, for <see cref="HoldGate"/> (the lean's
    /// body hold here, rig.camera's view hold).</summary>
    internal static HoldInputs GateInputs(Character c)
    {
        var d = c.data;
        var movement = c.refs.movement;
        var animator = c.refs.animator;
        return new HoldInputs
        {
            Grounded = d.isGrounded,
            Upright = Rig.RigDriver.Upright(c),
            MoveInput = c.input == null || c.input.movementInput.sqrMagnitude >= 1e-4f,
            Carried = d.isCarried || d.carrier != null || d.carrierCondor != null,
            // The animator keeps the blend WaterZone wrote; data.inWater is back at 1 once
            // CharacterAnimations fed it (CharacterAnimations.cs:283-284).
            WaterBlend = animator != null ? animator.GetFloat(InWaterParam) : d.inWater,
            Warping = c.warping,
            SinceFallSlide = d.sinceFallSlide,
            SinceBounce = Time.time - d.lastBouncedTime,
            SlopeDeg = Vector3.Angle(d.groundNormal, Vector3.up),
            MaxSlopeDeg =
                movement != null && AvatarPatches.MaxAngle != null
                    ? AvatarPatches.MaxAngle(movement)
                    : 50f,
        };
    }

    private void HoldBody()
    {
        var c = Owner;
        var parts = c.refs.ragdoll.partList;
        holdGateWhy = HoldGate.Why(GateInputs(c));
        bool free = holdGateWhy == null;
        var sum = Vector3.zero;
        var momentum = Vector3.zero;
        float mass = 0f;
        for (int i = 0; i < parts.Count; i++)
        {
            var body = parts[i] != null ? parts[i].Rig : null;
            if (body == null)
                continue;
            sum += body.worldCenterOfMass * body.mass;
            momentum += body.linearVelocity * body.mass;
            mass += body.mass;
        }
        if (mass <= 0f)
            return;
        var centre = sum / mass;
        var velocity = momentum / mass;
        float dt = Time.fixedDeltaTime;
        float bendRate = dt > 0f ? (LeanDegrees - lastLeanDegrees) / dt : 0f;
        lastLeanDegrees = LeanDegrees;
        var a = bodyHold.Update(
            new System.Numerics.Vector2(centre.x, centre.z),
            new System.Numerics.Vector2(velocity.x, velocity.z),
            LeanDegrees >= 1f,
            free,
            dt,
            bendRate
        );
        if (a == System.Numerics.Vector2.Zero)
            return;
        var accel = new Vector3(a.X, 0f, a.Y);
        for (int i = 0; i < parts.Count; i++)
            if (parts[i] != null)
                parts[i].AddForce(accel, ForceMode.Acceleration);
    }

    private static bool IsArmEnd(BodypartType t) =>
        t
            is BodypartType.Elbow_L
                or BodypartType.Elbow_R
                or BodypartType.Hand_L
                or BodypartType.Hand_R;

    private void LateUpdate()
    {
        var c = Owner;
        if (headPairs.Count > 0 && armsPastHeadOn != PeakXR.VR.VRSession.Active)
            SetArmsPastHead(PeakXR.VR.VRSession.Active);
        if (HasFreshPose && c != null)
            armBlend.Update(ArmsTracked(c), Time.deltaTime);
        else
            armBlend.Reset();
        LearnEyes(c);
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
        ClearReach();
        if (!HasFreshPose)
        {
            // Vanilla animation takes the whole body back (the arms snap back too), so the lean ends with it.
            EyeLeanShift = Vector3.zero;
            UnleanedEye = null;
            ShiftLegs(Vector3.zero);
            leanWeight = 0f;
            LeanGate = "stale pose";
            SpreadTwist(true, false);
            SpreadTwist(false, false);
            FadeUnreached();
            if (rigReady)
                RestoreFingers();
            return;
        }
        if (!EnsureRig())
        {
            FadeUnreached();
            return;
        }
        var refs = Owner.refs;
        var cfg = AvatarConfig.Instance;

        float w = armBlend.Weight;
        var tipL = TipRotation(refs.ikLeft);
        var tipR = TipRotation(refs.ikRight);
        bool leaned = ApplyLean(w);
        if (Owner.IsLocal)
            HoldBody();
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
                BlendTarget(refs.IKHandTargetLeft, Reach(true, refs.ikLeft, p + offset), r, w);
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
                BlendTarget(refs.IKHandTargetRight, Reach(false, refs.ikRight, p + offset), r, w);
            }
            SetHint(false, pose.HasRightHand, refs.IKHandTargetRight.position, w);
            // The IK solved this tick's arms on the unleaned shoulders; reach again from where the lean put them.
            if (leaned && pose.HasLeftHand)
                ReachAfterLean(refs.ikLeft, refs.IKHandTargetLeft, tipL, w);
            if (leaned && pose.HasRightHand)
                ReachAfterLean(refs.ikRight, refs.IKHandTargetRight, tipR, w);
            SpreadTwist(true, pose.HasLeftHand);
            SpreadTwist(false, pose.HasRightHand);
        }
        else
        {
            SpreadTwist(true, false);
            SpreadTwist(false, false);
        }
        FadeUnreached();

        // The local player's head is hidden and the camera hangs off it, so only other characters get head look.
        if (cfg.HeadLook && head != null && Pose.HasHead && !Owner.IsLocal)
        {
            var look = Owner.data.lookValues;
            var delta = GripMath.HeadDelta(Pose.HeadRotation.N(), look.x, look.y, 70f).U();
            head.transform.rotation = delta * head.transform.rotation;
        }

        ApplyFingers();
    }

    /// <summary>
    /// Bends the animated spine (<see cref="spine"/>, about its own joint) so the avatar's eyes swing toward the
    /// tracked head within <see cref="BodyLean"/>'s limits; the head keeps its world rotation (the neck takes it).
    /// Runs in the animated pose before <c>SaveAnimationData</c> (CharacterRagdoll.cs:170-174), so the ragdoll is
    /// driven into the lean like any animation, for remote characters as well. Every gate fades the lean over
    /// <see cref="LeanFadeSeconds"/> (the last swing is held while it fades): climbing, a piton or rope hang
    /// (<c>currentClimbHandle</c>, set by <c>StartHang</c>), gliding, a full-body emote, the setting, the head below
    /// the pivot, and for the local player an uncalibrated eye (rig.camera samples the unleaned eyes first).
    /// </summary>
    private bool ApplyLean(float w)
    {
        EyeLeanShift = Vector3.zero;
        LeanDegrees = 0f;
        LeanShoulderShift = Vector3.zero;
        ShiftLegs(Vector3.zero);
        UnleanedEye = null;
        if (spine == null || torso == null || head == null || !haveEyeInHead)
        {
            LeanGate =
                head == null || torso == null ? "no head or torso bodypart" : "eyes not learned";
            TraceLean(Vector3.zero, Vector3.zero);
            return false;
        }
        var c = Owner;
        var forward = bodyFrame.forward;
        forward.y = 0f;
        var eye = head.transform.TransformPoint(eyeInHead);
        UnleanedEye = eye - hip.transform.position;
        var target = Pose.HeadPosition + AnimatedOffset();
        LeanGate = ClosedGate(c, w, forward);
        bool allowed = false;
        if (
            LeanGate == null
            && BodyLean.TrySwing(
                spine.transform.position.N(),
                eye.N(),
                // A still head bends nothing (BodyLean.WantDeadband), or the hip's own sway feeds back (MP run 9).
                eye.N() + BodyLean.Deadband((target - eye).N(), Vector3.up.N()),
                forward.normalized.N(),
                Vector3.up.N(),
                out var swing
            )
        )
        {
            lastSwing = swing.U();
            allowed = true;
        }
        else
            LeanGate ??= "eye or head not above the spine";
        leanWeight = Mathf.MoveTowards(
            leanWeight,
            allowed ? 1f : 0f,
            Time.fixedDeltaTime / LeanFadeSeconds
        );
        float amount = leanWeight * w;
        if (amount <= 0f)
        {
            TraceLean(target - eye, forward);
            return false;
        }
        var q = Quaternion.Slerp(Quaternion.identity, lastSwing, amount);
        var headRot = head.transform.rotation;
        var shoulders = ShoulderMid();
        for (int i = 0; i < bentParts.Length; i++)
            bentBefore[i] = bentParts[i].position;
        spine.transform.rotation = q * spine.transform.rotation;
        head.transform.rotation = headRot;
        // Keep the mass centre over the planted feet (BodyLean.LegShift): the hip goes back instead of the whole
        // body creeping toward the lean and back on standing up.
        var moment = Vector3.zero;
        for (int i = 0; i < bentParts.Length; i++)
            moment += bentMass[i] * (bentParts[i].position - bentBefore[i]);
        ShiftLegs(BodyLean.LegShift(moment.N(), bodyMass, legMass).U());
        EyeLeanShift = head.transform.TransformPoint(eyeInHead) - eye;
        LeanShoulderShift = ShoulderMid() - shoulders;
        LeanDegrees = Quaternion.Angle(Quaternion.identity, q);
        TraceLean(target - eye, forward);
        return true;
    }

    /// <summary>The first lean gate that is closed, or null when all are open (logged and read by vr.45).</summary>
    private string ClosedGate(Character c, float w, Vector3 forward)
    {
        if (!AvatarConfig.Instance.BodyLean)
            return "setting off";
        if (!Pose.HasHead)
            return "no head pose";
        if (ArmsSuspended)
            return "arms suspended";
        if (w <= 0f)
            return "arms not tracked";
        if (forward.sqrMagnitude <= 1e-4f)
            return "no body forward";
        if (!Rig.RigDriver.Upright(c))
            return "not upright";
        if (c.IsLocal && !Rig.RigDriver.EyeCalibrated)
            return "eyes not calibrated";
        return null;
    }

    private Vector3 ShoulderMid() =>
        shoulderL != null && shoulderR != null
            ? (shoulderL.transform.position + shoulderR.transform.position) * 0.5f
            : torso.transform.position;

    /// <summary>Why the lean is off this tick, or null while it may run. Read by the self-test.</summary>
    internal string LeanGate { get; private set; } = "not run";

    /// <summary>Degrees of spine bend applied this tick (weighted), and how far it moved the animated shoulders
    /// (world metres). Read by the self-test, which compares them with what the ragdoll does.</summary>
    internal float LeanDegrees { get; private set; }

    internal Vector3 LeanShoulderShift { get; private set; }

    /// <summary>This tick's animated eyes before the bend, relative to the animated hip: where the lean starts from.
    /// avatar.local carries it onto the rendered frame's hip (<see cref="BodyLean.HeadBias"/>) and sends the head
    /// relative to it, so the head on the calibrated eyes is upright.</summary>
    internal Vector3? UnleanedEye { get; private set; }

    private float nextLeanTrace;

    /// <summary>Lean trace, avatar side (local player only): the gate, the eased weight, what the head asks of the
    /// animated eye (forward, right), the bend applied and how far it moved the animated eye and shoulders, and the
    /// physical torso and shoulders against the physical hip. Proves whether the bend is made and whether the
    /// ragdoll follows it.</summary>
    private void TraceLean(Vector3 want, Vector3 forward)
    {
        var c = Owner;
        if (!c.IsLocal || hip == null || !Rig.RigDriver.LeanTraceDue(ref nextLeanTrace))
            return;
        var f = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
        var r = new Vector3(f.z, 0f, -f.x);
        var hipPos = hip.Rig.position;
        var torsoPhys = torso != null ? torso.Rig.position - hipPos : Vector3.zero;
        var shoulderPhys =
            shoulderL != null && shoulderR != null
                ? (shoulderL.Rig.position + shoulderR.Rig.position) * 0.5f - hipPos
                : torsoPhys;
        Plugin.Log.LogInfo(
            System.FormattableString.Invariant(
                $"[avatar.local] lean t{Time.unscaledTime:0.00} gate {LeanGate ?? "open"} weight {leanWeight:0.00} spine {(spine != null ? spine.partType.ToString() : "none")} want f{Vector3.Dot(want, f):0.000} r{Vector3.Dot(want, r):0.000} u{want.y:0.000} bend {LeanDegrees:0.0} eye-shift f{Vector3.Dot(EyeLeanShift, f):0.000} shoulder-shift f{Vector3.Dot(LeanShoulderShift, f):0.000} r{Vector3.Dot(LeanShoulderShift, r):0.000} leg-shift f{Vector3.Dot(LegShiftNow, f):0.000} r{Vector3.Dot(LegShiftNow, r):0.000} hold {(bodyHold.Holding ? "1" : "0 (" + (holdGateWhy ?? bodyHold.Why) + ")")} | phys torso-hip f{Vector3.Dot(torsoPhys, f):0.000} shoulders-hip f{Vector3.Dot(shoulderPhys, f):0.000} r{Vector3.Dot(shoulderPhys, r):0.000} hip ({hipPos.x:0.000},{hipPos.y:0.000},{hipPos.z:0.000}) head-height {c.data.currentHeadHeight:0.000} target {c.data.targetHeadHeight:0.000} anim-offset {AnimatedOffset().magnitude:0.000}"
            )
        );
    }

    private static Quaternion TipRotation(TwoBoneIKConstraint ik) =>
        ik != null && ik.data.tip != null ? ik.data.tip.rotation : Quaternion.identity;

    /// <summary>Re-solves one arm (the constraint's own root, mid and tip) onto its IK target after the lean moved
    /// the shoulder, in the bend plane the IK left (<see cref="ArmMath.SolveTwoBone"/>), and puts the hand back to
    /// its pre-lean world rotation.</summary>
    private static void ReachAfterLean(
        TwoBoneIKConstraint ik,
        Transform target,
        Quaternion tipRotation,
        float w
    )
    {
        if (ik == null)
            return;
        var root = ik.data.root;
        var mid = ik.data.mid;
        var tip = ik.data.tip;
        if (root == null || mid == null || tip == null)
            return;
        var hint = ik.data.hint != null ? ik.data.hint.position : mid.position;
        ArmMath.SolveTwoBone(
            root.position.N(),
            mid.position.N(),
            tip.position.N(),
            target.position.N(),
            hint.N(),
            out var upper,
            out var lower
        );
        var midRot = mid.rotation;
        root.rotation = Quaternion.Slerp(Quaternion.identity, upper.U(), w) * root.rotation;
        mid.rotation = Quaternion.Slerp(Quaternion.identity, lower.U(), w) * midRot;
        tip.rotation = tipRotation;
    }

    /// <summary>Learns <see cref="eyeInHead"/> as a slow average over rendered frames (the head and eyes are posed
    /// from physics here, so both sides of the measurement agree).</summary>
    private void LearnEyes(Character c)
    {
        if (
            eyeLearnedFor >= EyeLearnSeconds
            || !rigReady
            || head == null
            || c == null
            || !Rig.RigDriver.TryEyeRenderers(c, out var eye)
        )
            return;
        var local = head.transform.InverseTransformPoint(eye);
        eyeInHead = haveEyeInHead
            ? Vector3.Lerp(eyeInHead, local, 1f - Mathf.Exp(-Time.deltaTime))
            : local;
        haveEyeInHead = true;
        eyeLearnedFor += Time.deltaTime;
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
        ref var bend = ref left ? ref bendL : ref bendR;
        if (on)
        {
            var s = shoulder.transform.position.N();
            var hint = ArmMath.ElbowHint(
                s,
                hand.N(),
                bodyFrame.right.N(),
                bodyFrame.up.N(),
                bodyFrame.forward.N(),
                left
            );
            data.hint.position = ArmMath.StableHint(s, hand.N(), hint, ref bend).U();
            data.hintWeight = Mathf.Lerp(left ? hintWeightL : hintWeightR, 1f, w);
        }
        else
        {
            bend = default;
            if (left ? hintOnL : hintOnR)
                data.hintWeight = left ? hintWeightL : hintWeightR;
        }
        if (left)
            hintOnL = on;
        else
            hintOnR = on;
    }

    /// <summary>The wrist bone pose a tracked hand asks for, in physics (world) space; false when the hand is not
    /// tracked. The self-test compares it with where the ragdoll hand really is.</summary>
    internal bool HandTarget(bool left, out Vector3 pos) => HandTarget(left, out pos, out _);

    /// <summary>How far the <paramref name="left"/> arm falls short of where its controller puts the hand bone
    /// (<see cref="ArmShortfall"/>: geometry, not lag), what <see cref="ReachGhost"/> fades on. False without a fresh
    /// pose for that hand or while the arms are let go.</summary>
    internal bool ReachGap(bool left, out float gap) =>
        ReachGap(left, out gap, out _, out _, out _);

    /// <inheritdoc cref="ReachGap(bool, out float)"/>
    /// <param name="shoulder">The shoulder in the physics frame, before the body-to-head shift.</param>
    /// <param name="bone">Where the controller puts the hand bone.</param>
    /// <param name="arm">The reach the hand is judged on: the arm, or the player's reach while the reach map carries
    /// the hand (<see cref="ArmReach.GhostReach"/>).</param>
    internal bool ReachGap(
        bool left,
        out float gap,
        out Vector3 shoulder,
        out Vector3 bone,
        out float arm
    )
    {
        gap = 0f;
        shoulder = bone = default;
        arm = 0f;
        var pose = Pose;
        if (
            !HasFreshPose
            || ArmsSuspended
            || armBlend.Weight < 1f
            || !EnsureRig()
            || (left ? !pose.HasLeftHand : !pose.HasRightHand)
        )
            return false;
        (left ? leftRig : rightRig).BoneFromController(
            left ? pose.LeftHandPosition : pose.RightHandPosition,
            left ? pose.LeftHandRotation : pose.RightHandRotation,
            out bone,
            out _
        );
        // Reach past the arm from where the shoulder belongs under the tracked head, never the body's lag (vr72).
        if (!PhysicsShoulder(left, out shoulder, out arm))
            return false;
        // An empty standing hand is mapped onto the arm out to the player's own reach (Reach): only past that is it
        // out of reach. The bare arm alone showed the ghost at rest under a rig above x1 (vr.173, run 17).
        arm = ArmReach.GhostReach(
            arm,
            Mathf.Max(reachL.Reach, reachR.Reach),
            left ? reachWeightL : reachWeightR
        );
        gap = GhostFade.Shortfall(shoulder.N(), BodyToHead().N(), bone.N(), arm);
        return true;
    }

    /// <inheritdoc cref="HandTarget(bool, out Vector3)"/>
    internal bool HandTarget(bool left, out Vector3 pos, out Quaternion rot)
    {
        pos = default;
        rot = Quaternion.identity;
        var pose = Pose;
        if (!HasFreshPose || !EnsureRig() || (left ? !pose.HasLeftHand : !pose.HasRightHand))
            return false;
        (left ? leftRig : rightRig).BoneFromController(
            left ? pose.LeftHandPosition : pose.RightHandPosition,
            left ? pose.LeftHandRotation : pose.RightHandRotation,
            out pos,
            out rot
        );
        if (left ? haveReachL : haveReachR)
            pos += left ? reachShiftL : reachShiftR;
        return true;
    }

    /// <summary>The player's reach, learned per arm (<see cref="ReachLearner"/>); the map uses the longer.</summary>
    private readonly ReachLearner reachL = new();
    private readonly ReachLearner reachR = new();
    private float reachWeightL;
    private float reachWeightR;
    private bool haveReachL;
    private bool haveReachR;
    private Vector3 reachShiftL;
    private Vector3 reachShiftR;

    /// <summary>How far the reach map holds this hand's target off its controller (m). Read by the self-test.</summary>
    internal float ReachPull(bool left) => (left ? reachShiftL : reachShiftR).magnitude;

    /// <summary>Drops this tick's reach shifts, so <see cref="HandTarget"/> never adds one from a tick that skipped
    /// <see cref="Reach"/>.</summary>
    private void ClearReach()
    {
        haveReachL = haveReachR = false;
        reachShiftL = reachShiftR = Vector3.zero;
    }

    /// <summary>Fades out the map of a hand <see cref="Reach"/> did not run for this tick.</summary>
    private void FadeUnreached()
    {
        float dt = Time.fixedDeltaTime;
        if (!haveReachL)
            reachWeightL = ArmReach.StepWeight(reachWeightL, false, false, dt);
        if (!haveReachR)
            reachWeightR = ArmReach.StepWeight(reachWeightR, false, false, dt);
    }

    /// <summary>
    /// <paramref name="target"/> (the controller's hand bone, animated space) mapped onto the avatar's reach from the
    /// arm's shoulder (<see cref="ArmReach"/>), while standing with that hand empty. A hand holding an item is on the
    /// controller from the grab's first tick (<see cref="ArmReach.StepWeight"/>): the item follows the controller pose
    /// and is welded to the hand. Climbing, hanging and gliding fade back to the raw controller. Also learns the
    /// player's reach while the body is at rest and, for the local player, the view is not settling after a warp,
    /// spawn or takeover.
    /// </summary>
    private Vector3 Reach(bool left, TwoBoneIKConstraint ik, Vector3 target)
    {
        var c = Owner;
        var item = c.data.currentItem;
        bool holding = item != null && (Pose.TwoHandHold || Pose.ItemInLeftHand == left);
        bool upright = Rig.RigDriver.Upright(c);
        float dt = Time.fixedDeltaTime;
        ref float weight = ref left ? ref reachWeightL : ref reachWeightR;
        weight = ArmReach.StepWeight(weight, holding, upright, dt);
        var result = target;
        if (ik != null && ik.data.root != null && ik.data.mid != null && ik.data.tip != null)
        {
            var shoulder = ik.data.root.position;
            var mid = ik.data.mid.position;
            float arm =
                Vector3.Distance(shoulder, mid) + Vector3.Distance(mid, ik.data.tip.position);
            bool learn =
                upright
                && !holding
                && !c.warping
                && hip.Rig.linearVelocity.sqrMagnitude < 0.25f
                && !(c.IsLocal && Rig.RigDriver.ViewSettling);
            (left ? reachL : reachR).Sample(
                Vector3.Distance(shoulder, target),
                target.y - shoulder.y,
                arm,
                learn,
                dt
            );
            if (weight > 0f)
                result = Vector3.Lerp(
                    target,
                    ArmReach
                        .Target(
                            shoulder.N(),
                            target.N(),
                            arm,
                            Mathf.Max(reachL.Reach, reachR.Reach)
                        )
                        .U(),
                    weight
                );
        }
        if (left)
        {
            haveReachL = true;
            reachShiftL = result - target;
        }
        else
        {
            haveReachR = true;
            reachShiftR = result - target;
        }
        return result;
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

    /// <summary>Tracked head minus the avatar's eyes (zero until the eyes are learned): how far the body lags the
    /// head.</summary>
    internal Vector3 BodyToHead() =>
        haveEyeInHead && head != null
            ? Pose.HeadPosition - head.transform.TransformPoint(eyeInHead)
            : Vector3.zero;

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
        // The local VR player's smaller drawn fingers stop on the item sooner; the pose (and peers) keep the
        // game-size curls.
        if (pose.HasLeftHand)
            leftRig.Curl(DrawnHands.Curls(Owner, true, pose.LeftFingers));
        if (pose.HasRightHand)
            rightRig.Curl(DrawnHands.Curls(Owner, false, pose.RightFingers));
    }

    /// <summary>A hand's rig once calibrated, else null.</summary>
    internal HandRig HandRigOf(bool left) => rigReady ? (left ? leftRig : rightRig) : null;

    private void RestoreFingers()
    {
        leftRig?.Restore();
        rightRig?.Restore();
    }
}
