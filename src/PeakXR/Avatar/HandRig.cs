using System.Collections.Generic;
using PeakXR.Core;
using UnityEngine;

namespace PeakXR.Avatar;

/// <summary>
/// One hand of a character rig: where the hand bone sits relative to a tracked controller, and the finger
/// chains with their bend axes. Calibrated once from the skinned mesh's bind pose (falling back to the current
/// bone transforms), so it works without hard-coded bone axes:
/// - hand forward = wrist to middle-finger knuckle, hand up = pinky to index knuckle; these map to the grip
///   pose's fingers (-Y) and thumb side (+Z) (<see cref="GripFrame"/>: OpenXR's grip runs +Z along the handle, so
///   mapping the fingers onto +Z stood every hand up along the handle with its wrist below the fist);
/// - each finger bends about (finger direction x palm normal), the axis that moves the tip toward the palm.
/// Scout rig facts (from the prefab): bones point along +Y, fingers are Thumb/Index/Middle/Pinky with 3 joints,
/// no ring finger, no Bodypart on fingers (plain skinned bones, free to rotate).
/// </summary>
internal sealed class HandRig
{
    private const int Joints = HandPose.JointsPerFinger;

    public readonly bool IsLeft;
    public readonly bool HasFingers;

    /// <summary>The index fingertip pointing straight (its rest pose), in the hand bone's frame at world size, for the
    /// poke that follows the drawn fingertip (<see cref="DrawnHands.PokeTip"/>). Null without an index tip bone.</summary>
    public readonly Vector3? IndexTip;

    private readonly Transform[] joints = new Transform[FingerCurls.Count * Joints];
    private readonly Quaternion[] initialLocal = new Quaternion[FingerCurls.Count * Joints];
    private readonly Quaternion[] proximalRest = new Quaternion[FingerCurls.Count];
    private readonly Vector3[] bendAxis = new Vector3[FingerCurls.Count];
    private readonly Quaternion handFromController;
    private bool curled;

    public HandRig(Transform hand, bool isLeft, string[][] fingerNames, SkinnedMeshRenderer skin)
    {
        IsLeft = isLeft;
        for (int f = 0; f < FingerCurls.Count; f++)
        {
            for (int j = 0; j < Joints; j++)
            {
                var t = FindDeep(hand, fingerNames[f][j]);
                joints[f * Joints + j] = t;
                if (t != null)
                    initialLocal[f * Joints + j] = t.localRotation;
            }
        }

        var space = new BoneSpace(hand, skin, joints);
        var forward = Vector3.up;
        var up = Vector3.forward;
        var middle = Joint(2, 1);
        if (middle == null)
            middle = Joint(1, 1);
        if (middle != null)
            forward = space.PointInHand(middle).normalized;
        var index = Joint(1, 1);
        var pinky = Joint(4, 1);
        if (pinky == null)
            pinky = Joint(3, 1);
        if (index != null && pinky != null)
            up = space.PointInHand(index) - space.PointInHand(pinky);
        up = Vector3.ProjectOnPlane(up, forward);
        if (up.sqrMagnitude < 1e-8f)
            up = Vector3.ProjectOnPlane(Vector3.forward, forward);

        var outer = Joint(1, 2);
        if (outer != null)
        {
            // Index_3_R_end, the tip bone the prefab hangs off the last joint.
            var end = outer.childCount > 0 ? outer.GetChild(0).localPosition : Vector3.zero;
            IndexTip =
                hand.lossyScale.x * (space.PointInHand(outer) + space.RotationInHand(outer) * end);
        }

        var look = Quaternion.LookRotation(forward, up);
        handFromController = Quaternion.Inverse(look);

        // Palm faces the controller's -X on the right hand and +X on the left.
        var right = look * Vector3.right;
        var palm = IsLeft ? right : -right;

        for (int f = 0; f < FingerCurls.Count; f++)
        {
            var p = Joint(f, 0);
            var tip = Joint(f, 2);
            if (tip == null)
                tip = Joint(f, 1);
            if (p == null || tip == null)
                continue;
            HasFingers = true;
            var dir = space.PointInHand(tip) - space.PointInHand(p);
            var axisInHand = Vector3.Cross(dir, palm).normalized;
            var proximalInHand = space.RotationInHand(p);
            proximalRest[f] = space.LocalRotation(p);
            bendAxis[f] = Quaternion.Inverse(proximalInHand) * axisInHand;
        }
    }

