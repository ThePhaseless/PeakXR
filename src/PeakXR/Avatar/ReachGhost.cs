using PeakXR.Core;
using PeakXR.UI.Common;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Avatar;

/// <summary>
/// A faint ghost hand at each controller the local character's arm cannot reach (a deep stoop, a reach past the
/// arm): the held item stays on the character's hand, and the ghost shows where the real hand is. It fades in once
/// the arm falls 5 cm short for 0.15 s and is full by 15 cm (<see cref="GhostFade"/>; geometry, so a fast swing's lag
/// never shows it), and its fingers follow the hand's curls. Local
/// only: a renderer on the rig side, never sent. Built once; LateUpdate allocates nothing and poses only what shows.
/// </summary>
internal sealed class ReachGhost : MonoBehaviour
{
    /// <summary>GhostDemo's ghost tint (UI/Guide/GhostDemo.cs), so every ghost reads the same.</summary>
    private static readonly Color GhostColor = new(0.85f, 0.92f, 1f, 0.32f);

    private const int RenderQueue = 3100;

    /// <summary>Knuckle line ahead of the grip pose's origin and the finger spread across it, metres.</summary>
    private const float Knuckles = 0.05f;
    private const float Spread = 0.012f;

    private sealed class Hand
    {
        public readonly GhostFade Fade = new();
        public Transform Root;
        public Material Material;
        public readonly Transform[] Fingers = new Transform[FingerCurls.Count];
        public float ShownAlpha = -1f;
    }

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
        bool was = h.Fade.Visible;
        float alpha = on ? h.Fade.Update(gap, dt) : h.Fade.Update(0f, dt);
        if (on && h.Fade.Visible && !was)
        {
            // Rare edge: why it showed.
            Plugin.Log.LogInfo(
                System.FormattableString.Invariant(
                    $"[avatar.local] ghost {(isLeft ? "left" : "right")} hand: short {gap:0.00} m, shoulder {shoulder}, controller bone {bone}, arm {arm:0.00} m, body to head {driver.BodyToHead()}"
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
        for (int i = 0; i < FingerCurls.Count; i++)
        {
            // Thumb folds across the palm about the hand's long axis; fingers fold toward the palm.
            h.Fingers[i].localRotation =
                i == 0
                    ? Quaternion.Euler(0f, (isLeft ? 1f : -1f) * (30f + 50f * curls[i]), 0f)
                    : Quaternion.Euler(90f * curls[i], 0f, 0f);
        }
    }

    /// <summary>A mitten of ellipsoids around the grip pose (OpenXR grip: +Z where the fist points, -Y toward the
    /// palm's fold): a palm, four fingers and a thumb on pivots at their knuckles.</summary>
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
        Part(
            "Palm",
            root,
            mesh,
            h.Material,
            new Vector3(0f, 0f, 0.01f),
            new Vector3(0.075f, 0.03f, 0.09f)
        );
        float side = isLeft ? 1f : -1f;
        for (int i = 1; i < FingerCurls.Count; i++)
        {
            var pivot = new GameObject("Finger" + i).transform;
            pivot.SetParent(root, false);
            pivot.localPosition = new Vector3(side * Spread * (i - 2.5f) * 2f, 0f, Knuckles);
            Part(
                "Seg",
                pivot,
                mesh,
                h.Material,
                new Vector3(0f, 0f, 0.035f),
                new Vector3(0.017f, 0.017f, 0.075f)
            );
            h.Fingers[i] = pivot;
        }
        var thumb = new GameObject("Thumb").transform;
        thumb.SetParent(root, false);
        thumb.localPosition = new Vector3(side * 0.035f, -0.005f, 0.0f);
        Part(
            "Seg",
            thumb,
            mesh,
            h.Material,
            new Vector3(0f, 0f, 0.03f),
            new Vector3(0.02f, 0.02f, 0.06f)
        );
        h.Fingers[0] = thumb;
        root.gameObject.SetActive(false);
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
