using System;
using System.Collections;
using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Inventory;
using PeakXR.VR;
using Photon.Pun;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

namespace PeakXR.Sandbox;

/// <summary>
/// <c>vr.08-arm-reach</c>: whether the tracked hands drive the whole arm. Samples the ragdoll arm chain
/// (<c>Arm</c>, <c>Elbow</c>, <c>Hand</c> bodyparts, physics space) against the controller-derived wrist target. The
/// chain's joints are locked (ConfigurableJoint x/y/zMotion 0 in the Character prefab), so a stretch ratio well
/// above 1 means a force is tearing the hand off the forearm; a ratio near 1 with the hand on target means the IK
/// pose itself is what the player sees. With the idle hands in front of the face it fails above 1.15 stretch,
/// when an elbow points up, or when an empty hand sits more than 0.15 m off its target (each hand follows only its
/// own controller); with real controllers (hold mode) it only reports. The scripted hands then hold two more reaches
/// (<see cref="ReachPoses"/>, <see cref="JudgeReaches"/>) and still rolled wrists (<see cref="JudgeWristRolls"/>). Either way it fails when the eye, less the idle-sway hold's
/// deliberate offset (<see cref="Rig.RigDriver.ViewHeld"/>), sits more than <see cref="SelfTestChecks.MaxEyeError"/>
/// from the avatar's eye renderers (and says so when it has none), or when that hold exceeds its deadband.
/// </summary>
internal sealed partial class SelfTestRunner
{
    const float MaxArmStretch = 1.15f;

    /// <summary>Seconds a step waits for the arms to be tracked (the beach spawn ragdoll included).</summary>
    const float ArmTrackedWait = 12f;

    /// <summary>Elbow bend component along the body's back that fails an idle arm. The elbow hint itself points
    /// about 0.26 back (<see cref="ArmMath.ElbowHint"/>); an arm twisted behind the torso reads about 0.75.</summary>
    const float MaxElbowBack = 0.5f;

    /// <summary>Mean elbow offset (m) off the shoulder-hand line below which the arm counts as straight.</summary>
    const float MinJudgedBend = 0.03f;

    /// <summary>Mean wrist-to-target error that fails an idle, empty hand: each hand follows only its controller,
    /// mapped onto the avatar's reach (<see cref="ArmReach"/>; <see cref="AvatarPoseDriver.HandTarget"/>).</summary>
    const float MaxIdleTargetError = 0.15f;
    const int ArmSamples = 50;

    struct ArmSample
    {
        public float TargetError;
        public float MaxTargetError;
        public float Fore;
        public float Upper;
        public float MaxStretch;
        public float ShoulderToTarget;
        public Vector3 Bend;
        public float Follow;
        public int Count;
    }

    /// <summary>Reaches the arms are judged in besides the idle pose, in the head's level frame (m: out to the hand's
    /// own side, up, forward): out to the side and up, and overhead like vr.43's vine grip. Both well inside a
    /// player's reach (<see cref="MaxHandFromEye"/>).</summary>
    static readonly Vector3[] ReachPoses = { new(0.45f, 0.1f, 0.35f), new(0.12f, 0.4f, 0.25f) };

    /// <summary>Degrees the avatar's arm (shoulder to hand) may point off the line from the shoulder to its
    /// controller: the reach map keeps the controller's direction (<see cref="ArmReach.Target"/>), less the
    /// controller-to-wrist offset.</summary>
    const float MaxFollowDegrees = 25f;