    private Transform Joint(int finger, int joint) => joints[finger * Joints + joint];

    /// <summary>Hand bone pose relative to the controller grip pose (position, rotation), with user tweaks. The
    /// offsets and tweaks are in the hand's frame (back = toward the forearm, up = toward the thumb), turned into the
    /// grip's by <see cref="GripFrame.FromHand"/>.</summary>
    public void ControllerToBone(out Vector3 pos, out Quaternion rot)
    {
        var cfg = AvatarConfig.Instance;
        float side = IsLeft ? -1f : 1f;
        pos = GripFromHand * new Vector3(side * cfg.WristSide, cfg.WristUp, -cfg.WristBack);
        var tweak = Quaternion.Euler(cfg.HandPitch, side * cfg.HandYaw, side * cfg.HandRoll);
        rot = GripFromHand * tweak * handFromController;
    }

    private static readonly Quaternion GripFromHand = GripFrame.FromHand.U();

    public void BoneFromController(
        Vector3 ctrlPos,
        Quaternion ctrlRot,
        out Vector3 pos,
        out Quaternion rot
    )
    {
        ControllerToBone(out var lp, out var lr);
        pos = ctrlPos + ctrlRot * lp;
        rot = ctrlRot * lr;
    }

    /// <summary>Writes absolute joint rotations: proximal joints bend from their rest pose, the outer joints from
    /// straight (bones point along +Y, so identity is straight).</summary>
    public void Curl(in FingerCurls curls)
    {
        if (!HasFingers)
            return;
        for (int f = 0; f < FingerCurls.Count; f++)
        {
            if (Joint(f, 0) == null)
                continue;
            float c = curls[f];
            for (int j = 0; j < Joints; j++)
            {
                var t = Joint(f, j);
                if (t == null)
                    continue;
                var bend = Quaternion.AngleAxis(HandPose.JointAngle(f, j, c), bendAxis[f]);
                t.localRotation = j == 0 ? proximalRest[f] * bend : bend;
            }
        }
        curled = true;
    }

    /// <summary>Puts the fingers back the way the prefab had them when the pose goes stale.</summary>
    public void Restore()
    {
        if (!curled)
            return;
        for (int i = 0; i < joints.Length; i++)
        {
            if (joints[i] != null)
                joints[i].localRotation = initialLocal[i];
        }
        curled = false;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (string.Equals(root.name, name, System.StringComparison.Ordinal))
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindDeep(root.GetChild(i), name);
            if (found != null)
                return found;
        }
        return null;
    }

    /// <summary>
    /// Bone matrices from the mesh bind pose when every needed bone is skinned (a pose independent of whatever
    /// animation is playing), otherwise from the live transforms.
    /// </summary>
    private readonly struct BoneSpace
    {
        private readonly Dictionary<Transform, Matrix4x4> bind;
        private readonly Matrix4x4 handInv;

        public BoneSpace(Transform hand, SkinnedMeshRenderer skin, Transform[] needed)
        {
            bind = null;
            if (skin != null && skin.sharedMesh != null)
            {
                var bones = skin.bones;
                var poses = skin.sharedMesh.bindposes;
                var map = new Dictionary<Transform, Matrix4x4>();
                for (int i = 0; i < bones.Length && i < poses.Length; i++)
                {
                    if (bones[i] != null)
                        map[bones[i]] = poses[i].inverse;
                }
                bool complete = map.ContainsKey(hand);
                foreach (var t in needed)
                {
                    for (var p = t; complete && p != null && p != hand; p = p.parent)
                        complete = map.ContainsKey(p);
                }
                if (complete)
                    bind = map;
            }
            handInv = Matrix(bind, hand).inverse;
        }

        private static Matrix4x4 Matrix(Dictionary<Transform, Matrix4x4> bind, Transform t) =>
            bind != null && bind.TryGetValue(t, out var m) ? m : t.localToWorldMatrix;

        public Vector3 PointInHand(Transform t) =>
            handInv.MultiplyPoint3x4(Matrix(bind, t).GetColumn(3));

        public Quaternion RotationInHand(Transform t) => (handInv * Matrix(bind, t)).rotation;

        public Quaternion LocalRotation(Transform t) =>
            t.parent == null
                ? t.localRotation
                : (Matrix(bind, t.parent).inverse * Matrix(bind, t)).rotation;
    }
}
