using System;
using System.Collections;
using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Inventory;
using PeakXR.VR;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

namespace PeakXR.Sandbox;

/// <summary>
/// <c>vr.08-arm-reach</c>: whether the tracked hands drive the whole arm. Samples the ragdoll arm chain
/// (<c>Arm</c>, <c>Elbow</c>, <c>Hand</c> bodyparts, physics space) against the controller-derived wrist target. The
/// chain's joints are locked (ConfigurableJoint x/y/zMotion 0 in the Character prefab), so a stretch ratio well
/// above 1 means a force is tearing the hand off the forearm; a ratio near 1 with the hand on target means the IK
/// pose itself is what the player sees. With the idle hands in front of the face it fails above 1.15 stretch or
/// when an elbow points up; with real controllers (hold mode) it only reports. Either way it fails when the eye
/// sits more than <see cref="SelfTestChecks.MaxEyeAboveShoulders"/> above the shoulders.
/// </summary>
internal sealed partial class SelfTestRunner
{
    const float MaxArmStretch = 1.15f;
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
        // Let the arms settle on the current hands before measuring.
        yield return Wait(1f);

        var fixedWait = new WaitForFixedUpdate();
        var left = default(ArmSample);
        var right = default(ArmSample);
        for (int i = 0; i < ArmSamples; i++)
        {
            yield return fixedWait;
            SampleArm(c, driver, true, ref left);
            SampleArm(c, driver, false, ref right);
        }
        yield return Shot("vr.08-arm-reach");

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
            // Above that the view rides the top of the head: looking down shows the top of your own body.
            if (drop > SelfTestChecks.MaxEyeAboveShoulders)
                fails.Add(
                    F(
                        $"eye {drop:0.00} m above the shoulders > {SelfTestChecks.MaxEyeAboveShoulders:0.00} m"
                    )
                );
        }
        notes.Add(idle ? "idle hands" : "live controllers, not judged");
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
        s.Bend += ArmMath.BendDirection(a.N(), e.N(), h.N()).U();
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
        var bend = s.Bend / n;
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
                + F($"elbow out {outward:0.00} up {up:0.00} back {back:0.00}")
        );
        if (!idle)
            return;
        if (s.MaxStretch > MaxArmStretch)
            fails.Add(F($"{side} arm stretched x{s.MaxStretch:0.00} > x{MaxArmStretch:0.00}"));
        if (up > 0f)
            fails.Add(F($"{side} elbow points up ({up:0.00})"));
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