    IEnumerator ArmReachCheck()
    {
        var fails = new List<string>();
        var notes = new List<string>();
        var c = Character.localCharacter;
        var driver = c != null ? AvatarPoseDriver.Of(c) : null;
        if (driver == null || c.refs.ragdoll == null || c.refs.ragdoll.partDict.Count == 0)
        {
            fails.Add("no pose driver or ragdoll on the local character");
            Conclude(fails, notes);
            yield break;
        }
        bool idle = VRInput.TestOverride == IdleHands;
        // The beach spawn ragdolls the body (Fall(7f), Character.cs:2035) and the arms only take the tracked hands
        // once the body is up again.
        float waitStart = Now;
        yield return WaitFor(() => driver.ArmWeight >= 1f, ArmTrackedWait);
        notes.Add(F($"arms tracked after {Now - waitStart:0.0} s"));
        if (driver.ArmWeight < 1f)
        {
            fails.Add(F($"arm weight {driver.ArmWeight:0.00} after {ArmTrackedWait:0} s"));
            Conclude(fails, notes);
            yield break;
        }
        // Let the arms settle on the current hands before measuring.
        yield return Wait(1f);

        var left = default(ArmSample);
        var right = default(ArmSample);
        yield return SampleArms(c, driver, () => left, v => left = v, () => right, v => right = v);
        yield return ShotAt("vr.08-arm-reach", HandsMid());

        var parts = c.refs.ragdoll.partDict;
        var head = VRRig.Instance != null ? VRRig.Instance.Head : null;
        if (
            head != null
            && parts.TryGetValue(BodypartType.Arm_L, out var sl)
            && parts.TryGetValue(BodypartType.Arm_R, out var sr)
        )
        {
            float drop = head.position.y - 0.5f * (sl.Rig.position.y + sr.Rig.position.y);
            notes.Add(F($"eye {drop:0.00} m above the shoulders"));
        }
        // The view must ride the avatar's own eyes (scouts have big heads, so no human eye-to-shoulder rule). The
        // idle-sway hold keeps it up to its deadband off them on purpose (ViewHold; runs 45-46 read that as a 0.08 m
        // misplacement), so the placement is judged without it and the hold on its own.
        if (head != null && Rig.RigDriver.TryEyeRenderers(c, out var avatarEye))
        {
            var held = Rig.RigDriver.ViewHeld;
            var off = head.position - held - avatarEye;
            notes.Add(
                F(
                    $"eye {off.magnitude:0.00} m from the avatar's eyes (up {off.y:0.00}, level {new Vector2(off.x, off.z).magnitude:0.00}) with the view hold's {held.magnitude:0.00} m taken out"
                )
            );
            if (off.magnitude > SelfTestChecks.MaxEyeError)
                fails.Add(
                    F(
                        $"eye {off.magnitude:0.00} m from the avatar's eyes > {SelfTestChecks.MaxEyeError:0.00} m"
                    )
                );
            if (held.magnitude > ViewHold.Deadband + 0.01f)
                fails.Add(
                    F(
                        $"the view hold keeps the view {held.magnitude:0.00} m off the body, past its {ViewHold.Deadband:0.00} m deadband"
                    )
                );
        }
        else
        {
            notes.Add("avatar eye renderers not found, eye placement not judged");
        }
        notes.Add(idle ? "idle hands" : "live controllers, not judged");
        notes.Add(
            c.data.currentItem != null ? "holding " + c.data.currentItem.name : "nothing held"
        );
        JudgeArm(c, "L", true, left, idle, fails, notes);
        JudgeArm(c, "R", false, right, idle, fails, notes);
        yield return JudgeReaches(c, driver, fails, notes);
        yield return JudgeWristRolls(c, driver, fails, notes);
        Conclude(fails, notes);
    }

    /// <summary>Wrist rolls (degrees off the idle pose about the grip's pointing axis, same sense on both hands) the
    /// still hands hold in <see cref="JudgeWristRolls"/>: past a quarter turn either way, where the one wrist joint
    /// used to take it all.</summary>
    static readonly float[] WristRolls = { 0f, 120f, -120f };

    /// <summary>Most twist a ragdoll wrist may show: <see cref="ArmTwist.MaxWrist"/> plus the physics lag.</summary>
    const float MaxWristTwistDegrees = 105f;

    /// <summary>Most a still arm's forearm or hand body may turn in one physics tick, or its wrist twist change: a
    /// flip between the two ways round is a half turn.</summary>
    const float MaxTickTurnDegrees = 20f;

    /// <summary>Most the ragdoll hand's long axis may point off its target's: the roll is shared, the pointing kept.</summary>
    const float MaxPointDegrees = 30f;

    struct WristSample
    {
        public int Count;
        public float MaxTwist;
        public float MaxTick;
        public float Point;
        public float Twist;
        public Quaternion Fore;
        public Quaternion Hand;
    }

    /// <summary>
    /// With the scripted idle hands, both hands hold still at rest and rolled by each of <see cref="WristRolls"/>.
    /// Over <see cref="ArmSamples"/> ticks each ragdoll wrist's twist (<see cref="AvatarPoseDriver.WristTwist"/>)
    /// must stay within <see cref="MaxWristTwistDegrees"/>, neither the forearm nor the hand body nor the wrist twist
    /// may jump more than <see cref="MaxTickTurnDegrees"/> between ticks, and the hand must point within
    /// <see cref="MaxPointDegrees"/> of its target.
    /// </summary>
    IEnumerator JudgeWristRolls(
        Character c,
        AvatarPoseDriver driver,
        List<string> fails,
        List<string> notes
    )
    {
        if (VRInput.TestOverride != IdleHands)
            yield break;
        var fixedWait = new WaitForFixedUpdate();
        try
        {
            foreach (var roll in WristRolls)
            {
                float at = roll;
                VRInput.TestOverride = () =>
                {
                    ApplyIdle();
                    // A forearm roll: about the fingers (GripFrame), not the grip's +Z (the handle).
                    VRInput.Left.Rotation *= Quaternion.AngleAxis(-at, FingersLocal);
                    VRInput.Right.Rotation *= Quaternion.AngleAxis(at, FingersLocal);
                };
                yield return Wait(1f);
                var left = default(WristSample);
                var right = default(WristSample);
                for (int i = 0; i < ArmSamples; i++)
                {
                    yield return fixedWait;
                    SampleWrist(c, driver, true, ref left);
                    SampleWrist(c, driver, false, ref right);
                }
                var label = F($"wrist roll {at:+0;-0;0}");
                yield return ShotAt(F($"vr.08-arm-reach-wrist{at:+0;-0;0}"), HandsMid());
                JudgeWrist(label + " L", left, fails, notes);
                JudgeWrist(label + " R", right, fails, notes);
            }
        }
        finally
        {
            VRInput.TestOverride = IdleHands;
        }
    }

