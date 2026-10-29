using System.Collections.Generic;
using PeakXR.Core;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Avatar;

/// <summary>
/// Draws the local VR player's hands smaller (<c>Avatar.HandScale</c>) about the controller grip, the forearms
/// thinned to match and the upper arms halfway, so the scout's chunky hands stop making the world read as gigantic.
/// Only the drawing changes: the character's skinned meshes are pointed at proxy copies of the arm bones, posed here
/// after everything else this frame, while the real bones (ragdoll rigidbodies on each Bodypart, Bodypart.cs:72-83, interpolated,
/// RigCreator.cs:100; colliders, IK, animation, the item
/// weld, net.pose) are never touched. Peers render their own copy of the character at the game's size.
/// The grip (the controller's point in the hand, <see cref="HandRig.ControllerToBone"/>) stays put, so a held item
/// stays in the palm; the forearm stretches to the drawn wrist so the arm never parts.
/// An empty hand is drawn where its controller is: the scout's shoulders sit 0.47 m under and 0.2 m behind its eyes
/// with a 0.6 m arm, so the ragdoll hand stops short of a controller held in front of the face (the reach map holds
/// it at the bent reach, a held or climbing arm locks straight). The whole drawn arm is scaled about its shoulder
/// (<see cref="ArmReach.DrawnStretch"/>, up to 1.5x) until its hand meets the controller; past that the ghost hand
/// shows the rest (<see cref="ReachGhost"/>). A hand holding an item or gripping a hold (a wall, rope, vine, handle or
/// piton) is drawn on its ragdoll hand.
/// </summary>
[DefaultExecutionOrder(32000)]
internal sealed class DrawnHands : MonoBehaviour
{
    private sealed class Side
    {
        public readonly bool Left;
        public HandRig Rig;
        public Transform Upper,
            Fore,
            Hand;
        public Transform UpperProxy,
            ForeProxy,
            HandProxy;

        /// <summary>The skinned bones under the hand (fingers, thumb) and their proxies.</summary>
        public Transform[] Parts = System.Array.Empty<Transform>();
        public Transform[] PartProxies = System.Array.Empty<Transform>();

        public Side(bool left) => Left = left;
    }

    private static DrawnHands local;

    /// <summary>Each hand's finger stop for its drawn size this frame (1 = none), set by <see cref="LocalAvatar"/>.</summary>
    internal static float WrapLeft = 1f;
    internal static float WrapRight = 1f;

    private readonly Side left = new(true);
    private readonly Side right = new(false);
    private readonly List<Transform> proxies = new();
    private readonly Dictionary<Transform, Transform> proxyOf = new();
    private SkinnedMeshRenderer[] skins = System.Array.Empty<SkinnedMeshRenderer>();
    private Transform[][] original = System.Array.Empty<Transform[]>();
    private Character character;
    private AvatarPoseDriver driver;
    private bool built;
    private bool swapped;

    /// <summary>How big the local VR player's hands are drawn now (1 = the game's size).</summary>
    public static float Factor =>
        VRSession.Active && local != null && local.character == Character.localCharacter
            ? HandScale.Clamp(AvatarConfig.Instance.HandScale)
            : 1f;

    /// <summary>The drawn hands are on: the meshes draw the proxies.</summary>
    public static bool Drawn => local != null && local.swapped;

    /// <summary>The drawn proxy of a real bone, or the bone itself when it has none or the proxies are off (outside
    /// VR): they then stop being posed and keep their last pose (run 23's vr.08b read stale 0.50 proxies at 1.00 and
    /// got a span ratio of 1.00).</summary>
    public static Transform DrawnBone(Transform bone) =>
        local != null && local.swapped && bone != null && local.proxyOf.TryGetValue(bone, out var p)
            ? p
            : bone;

    /// <summary>The bones <paramref name="skin"/> had before the proxies (its own when unchanged).</summary>
    public static Transform[] Bones(SkinnedMeshRenderer skin)
    {
        if (local != null && local.swapped)
        {
            int i = System.Array.IndexOf(local.skins, skin);
            if (i >= 0)
                return local.original[i];
        }
        return skin.bones;
    }

    /// <summary>
    /// Where the <paramref name="isLeft"/> hand pokes UI (keyboard keys, passport cells): the drawn index fingertip,
    /// pointing straight, of a hand at the controller (the ragdoll arm lags it). 4 cm ahead of the aim pose without
    /// a calibrated avatar hand (the title menu).
    /// </summary>
    public static Vector3 PokeTip(VRRig rig, bool isLeft)
    {
        var aim = rig.Aim(isLeft);
        var grip = rig.Hand(isLeft);
        var pose = local != null ? AvatarPoseDriver.Of(local.character) : null;
        var hand = pose != null ? pose.HandRigOf(isLeft) : null;
        if (
            hand?.IndexTip is not { } tip
            || grip == null
            || local.character != Character.localCharacter
        )
            return aim.position + aim.forward * 0.04f;
        hand.BoneFromController(grip.position, grip.rotation, out var bone, out var rot);
        return grip.position + (bone + rot * tip - grip.position) * Factor;
    }

