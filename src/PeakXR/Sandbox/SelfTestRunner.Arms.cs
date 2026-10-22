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
/// own controller); with real controllers (hold mode) it only reports. Either way it fails when the eye sits more
/// than <see cref="SelfTestChecks.MaxEyeError"/> from the avatar's eye renderers (and says so when it has none).
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

    /// <summary>Mean wrist-to-target error that fails an idle, empty hand: each hand follows only its controller.</summary>
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
        public int Count;
    }

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
        // The beach spawn ragdolls the body (Fall(7f), Character.cs:2039) and the arms only take the tracked hands
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
        // The view must ride the avatar's own eyes (scouts have big heads, so no human eye-to-shoulder rule).
        if (head != null && Rig.RigDriver.TryEyeRenderers(c, out var avatarEye))
        {
            var off = head.position - avatarEye;
            notes.Add(
                F(
                    $"eye {off.magnitude:0.00} m from the avatar's eyes (up {off.y:0.00}, level {new Vector2(off.x, off.z).magnitude:0.00})"
                )
            );
            if (off.magnitude > SelfTestChecks.MaxEyeError)
                fails.Add(
                    F(
                        $"eye {off.magnitude:0.00} m from the avatar's eyes > {SelfTestChecks.MaxEyeError:0.00} m"
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
        Conclude(fails, notes);
    }

    /// <summary>A ragdoll hand farther than this from the chest is being pulled off the body.</summary>
    const float MaxRagdollHandFromChest = 0.9f;

    /// <summary>A tracked hand target farther than this from the hip follows a view that left the body (the third
    /// person view sits 3 m behind it).</summary>
    const float MaxHandTargetFromHip = 1.5f;

    /// <summary>
    /// <c>vr.33-ragdoll-arms</c>: a 2 s fall sent the way <c>Character.Fall</c> does (<c>RPCA_Fall</c>, decomp
    /// Assembly-CSharp/Character.cs:1735, :1752). While the game ragdolls the body its arm joints keep chasing the
    /// animated pose, so tracked IK must let go: <see cref="AvatarPoseDriver.ArmWeight"/> reaches 0 within 1 s, the
    /// ragdoll hands stay within <see cref="MaxRagdollHandFromChest"/> of the chest, the tracked hand targets stay
    /// within <see cref="MaxHandTargetFromHip"/> of the hip even when the view detaches, and the arms are tracked
    /// again within 6 s of the fall ending. With <c>Camera.TakeoverView</c> ThirdPerson and <c>DetachOnRagdoll</c> on,
    /// the view must detach during the fall, so the held-hands path runs. Once tracked again, each arm is judged as in
    /// vr.08: an arm left twisted behind the torso fails.
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
        Conclude(fails, notes);
    }

    /// <summary>Looking 60 degrees down, the belt slots must sit this close to the view axis (degrees): well inside
    /// any headset's vertical field of view.</summary>
    const float BeltViewHalfAngle = 40f;

    /// <summary>
    /// <c>vr.09-belt-view</c>: the hotbar ghosts on the belt can be seen from the eye. The headset's own pitch cannot
    /// be scripted, so it is judged from poses: each hotbar ghost must be drawn, within
    /// <see cref="BeltViewHalfAngle"/> of a view pitched 60 degrees down along the body's heading, and either drawn
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
        // Ghosts are placed in LateUpdate; let a frame pass with the current pose.
        yield return null;

        var eye = cam.transform.position;
        var flat =
            c.data.lookDirection_Flat.sqrMagnitude > 1e-4f
                ? c.data.lookDirection_Flat.normalized
                : Vector3.forward;
        var down = Quaternion.AngleAxis(60f, Vector3.Cross(Vector3.up, flat)) * flat;
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
        yield return ShotAt("vr-09-belt-view", eye + down);
        Conclude(fails, notes);
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
                F($"{side} hand {error:0.000} m off its controller > {MaxIdleTargetError:0.00} m ")
                    + "with idle hands and nothing held"
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
            var bones = skin.bones;
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