    static void SampleWrist(Character c, AvatarPoseDriver driver, bool left, ref WristSample s)
    {
        var parts = c.refs.ragdoll.partDict;
        if (
            !parts.TryGetValue(left ? BodypartType.Elbow_L : BodypartType.Elbow_R, out var elbow)
            || !parts.TryGetValue(left ? BodypartType.Hand_L : BodypartType.Hand_R, out var hand)
            || !driver.WristTwist(left, out float twist)
            || !driver.HandTarget(left, out _, out var target)
        )
            return;
        var fore = elbow.Rig.rotation;
        var h = hand.Rig.rotation;
        s.MaxTwist = Mathf.Max(s.MaxTwist, Mathf.Abs(twist));
        if (s.Count > 0)
            s.MaxTick = Mathf.Max(
                s.MaxTick,
                Mathf.Max(
                    Mathf.Abs(Mathf.DeltaAngle(s.Twist, twist)),
                    Mathf.Max(Quaternion.Angle(s.Fore, fore), Quaternion.Angle(s.Hand, h))
                )
            );
        // Scout bones point along +Y (HandRig): the hand's long axis against its target's.
        s.Point += Vector3.Angle(h * Vector3.up, target * Vector3.up);
        s.Twist = twist;
        s.Fore = fore;
        s.Hand = h;
        s.Count++;
    }

    static void JudgeWrist(string name, WristSample s, List<string> fails, List<string> notes)
    {
        if (s.Count == 0)
        {
            fails.Add(
                name + ": wrist not measured (no forearm or hand body, or no tracked target)"
            );
            return;
        }
        float point = s.Point / s.Count;
        notes.Add(
            F(
                $"{name}: wrist twist max {s.MaxTwist:0} deg, max tick turn {s.MaxTick:0} deg, hand {point:0} deg off its target's pointing"
            )
        );
        if (s.MaxTwist > MaxWristTwistDegrees)
            fails.Add(
                F($"{name}: wrist twisted {s.MaxTwist:0} deg > {MaxWristTwistDegrees:0} deg")
            );
        if (s.MaxTick > MaxTickTurnDegrees)
            fails.Add(
                F(
                    $"{name}: arm flipped, turned {s.MaxTick:0} deg in one tick > {MaxTickTurnDegrees:0} deg"
                )
            );
        if (point > MaxPointDegrees)
            fails.Add(
                F($"{name}: hand points {point:0} deg off its target > {MaxPointDegrees:0} deg")
            );
    }

    /// <summary>
    /// With the scripted idle hands (VR run), both hands hold each of <see cref="ReachPoses"/> for
    /// <see cref="ArmSamples"/> ticks: each avatar hand must reach its target (<see cref="MaxIdleTargetError"/>, nothing
    /// held), the arm must not stretch past <see cref="MaxArmStretch"/>, and it must point along the line to its
    /// controller (<see cref="MaxFollowDegrees"/>), so the avatar hand follows the tracked one. Elbow direction is judged
    /// in the idle pose only. Real controllers: nothing to script.
    /// </summary>
    static IEnumerator JudgeReaches(
        Character c,
        AvatarPoseDriver driver,
        List<string> fails,
        List<string> notes
    )
    {
        if (VRInput.TestOverride != IdleHands)
            yield break;
        try
        {
            foreach (var reach in ReachPoses)
            {
                var at = reach;
                VRInput.TestOverride = () =>
                {
                    ApplyIdle();
                    HeadFrame(out var head, out var forward, out var right);
                    var lift = Vector3.up * at.y + forward * at.z;
                    VRInput.Left.Position = head - right * at.x + lift;
                    VRInput.Right.Position = head + right * at.x + lift;
                };
                yield return Wait(1f);
                var left = default(ArmSample);
                var right = default(ArmSample);
                yield return SampleArms(
                    c,
                    driver,
                    () => left,
                    v => left = v,
                    () => right,
                    v => right = v
                );
                var label = F($"reach out {at.x:0.00} up {at.y:0.00} forward {at.z:0.00}");
                JudgeReach(c, label + " L", left, fails, notes);
                JudgeReach(c, label + " R", right, fails, notes);
                notes.Add(label + ": " + ArmContacts(c));
            }
        }
        finally
        {
            VRInput.TestOverride = IdleHands;
        }
    }

    static void JudgeReach(
        Character c,
        string name,
        ArmSample s,
        List<string> fails,
        List<string> notes
    )
    {
        if (s.Count == 0)
        {
            fails.Add(name + ": hand not tracked");
            return;
        }
        float error = s.TargetError / s.Count;
        float follow = s.Follow / s.Count;
        notes.Add(
            F(
                $"{name}: target-hand {error:0.000} m, max stretch x{s.MaxStretch:0.00}, arm {follow:0} deg off its controller"
            )
        );
        if (error > MaxIdleTargetError && c.data.currentItem == null)
            fails.Add(
                F($"{name}: hand {error:0.000} m off its target > {MaxIdleTargetError:0.00} m")
            );
        if (s.MaxStretch > MaxArmStretch)
            fails.Add(F($"{name}: arm stretched x{s.MaxStretch:0.00} > x{MaxArmStretch:0.00}"));
        if (follow > MaxFollowDegrees)
            fails.Add(
                F($"{name}: arm {follow:0} deg off its controller > {MaxFollowDegrees:0} deg")
            );
    }