    /// <summary>The local hands' curls as drawn: stopped where the smaller fingers meet the held item.</summary>
    public static FingerCurls Curls(Character owner, bool isLeft, FingerCurls curls)
    {
        float wrap = isLeft ? WrapLeft : WrapRight;
        return wrap < 1f && local != null && ReferenceEquals(owner, local.character)
            ? HandPose.Wrap(curls, wrap)
            : curls;
    }

    private void Awake()
    {
        character = GetComponent<Character>();
        local = this;
    }

    private void LateUpdate()
    {
        // On in VR at every hand size: the stretch to the controller needs the proxies at the game's size too.
        if (!VRSession.Active || character != Character.localCharacter || !Build())
        {
            Swap(false);
            return;
        }
        Swap(true);
        float s = Factor;
        Pose(left, s, Stretch(left));
        Pose(right, s, Stretch(right));
    }

    /// <summary>Whether <paramref name="owner"/>'s <paramref name="isLeft"/> arm is drawn stretched to its controller
    /// this frame (the local VR player's empty hand): the ghost hand then waits for the drawn reach.</summary>
    public static bool Stretches(Character owner, bool isLeft) =>
        local != null
        && local.swapped
        && ReferenceEquals(owner, local.character)
        && local.driver != null
        && AvatarConfig.Instance.ArmIK
        && !local.Holds(local.driver.Pose, isLeft);

    /// <summary>The hand stays drawn on its ragdoll hand: it holds the item, or its grip holds the world (a wall
    /// climb, a rope, vine, handle or piton, a carried friend: <see cref="Interact.PhysicalInteraction.GripHolds"/>).
    /// A climbing controller runs up to 0.5 m ahead of the body while the game pays off the pull (ClimbDebt), and a
    /// stretched hand would slide off the hold.</summary>
    private bool Holds(AvatarPose pose, bool isLeft) =>
        character.data.currentItem != null && (pose.TwoHandHold || pose.ItemInLeftHand == isLeft)
        || Interact.PhysicalInteraction.GripHolds(isLeft);

    /// <summary>The drawn arm's scale about its shoulder that puts an empty hand on its controller (1: as the
    /// ragdoll has it).</summary>
    private float Stretch(Side side)
    {
        if (
            driver == null
            || !driver.HasFreshPose
            || driver.ArmsSuspended
            || !AvatarConfig.Instance.ArmIK
        )
            return 1f;
        var pose = driver.Pose;
        if (!(side.Left ? pose.HasLeftHand : pose.HasRightHand) || Holds(pose, side.Left))
            return 1f;
        side.Rig.BoneFromController(
            side.Left ? pose.LeftHandPosition : pose.RightHandPosition,
            side.Left ? pose.LeftHandRotation : pose.RightHandRotation,
            out var bone,
            out _
        );
        var shoulder = side.Upper.position;
        var elbow = side.Fore.position;
        var hand = side.Hand.position;
        return ArmReach.DrawnStretch(
            Vector3.Distance(shoulder, hand),
            Vector3.Distance(shoulder, bone),
            Vector3.Distance(shoulder, elbow) + Vector3.Distance(elbow, hand),
            driver.ArmWeight
        );
    }

    /// <summary>Poses one arm's proxies: the hand <paramref name="s"/> times smaller about its grip, the arm
    /// <paramref name="k"/> times longer about its shoulder.</summary>
    private static void Pose(Side side, float s, float k)
    {
        side.Rig.ControllerToBone(out var lp, out var lr);
        var hand = side.Hand;
        var rot = hand.rotation;
        var shoulder = side.Upper.position;
        // The stretch moves the elbow and hand out along their own lines from the shoulder: the arm keeps its shape.
        var shift = (hand.position - shoulder) * (k - 1f);
        var grip = hand.position + shift - rot * Quaternion.Inverse(lr) * lp;
        Place(side.HandProxy, hand, grip, shift, s);
        for (int i = 0; i < side.Parts.Length; i++)
            Place(side.PartProxies[i], side.Parts[i], grip, shift, s);
        // The forearm runs from its (stretched) elbow to the drawn wrist, as thin as the hand is scaled.
        var elbow = shoulder + (side.Fore.position - shoulder) * k;
        var from = hand.position - side.Fore.position;
        var to = side.HandProxy.position - elbow;
        float length = from.sqrMagnitude > 1e-8f ? to.magnitude / from.magnitude : 1f;
        side.ForeProxy.SetPositionAndRotation(
            elbow,
            Quaternion.FromToRotation(from, to) * side.Fore.rotation
        );
        // Scout arm bones run along their +Y (Hand_R sits at (0, 0.637, 0) under Elbow_R in the prefab).
        side.ForeProxy.localScale = Vector3.Scale(side.Fore.lossyScale, new Vector3(s, length, s));
        float u = HandScale.UpperArm(s);
        side.UpperProxy.SetPositionAndRotation(shoulder, side.Upper.rotation);
        side.UpperProxy.localScale = Vector3.Scale(side.Upper.lossyScale, new Vector3(u, k, u));
    }

