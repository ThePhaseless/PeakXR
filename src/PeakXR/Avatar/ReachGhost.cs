using PeakXR.Core;
using PeakXR.UI.Common;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Avatar;

/// <summary>
/// A faint ghost hand at each controller the local character's arm cannot reach (a deep stoop, a reach past the
/// arm): the held item stays on the character's hand, and the ghost shows where the real hand is. It fades in once
/// the hand is 5 cm past its reach for 0.15 s (the arm, or the player's own reach while the reach map carries an empty
/// hand, <see cref="ArmReach.GhostReach"/>; an empty hand's drawn arm stretches to its controller first, so its ghost
/// waits for <see cref="ArmReach.DrawnReach"/>) and is full by 15 cm (<see cref="GhostFade"/>; geometry, so a fast swing's lag
/// never shows it), and its fingers follow the hand's curls. Local
/// only: a renderer on the rig side, never sent. Built once; LateUpdate allocates nothing and poses only what shows.
/// </summary>
internal sealed class ReachGhost : MonoBehaviour
{
    /// <summary>GhostDemo's ghost tint (UI/Guide/GhostDemo.cs), so every ghost reads the same.</summary>
    private static readonly Color GhostColor = new(0.85f, 0.92f, 1f, 0.32f);

    private const int RenderQueue = 3100;

    /// <summary>Knuckle line ahead of the grip pose's origin, the palm's centre behind it toward the back of the hand,
    /// and the finger spacing across the knuckles, metres (hand frame: +Z the fingers, +Y the thumb side).</summary>
    private const float Knuckles = 0.035f;
    private const float PalmBack = 0.025f;
    private const float Spread = 0.019f;
    private const float Proximal = 0.042f;
    private const float Distal = 0.034f;

    /// <summary>The palm's width across the knuckles (its ellipsoid's Y), metres.</summary>
    private const float GhostPalm = 0.085f;

    /// <summary>Thumb's rest angle up toward the thumb side, and how far a full curl folds each finger joint.</summary>
    private const float ThumbRaise = 40f;
    private const float FingerFold = 80f;
    private const float ThumbFold = 55f;

    private sealed class Hand
    {
        public readonly GhostFade Fade = new();
        public Transform Root;
        public Material Material;
        public readonly Transform[] Fingers = new Transform[FingerCurls.Count];
        public readonly Transform[] Tips = new Transform[FingerCurls.Count];
        public float ShownAlpha = -1f;

        /// <summary>Gap of the last log line, metres; negative before the first.</summary>
        public float LoggedGap = -1f;
    }

    // A logged ghost logs again only once its gap has moved this far (a hand parked at the eye flickered the fade at
    // its threshold and logged the same 0.05 m 26 times).
    private const float LogStep = 0.05f;

    private static ReachGhost instance;
    private readonly Hand left = new();
    private readonly Hand right = new();
    private bool built;

    /// <summary>Ghost strength (0..1) of a hand; read by the self-test.</summary>
    internal static float Alpha(bool isLeft) =>
        instance == null ? 0f : (isLeft ? instance.left : instance.right).Fade.Alpha;

    internal static void Create()
    {
        if (instance != null)
            return;
        var host = new GameObject("PeakXR.ReachGhost") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(host);
        instance = host.AddComponent<ReachGhost>();
    }