    /// <summary>A ragdoll hand farther than this from the chest is being pulled off the body.</summary>
    const float MaxRagdollHandFromChest = 0.9f;

    /// <summary>A tracked hand target farther than this from the hip follows a view that left the body (the third
    /// person view sits 3 m behind it).</summary>
    const float MaxHandTargetFromHip = 1.5f;

    /// <summary>
    /// <c>vr.33-ragdoll-arms</c>: a 2 s fall sent the way <c>Character.Fall</c> does (<c>RPCA_Fall</c>, decomp
    /// Assembly-CSharp/Character.cs:1732, :1749). While the game ragdolls the body its arm joints keep chasing the
    /// animated pose, so tracked IK must let go: <see cref="AvatarPoseDriver.ArmWeight"/> reaches 0 within 1 s, the
    /// ragdoll hands stay within <see cref="MaxRagdollHandFromChest"/> of the chest, the tracked hand targets stay
    /// within <see cref="MaxHandTargetFromHip"/> of the hip even when the view detaches, and the arms are tracked
    /// again within 6 s of the fall ending. With <c>Camera.TakeoverView</c> ThirdPerson and <c>DetachOnRagdoll</c> on,
    /// the view must detach during the fall, so the held-hands path runs. Once tracked again, each arm is judged as in
    /// vr.08, and in vr.08's reaches (<see cref="JudgeReaches"/>): an arm left twisted behind the torso fails.
    /// </summary>
    IEnumerator RagdollArmsCheck()
    {
        Recover();
        var fails = new List<string>();
        var notes = new List<string>();
        var c = Character.localCharacter;
        var driver = c != null ? AvatarPoseDriver.Of(c) : null;
        var parts = c != null && c.refs.ragdoll != null ? c.refs.ragdoll.partDict : null;
        if (
            driver == null
            || parts == null
            || !parts.TryGetValue(BodypartType.Torso, out var torso)
            || !parts.TryGetValue(BodypartType.Hip, out var hip)
            || !parts.TryGetValue(BodypartType.Hand_L, out var handL)
            || !parts.TryGetValue(BodypartType.Hand_R, out var handR)
        )
        {
            Fail("no pose driver or ragdoll parts on the local character");
            yield break;
        }
        yield return WaitFor(() => driver.ArmWeight >= 1f, 3f);
        if (driver.ArmWeight < 1f)
        {
            fails.Add(F($"arm weight {driver.ArmWeight:0.00} before the fall"));
            Conclude(fails, notes);
            yield break;
        }

        c.photonView.RPC("RPCA_Fall", RpcTarget.All, 2f, 0f);
        float start = Now;
        yield return WaitFor(() => driver.ArmWeight <= 0f, 1f);
        notes.Add(F($"arms released after {Now - start:0.00} s"));
        if (driver.ArmWeight > 0f)
            fails.Add(F($"arm weight {driver.ArmWeight:0.00} 1 s into the fall"));

        float handMax = 0f;
        float targetMax = 0f;
        bool detached = false;
        bool shot = false;
        // Seen from outside, the own head must not stay ghosted (first-person hide) once the view has detached; the
        // body shows from the frame after (HideTheBody.Update runs before the rig's LateUpdate).
        var hide = c.GetComponentInChildren<HideTheBody>();
        int detachedFrames = 0;
        int hiddenWhileDetached = 0;
        while ((c.data.fallSeconds > 0f || Now - start < 0.5f) && Now - start < 4f)
        {
            detachedFrames = Rig.RigDriver.ViewDetached ? detachedFrames + 1 : 0;
            if (detachedFrames > 2 && hide != null && !AvatarPatches.BodyShowing(hide))
                hiddenWhileDetached++;
            var chest = torso.Rig.position;
            handMax = Mathf.Max(handMax, Vector3.Distance(handL.Rig.position, chest));
            handMax = Mathf.Max(handMax, Vector3.Distance(handR.Rig.position, chest));
            if (driver.HandTarget(true, out var tl))
                targetMax = Mathf.Max(targetMax, Vector3.Distance(tl, hip.Rig.position));
            if (driver.HandTarget(false, out var tr))
                targetMax = Mathf.Max(targetMax, Vector3.Distance(tr, hip.Rig.position));
            detached |= !Rig.RigDriver.ViewOnBody;
            if (!shot && Now - start > 1f)
            {
                shot = true;
                // From 1.5 m in front of the chest: frames the limp arms whether or not the view has detached.
                yield return ShotAt("vr.33-ragdoll-arms", torso.Rig.position, 1.5f);
                continue;
            }
            yield return null;
        }
        notes.Add(
            F(
                $"view {(detached ? "detached" : "on the body")}, hand to chest max {handMax:0.00} m, target to hip max {targetMax:0.00} m"
            )
        );
        if (handMax > MaxRagdollHandFromChest)
            fails.Add(
                F(
                    $"ragdoll hand {handMax:0.00} m from the chest > {MaxRagdollHandFromChest:0.00} m"
                )
            );
        if (targetMax > MaxHandTargetFromHip)
            fails.Add(
                F($"hand target {targetMax:0.00} m from the hip > {MaxHandTargetFromHip:0.00} m")
            );
        bool expectDetach =
            Rig.CameraFeature.DetachOnRagdoll.Value
            && Rig.CameraFeature.TakeoverViewEntry.Value == TakeoverView.ThirdPerson;
        if (expectDetach && !detached)
            fails.Add("view never detached during the fall with DetachOnRagdoll on");
        if (hiddenWhileDetached > 0)
            fails.Add(
                F(
                    $"own head hidden (ghosted) for {hiddenWhileDetached} frames of the detached view"
                )
            );

        float upAt = Now;
        yield return WaitFor(() => driver.ArmWeight >= 1f, 6f);
        notes.Add(F($"arms tracked again {Now - upAt:0.00} s after the fall"));
        if (hide != null && Rig.RigDriver.ViewOnBody && AvatarPatches.BodyShowing(hide))
            fails.Add("own head still shown after the view came back to the body");
        if (driver.ArmWeight < 1f)
        {
            fails.Add(F($"arm weight {driver.ArmWeight:0.00} 6 s after the fall"));
            Conclude(fails, notes);
            yield break;
        }

        // The arms are back on the tracked hands: none may stay twisted behind the torso.
        yield return Wait(1f);
        var left = default(ArmSample);
        var right = default(ArmSample);
        yield return SampleArms(c, driver, () => left, v => left = v, () => right, v => right = v);
        yield return ShotAt("vr.33-ragdoll-arms-after", HandsMid());
        bool idle = VRInput.TestOverride == IdleHands;
        notes.Add(idle ? "idle hands" : "live controllers, arms not judged");
        JudgeArm(c, "L", true, left, idle, fails, notes);
        JudgeArm(c, "R", false, right, idle, fails, notes);
        yield return JudgeReaches(c, driver, fails, notes);
        Conclude(fails, notes);
    }