    private static void Place(Transform proxy, Transform real, Vector3 grip, Vector3 shift, float s)
    {
        proxy.SetPositionAndRotation(grip + (real.position + shift - grip) * s, real.rotation);
        proxy.localScale = real.lossyScale * s;
    }

    /// <summary>Proxies for both arms once the driver has calibrated the hands. False until it can.</summary>
    private bool Build()
    {
        if (built)
            return true;
        if (character == null || character.refs?.ragdoll == null)
            return false;
        driver = AvatarPoseDriver.Of(character);
        if (driver == null || driver.HandRigOf(true) == null || driver.HandRigOf(false) == null)
            return false;
        skins = character.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        original = new Transform[skins.Length][];
        for (int i = 0; i < skins.Length; i++)
            original[i] = skins[i].bones;
        var parts = character.refs.ragdoll.partDict;
        if (
            !Bind(left, parts, BodypartType.Arm_L, BodypartType.Elbow_L, BodypartType.Hand_L)
            || !Bind(right, parts, BodypartType.Arm_R, BodypartType.Elbow_R, BodypartType.Hand_R)
        )
        {
            // Not this rig: stay at the game's size for good.
            Plugin.Log.LogWarning("[avatar.local] no arm bodyparts, hands drawn at game size");
            enabled = false;
            return false;
        }
        built = true;
        Plugin.Log.LogInfo(
            $"[avatar.local] drawn hands: {proxies.Count} proxy bones over {skins.Length} skinned meshes"
        );
        return true;
    }

    private bool Bind(
        Side side,
        Dictionary<BodypartType, Bodypart> parts,
        BodypartType upper,
        BodypartType fore,
        BodypartType hand
    )
    {
        if (
            !parts.TryGetValue(upper, out var u)
            || !parts.TryGetValue(fore, out var f)
            || !parts.TryGetValue(hand, out var h)
        )
            return false;
        side.Rig = driver.HandRigOf(side.Left);
        side.Upper = u.transform;
        side.Fore = f.transform;
        side.Hand = h.transform;
        side.UpperProxy = Proxy(side.Upper);
        side.ForeProxy = Proxy(side.Fore);
        side.HandProxy = Proxy(side.Hand);
        var under = new List<Transform>();
        foreach (var bones in original)
        {
            foreach (var b in bones)
            {
                if (b != null && b != side.Hand && b.IsChildOf(side.Hand) && !under.Contains(b))
                    under.Add(b);
            }
        }
        side.Parts = under.ToArray();
        side.PartProxies = new Transform[side.Parts.Length];
        for (int i = 0; i < side.Parts.Length; i++)
            side.PartProxies[i] = Proxy(side.Parts[i]);
        return true;
    }

    private Transform Proxy(Transform real)
    {
        var p = new GameObject("PeakXR.Drawn." + real.name)
        {
            hideFlags = HideFlags.HideAndDontSave,
        }.transform;
        proxies.Add(p);
        proxyOf[real] = p;
        return p;
    }

    /// <summary>Points the skinned meshes at the proxies (<paramref name="on"/>) or back at the real bones.</summary>
    private void Swap(bool on)
    {
        if (on == swapped)
            return;
        swapped = on;
        for (int i = 0; i < skins.Length; i++)
        {
            if (skins[i] == null)
                continue;
            if (!on)
            {
                skins[i].bones = original[i];
                continue;
            }
            var bones = (Transform[])original[i].Clone();
            for (int b = 0; b < bones.Length; b++)
            {
                if (bones[b] != null && proxyOf.TryGetValue(bones[b], out var p))
                    bones[b] = p;
            }
            skins[i].bones = bones;
        }
    }

    private void OnDestroy()
    {
        Swap(false);
        foreach (var p in proxies)
        {
            if (p != null)
                Destroy(p.gameObject);
        }
        if (local == this)
            local = null;
    }
}