    private void LateUpdate()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        var driver =
            VRSession.Active && AvatarConfig.Instance.ReachGhostHand && c != null
                ? AvatarPoseDriver.Of(c)
                : null;
        float dt = Time.deltaTime;
        Tick(left, true, driver, rig, dt);
        Tick(right, false, driver, rig, dt);
    }

    private void Tick(Hand h, bool isLeft, AvatarPoseDriver driver, VRRig rig, float dt)
    {
        float gap = 0f;
        var controller = rig != null ? rig.Hand(isLeft) : null;
        Vector3 shoulder = default,
            bone = default;
        float arm = 0f;
        bool on =
            driver != null
            && controller != null
            && VRInput.Hand(isLeft).Tracked
            && driver.ReachGap(isLeft, out gap, out shoulder, out bone, out arm);
        float alpha = on ? h.Fade.Update(gap, dt) : h.Fade.Update(0f, dt);
        if (on && h.Fade.Visible && (h.LoggedGap < 0f || Mathf.Abs(gap - h.LoggedGap) >= LogStep))
        {
            h.LoggedGap = gap;
            // Rare edge: why it showed.
            Plugin.Log.LogInfo(
                System.FormattableString.Invariant(
                    $"[avatar.local] ghost {(isLeft ? "left" : "right")} hand: short {gap:0.00} m, shoulder {shoulder}, controller bone {bone}, reach {arm:0.00} m, body to head {driver.BodyToHead()}"
                )
            );
        }
        if (!h.Fade.Visible)
        {
            if (h.Root != null && h.Root.gameObject.activeSelf)
                h.Root.gameObject.SetActive(false);
            return;
        }
        if (!built)
            Build();
        if (!h.Root.gameObject.activeSelf)
            h.Root.gameObject.SetActive(true);
        if (controller != null)
            h.Root.SetPositionAndRotation(controller.position, controller.rotation);
        // As big as the avatar's drawn hand (Avatar.HandScale): the palm below is a real hand's.
        var size = Vector3.one * HandScale.Ghost(DrawnHands.Factor, GhostPalm);
        if (h.Root.localScale != size)
            h.Root.localScale = size;
        if (!Mathf.Approximately(alpha, h.ShownAlpha))
        {
            h.ShownAlpha = alpha;
            var col = GhostColor;
            col.a *= alpha;
            h.Material.color = col;
        }
        var curls =
            driver != null
                ? (isLeft ? driver.Pose.LeftFingers : driver.Pose.RightFingers)
                : default;
        // Fingers fold about the knuckle line (hand +Y) toward the palm: -X on the right hand, +X on the left.
        float palm = isLeft ? 1f : -1f;
        for (int i = 0; i < FingerCurls.Count; i++)
        {
            float c = curls[i];
            var fold =
                i == 0
                    ? Quaternion.Euler(0f, palm * ThumbFold * c, 0f)
                    : Quaternion.Euler(0f, palm * FingerFold * c, 0f);
            h.Fingers[i].localRotation = i == 0 ? ThumbRest * fold : fold;
            h.Tips[i].localRotation = fold;
        }
    }

    private static readonly Quaternion ThumbRest = Quaternion.Euler(-ThumbRaise, 0f, 0f);

    /// <summary>A hand of ellipsoids around the grip pose, built in a hand frame (+Z the fingers, +Y the thumb side,
    /// X the palm axis) that <see cref="GripFrame.FromHand"/> turns into the grip's, the same frame the avatar's hand
    /// bone takes (HandRig): a palm, four two-segment fingers and a thumb on pivots at their knuckles, wrapped round
    /// the controller's handle like the real hand.</summary>
    private void Build()
    {
        built = true;
        var mesh = UIMaterials.SphereMesh();
        BuildHand(left, true, mesh);
        BuildHand(right, false, mesh);
    }

    private void BuildHand(Hand h, bool isLeft, Mesh mesh)
    {
        h.Material = UIMaterials.Create(GhostColor, drawOnTop: false, renderQueue: RenderQueue);
        var root = new GameObject(isLeft ? "GhostHandL" : "GhostHandR").transform;
        root.SetParent(transform, false);
        h.Root = root;
        var hand = new GameObject("Hand").transform;
        hand.SetParent(root, false);
        hand.localRotation = GripFrame.FromHand.U();
        // The grip's origin is the middle of the fist, in front of the palm: the palm sits toward the back of the hand.
        float back = isLeft ? -PalmBack : PalmBack;
        Part(
            "Palm",
            hand,
            mesh,
            h.Material,
            new Vector3(back, 0f, -0.01f),
            new Vector3(0.032f, GhostPalm, 0.095f)
        );
        for (int i = 1; i < FingerCurls.Count; i++)
        {
            // Index nearest the thumb (+Y), little finger farthest.
            var pivot = Pivot(hand, new Vector3(back, Spread * (2.5f - i), Knuckles));
            h.Fingers[i] = pivot;
            h.Tips[i] = Segment(pivot, mesh, h.Material, Proximal, 0.02f);
        }
        var thumb = Pivot(hand, new Vector3(back * 0.5f, 0.04f, -0.035f));
        h.Fingers[0] = thumb;
        h.Tips[0] = Segment(thumb, mesh, h.Material, 0.045f, 0.024f);
        root.gameObject.SetActive(false);
    }

    private static Transform Pivot(Transform parent, Vector3 at)
    {
        var pivot = new GameObject("Knuckle").transform;
        pivot.SetParent(parent, false);
        pivot.localPosition = at;
        return pivot;
    }

    /// <summary>A finger's two segments under <paramref name="knuckle"/>; returns the outer joint's pivot.</summary>
    private static Transform Segment(
        Transform knuckle,
        Mesh mesh,
        Material material,
        float length,
        float thickness
    )
    {
        Part(
            "Seg",
            knuckle,
            mesh,
            material,
            new Vector3(0f, 0f, length * 0.5f),
            new Vector3(thickness, thickness, length * 1.1f)
        );
        var tip = Pivot(knuckle, new Vector3(0f, 0f, length));
        Part(
            "Tip",
            tip,
            mesh,
            material,
            new Vector3(0f, 0f, Distal * 0.5f),
            new Vector3(thickness * 0.9f, thickness * 0.9f, Distal * 1.1f)
        );
        return tip;
    }

    private static void Part(
        string name,
        Transform parent,
        Mesh mesh,
        Material material,
        Vector3 position,
        Vector3 scale
    )
    {
        var go = UIMaterials.MeshObject(name, mesh, material, parent);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
    }
}