    /// <summary>Looking <see cref="BeltViewPitch"/> down, the belt slots must sit this close to the view axis
    /// (degrees): well inside any headset's vertical field of view.</summary>
    const float BeltViewHalfAngle = 40f;

    /// <summary>Degrees below level a player looks to see the belt.</summary>
    const float BeltViewPitch = 60f;

    /// <summary>
    /// <c>vr.09-belt-view</c>: the hotbar ghosts on the belt can be seen from the eye. In a VR run the scripted head
    /// looks <see cref="BeltViewPitch"/> down (<c>VRInput.HeadRotation</c>, as vr.40 moves the head) and the ghosts are
    /// judged from the camera that pitch moves; with a live headset (hold mode) from a view computed along the body's
    /// heading. Each hotbar ghost must be drawn, within <see cref="BeltViewHalfAngle"/> of the view, and either drawn
    /// on top or with no collider of the local body between the eye and the ghost. Body colliders on the line are
    /// reported either way.
    /// </summary>
    IEnumerator BeltViewCheck()
    {
        var fails = new List<string>();
        var notes = new List<string>();
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        var cam = rig != null ? rig.HeadCamera : null;
        if (!SlotGhosts.Showing)
        {
            stepSkip = "body slots or slot ghosts off";
            yield break;
        }
        if (c == null || cam == null)
        {
            fails.Add("no local character or head camera");
            Conclude(fails, notes);
            yield break;
        }
        bool scripted = VRInput.TestOverride == IdleHands;
        try
        {
            if (scripted)
            {
                HeadFrame(out _, out var level, out var side);
                var pitched = Quaternion.LookRotation(
                    Quaternion.AngleAxis(BeltViewPitch, side) * level,
                    Vector3.up
                );
                VRInput.TestOverride = () =>
                {
                    ApplyIdle();
                    VRInput.HeadRotation = pitched;
                };
                yield return Wait(0.5f);
            }
            else
            {
                // Ghosts are placed in LateUpdate; let a frame pass with the current pose.
                yield return null;
            }
            var down = JudgeBelt(c, cam, scripted, fails, notes);
            JudgeOwnBody(c, cam.transform.position, fails, notes);
            yield return ShotAt("vr-09-belt-view", cam.transform.position + down);
        }
        finally
        {
            if (scripted)
                VRInput.TestOverride = IdleHands;
        }
        Conclude(fails, notes);
    }

    static readonly int VertexGhostId = Shader.PropertyToID("_VertexGhost");
    static readonly int RawVertexColorId = Shader.PropertyToID("_UseRawVertexColor");

    static readonly BodypartType[] OwnBodyLimbs =
    {
        BodypartType.Elbow_L,
        BodypartType.Elbow_R,
        BodypartType.Knee_L,
        BodypartType.Knee_R,
    };

    /// <summary>
    /// <see cref="BeltViewCheck"/>'s own-body verdict. Unless <c>Avatar.OwnBody</c> is Vanilla, the outfit at the
    /// elbows and knees must be drawn: every material of <c>HideTheBody.body</c> and its costumes
    /// (HideTheBody.cs:8, :20) is read for <c>_VertexGhost</c> and <c>_UseRawVertexColor</c> and run through
    /// <see cref="BodyGhost.Alpha"/> at each limb's flat distance from the eye, with the outfit's red vertex paint.
    /// In Vanilla the ghosted outfit is expected; the coverage is only noted.
    /// </summary>
    static void JudgeOwnBody(Character c, Vector3 eye, List<string> fails, List<string> notes)
    {
        var hide = c.refs.hideTheBody;
        var parts = c.refs.ragdoll != null ? c.refs.ragdoll.partDict : null;
        if (hide == null || hide.body == null || parts == null)
        {
            fails.Add("no HideTheBody body or ragdoll parts on the local character");
            return;
        }
        string mode = AvatarConfig.Instance.OwnBody;
        bool vanilla = string.Equals(mode, AvatarConfig.ShowVanilla, StringComparison.Ordinal);
        var renderers = new List<Renderer> { hide.body };
        renderers.AddRange(hide.costumes);
        var mats = new List<Material>();
        var worst = new List<string>();
        foreach (var part in OwnBodyLimbs)
        {
            if (!parts.TryGetValue(part, out var bp))
                continue;
            var d = bp.Rig.position - eye;
            float flat = new Vector2(d.x, d.z).magnitude;
            float least = 1f;
            string where = "";
            foreach (var r in renderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                    continue;
                r.GetSharedMaterials(mats);
                foreach (var m in mats)
                {
                    if (m == null || !m.HasProperty(VertexGhostId))
                        continue;
                    float ghost = m.GetFloat(VertexGhostId);
                    bool raw = m.HasProperty(RawVertexColorId) && m.GetFloat(RawVertexColorId) > 0f;
                    float a = BodyGhost.Alpha(ghost, flat, 1f, raw);
                    if (a < least)
                    {
                        least = a;
                        where = F($"{r.name}/{m.name} ghost {ghost:0.##}");
                    }
                }
            }
            notes.Add(
                F(
                    $"{part} {flat:0.00} m out: outfit drawn {least:0.00}{(where.Length > 0 ? " (" + where + ")" : "")}"
                )
            );
            if (!vanilla && least < 1f)
                worst.Add(F($"{part} {least:0.00} ({where})"));
        }
        notes.Add(F($"OwnBody {mode}"));
        if (worst.Count > 0)
            fails.Add("own outfit ghosted in VR at " + string.Join(", ", worst));
    }

    /// <summary>The <see cref="BeltViewCheck"/> verdicts; returns the view direction judged from.</summary>
    static Vector3 JudgeBelt(
        Character c,
        Camera cam,
        bool scripted,
        List<string> fails,
        List<string> notes
    )
    {
        var eye = cam.transform.position;
        var flat =
            c.data.lookDirection_Flat.sqrMagnitude > 1e-4f
                ? c.data.lookDirection_Flat.normalized
                : Vector3.forward;
        var down = scripted
            ? cam.transform.forward
            : Quaternion.AngleAxis(BeltViewPitch, Vector3.Cross(Vector3.up, flat)) * flat;
        if (scripted)
        {
            float pitch = 90f - Vector3.Angle(down, Vector3.down);
            notes.Add(F($"head pitched {pitch:0} deg down"));
            if (Mathf.Abs(pitch - BeltViewPitch) > 5f)
                fails.Add(
                    F($"the camera looks {pitch:0} deg down, not the scripted {BeltViewPitch:0}")
                );
        }
        else
        {
            notes.Add(F($"live headset: judged from a computed {BeltViewPitch:0} deg down view"));
        }
        bool onTop = SlotGhosts.DrawnOnTop;
        notes.Add(onTop ? "ghosts drawn on top" : "ghosts depth-tested");
        var inv = BodyInventory.Instance;
        int checkedSlots = 0;
        for (int i = 0; i < inv.Count; i++)
        {
            if (inv.Slot(i).GameSlot >= BodySlotLayout.MaxHotbar)
                continue;
            checkedSlots++;
            var ghost = SlotGhosts.Drawn(i);
            if (ghost == null)
            {
                fails.Add(F($"slot {i} not drawn"));
                continue;
            }
            var p = ghost.position;
            float angle = SelfTestChecks.AngleOff(eye.N(), down.N(), p.N());
            int blockers = BodyHits(c, eye, p);
            notes.Add(
                F(
                    $"slot {i}: {Vector3.Distance(eye, p):0.00} m, {angle:0} deg off the down view, {blockers} body colliders"
                )
            );
            if (angle > BeltViewHalfAngle)
                fails.Add(
                    F($"slot {i} {angle:0} deg off the down view > {BeltViewHalfAngle:0} deg")
                );
            if (blockers > 0 && !onTop)
                fails.Add(F($"slot {i} hidden behind {blockers} body colliders"));
        }
        if (checkedSlots == 0)
            fails.Add("no hotbar slots laid out");
        return down;
    }

    /// <summary>Colliders of <paramref name="c"/> on the line from <paramref name="from"/> to <paramref name="to"/>.</summary>
    static int BodyHits(Character c, Vector3 from, Vector3 to)
    {
        var dir = to - from;
        float length = dir.magnitude;
        if (length < 1e-3f)
            return 0;
        int n = 0;
        foreach (
            var hit in Physics.RaycastAll(
                from,
                dir / length,
                length,
                ~0,
                QueryTriggerInteraction.Ignore
            )
        )
            if (hit.collider.GetComponentInParent<Character>() == c)
                n++;
        return n;
    }

    static void SampleArm(Character c, AvatarPoseDriver driver, bool left, ref ArmSample s)
    {
        var parts = c.refs.ragdoll.partDict;
        if (
            !parts.TryGetValue(left ? BodypartType.Arm_L : BodypartType.Arm_R, out var arm)
            || !parts.TryGetValue(left ? BodypartType.Elbow_L : BodypartType.Elbow_R, out var elbow)
            || !parts.TryGetValue(left ? BodypartType.Hand_L : BodypartType.Hand_R, out var hand)
            || !driver.HandTarget(left, out var target)
        )
            return;
        var a = arm.Rig.position;
        var e = elbow.Rig.position;
        var h = hand.Rig.position;
        var rest = RestLengths(c, arm, elbow, hand);
        float fore = Vector3.Distance(h, e);
        float upper = Vector3.Distance(e, a);
        float err = Vector3.Distance(target, h);
        s.Count++;
        s.TargetError += err;
        s.MaxTargetError = Mathf.Max(s.MaxTargetError, err);
        s.Fore += fore;
        s.Upper += upper;
        s.MaxStretch = Mathf.Max(s.MaxStretch, Mathf.Max(fore / rest.x, upper / rest.y));
        s.ShoulderToTarget += Vector3.Distance(a, target);
        s.Bend += ArmMath.BendOffset(a.N(), e.N(), h.N()).U();
        var controller = VRRig.Instance != null ? VRRig.Instance.Hand(left) : null;
        if (controller != null)
            s.Follow += Vector3.Angle(h - a, controller.position - a);
    }

    static void JudgeArm(
        Character c,
        string side,
        bool left,
        ArmSample s,
        bool idle,
        List<string> fails,
        List<string> notes
    )
    {
        if (s.Count == 0)
        {
            notes.Add(side + ": hand not tracked");
            if (idle)
                fails.Add(side + " hand not tracked");
            return;
        }
        var parts = c.refs.ragdoll.partDict;
        var rest = RestLengths(
            c,
            parts[left ? BodypartType.Arm_L : BodypartType.Arm_R],
            parts[left ? BodypartType.Elbow_L : BodypartType.Elbow_R],
            parts[left ? BodypartType.Hand_L : BodypartType.Hand_R]
        );
        float n = s.Count;
        var fwd =
            c.data.lookDirection_Flat.sqrMagnitude > 1e-4f
                ? c.data.lookDirection_Flat.normalized
                : Vector3.forward;
        var rightAxis = Vector3.Cross(Vector3.up, fwd);
        // Mean offset in metres: a straight (out of reach) arm's elbow sits millimetres off the line in a direction
        // that is noise, which run 26 judged as "R elbow points up (0.46)".
        var bendOffset = s.Bend / n;
        bool bent = bendOffset.magnitude >= MinJudgedBend;
        var bend = bent ? bendOffset.normalized : Vector3.zero;
        float outward = Vector3.Dot(bend, rightAxis) * (left ? -1f : 1f);
        float up = bend.y;
        float back = -Vector3.Dot(bend, fwd);
        notes.Add(
            F($"{side}: target-hand {s.TargetError / n:0.000} m (max {s.MaxTargetError:0.000}), ")
                + F(
                    $"hand-forearm {s.Fore / n:0.000}/{rest.x:0.000} m x{s.Fore / n / rest.x:0.00}, "
                )
                + F(
                    $"forearm-upperarm {s.Upper / n:0.000}/{rest.y:0.000} m x{s.Upper / n / rest.y:0.00}, "
                )
                + F($"max stretch x{s.MaxStretch:0.00}, ")
                + F(
                    $"shoulder-target {s.ShoulderToTarget / n:0.000} m vs reach {rest.x + rest.y:0.000} m, "
                )
                + F(
                    $"elbow out {outward:0.00} up {up:0.00} back {back:0.00} (bent {bendOffset.magnitude:0.000} m"
                )
                + (bent ? ")" : ", straight: direction not judged)")
        );
        float error = s.TargetError / n;
        if (error > MaxIdleTargetError)
            notes.Add(side + " pulled by: " + HandPulls(c, left));
        if (!idle)
            return;
        if (error > MaxIdleTargetError && c.data.currentItem == null)
            fails.Add(
                F(
                    $"{side} hand {error:0.000} m off its target (controller, reach-mapped) > {MaxIdleTargetError:0.00} m "
                ) + "with idle hands and nothing held"
            );
        if (s.MaxStretch > MaxArmStretch)
            fails.Add(F($"{side} arm stretched x{s.MaxStretch:0.00} > x{MaxArmStretch:0.00}"));
        if (up > 0f)
            fails.Add(F($"{side} elbow points up ({up:0.00})"));
        if (back > MaxElbowBack)
            fails.Add(F($"{side} elbow points back ({back:0.00} > {MaxElbowBack:0.00})"));
    }

    /// <summary>Samples both arms for <see cref="ArmSamples"/> physics ticks.</summary>
    static IEnumerator SampleArms(
        Character c,
        AvatarPoseDriver driver,
        Func<ArmSample> getLeft,
        Action<ArmSample> setLeft,
        Func<ArmSample> getRight,
        Action<ArmSample> setRight
    )
    {
        var fixedWait = new WaitForFixedUpdate();
        for (int i = 0; i < ArmSamples; i++)
        {
            yield return fixedWait;
            var l = getLeft();
            var r = getRight();
            SampleArm(c, driver, true, ref l);
            SampleArm(c, driver, false, ref r);
            setLeft(l);
            setRight(r);
        }
    }

    /// <summary>What may hold a ragdoll hand off its target: welds on the hand body (an item joint, or one whose
    /// item was destroyed, which then pins the hand to the world), solid colliders it touches, and the arm's IK
    /// weights. vr9 had the left hand 0.36 m off with idle hands and nothing to say why.</summary>
    static string HandPulls(Character c, bool left)
    {
        var hand = c.refs.ragdoll.partDict[left ? BodypartType.Hand_L : BodypartType.Hand_R].Rig;
        var found = new List<string>();
        foreach (var joint in hand.GetComponents<FixedJoint>())
            found.Add(
                "weld to " + (joint.connectedBody != null ? joint.connectedBody.name : "the world")
            );
        foreach (
            var col in Physics.OverlapSphere(
                hand.position,
                0.1f,
                HelperFunctions.AllPhysicalExceptCharacter,
                QueryTriggerInteraction.Ignore
            )
        )
            found.Add($"touching {col.name} ({LayerMask.LayerToName(col.gameObject.layer)})");
        var ik = left ? c.refs.ikLeft : c.refs.ikRight;
        float ikWeight = ik != null ? ik.weight : -1f;
        float rigWeight = c.refs.ikRig != null ? c.refs.ikRig.weight : -1f;
        found.Add(F($"IK weight {ikWeight:0.00} (rig {rigWeight:0.00})"));
        return string.Join(", ", found);
    }

    /// <summary>Own-body colliders the arms overlap (the head, the torso) and whether the character layer collides
    /// with itself: what can push a reaching ragdoll arm off its target.</summary>
    static string ArmContacts(Character c)
    {
        var parts = c.refs.ragdoll.partDict;
        var found = new List<string>();
        foreach (var side in new[] { BodypartType.Arm_L, BodypartType.Arm_R })
        {
            if (!parts.TryGetValue(side, out var arm))
                continue;
            foreach (var a in arm.GetComponentsInChildren<Collider>())
            {
                foreach (var other in new[] { BodypartType.Head, BodypartType.Torso })
                {
                    if (!parts.TryGetValue(other, out var body))
                        continue;
                    foreach (var b in body.GetComponents<Collider>())
                    {
                        if (
                            a != b
                            && !Physics.GetIgnoreCollision(a, b)
                            && Physics.ComputePenetration(
                                a,
                                a.transform.position,
                                a.transform.rotation,
                                b,
                                b.transform.position,
                                b.transform.rotation,
                                out _,
                                out float depth
                            )
                        )
                            found.Add(F($"{a.name} in {other} {depth:0.00} m"));
                    }
                }
            }
        }
        int layer = parts.TryGetValue(BodypartType.Head, out var head) ? head.gameObject.layer : 0;
        return F(
            $"arm contacts {(found.Count == 0 ? "none" : string.Join(", ", found))} (body layer self-collides {!Physics.GetIgnoreLayerCollision(layer, layer)})"
        );
    }

    /// <summary>Rest lengths (x forearm, y upper arm) from the skin's bind pose, else from the joint anchors.</summary>
    static Vector2 RestLengths(Character c, Bodypart arm, Bodypart elbow, Bodypart hand) =>
        new(
            RestLength(c.refs.mainRenderer, elbow, hand),
            RestLength(c.refs.mainRenderer, arm, elbow)
        );

    static float RestLength(SkinnedMeshRenderer skin, Bodypart parent, Bodypart child)
    {
        if (skin != null && skin.sharedMesh != null)
        {
            var bones = Avatar.DrawnHands.Bones(skin);
            var poses = skin.sharedMesh.bindposes;
            int a = Array.IndexOf(bones, parent.transform);
            int b = Array.IndexOf(bones, child.transform);
            if (a >= 0 && b >= 0 && a < poses.Length && b < poses.Length)
            {
                Vector3 d = poses[a].inverse.GetColumn(3) - poses[b].inverse.GetColumn(3);
                return skin.transform.TransformVector(d).magnitude;
            }
        }
        var joint = child.GetComponent<ConfigurableJoint>();
        return joint != null
            ? parent.transform.TransformVector(joint.connectedAnchor).magnitude
            : float.NaN;
    }

    static string F(FormattableString s) => FormattableString.Invariant(s);
}
