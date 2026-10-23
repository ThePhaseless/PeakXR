using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Interact;
using PeakXR.UI.Common;
using PeakXR.UI.Spatial;
using PeakXR.UI.World;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.UI.HandWidgets;

/// <summary>
/// Moves the progress ring (A/UI_UseItemProgress.cs:5) and the friend feed container
/// (A/GUIManager.cs:56 <c>friendProgressTF</c>, filled at :458) into hand-posed boxes on the converted
/// HUD canvas. The widgets stay on that canvas, so the game keeps driving them and they die with the
/// scene; everything moved is put back when a setting returns to Hud or VR UI stops. A hand at the face
/// parks them low in view (<see cref="HandWidgetPlace"/>) instead of in front of the eyes.
/// </summary>
[DefaultExecutionOrder(31100)]
internal sealed class HandWidgetsRuntime : MonoBehaviour
{
    private const float StepSeconds = 0.02f;
    private const float CompleteSeconds = 0.08f;

    /// <summary>A <see cref="SpatialBox"/>, so the ring draws over the world like the labels: a held interaction's
    /// ring under its target's label was hidden behind the food held over a fire and the flames.</summary>
    private readonly SpatialBox ring = new("PeakXR_HandRing");
    private readonly MovedWidget feed = new();
    private readonly ProgressPulseTracker pulses = new();
    private GUIManager gui;
    private UI_UseItemProgress ringWidget;
    private ProgressSource ringSource;
    private bool ringLeft;

    public static void Create()
    {
        var go = new GameObject("PeakXR_HandWidgets");
        DontDestroyOnLoad(go);
        go.AddComponent<HandWidgetsRuntime>();
    }

    private void OnEnable() => Application.onBeforeRender += OnBeforeRender;

    private void OnDisable() => Application.onBeforeRender -= OnBeforeRender;

    private void LateUpdate()
    {
        var cfg = HandWidgetsFeature.Settings;
        var rig = VRRig.Instance;
        var current = GUIManager.instance;
        if (current != gui)
        {
            // The old HUD died with its scene, and the boxes with it.
            ring.Forget();
            feed.Forget();
            gui = current;
            ringWidget =
                gui != null && gui.hudCanvas != null
                    ? gui.hudCanvas.GetComponentInChildren<UI_UseItemProgress>(true)
                    : null;
        }

        var ready = UIWorldRuntime.Ready && rig != null && gui != null && gui.hudCanvas != null;
        var ringOnHand = ready && ringWidget != null && cfg.Ring.Value == RingPlacement.Hand;
        var feedOnHand =
            ready && gui.friendProgressTF != null && cfg.Feed.Value != FeedPlacement.Hud;
        if (!ringOnHand)
            ring.Restore();
        else if (!ring.Holding)
        {
            ring.Move((RectTransform)gui.hudCanvas.transform, ringWidget.transform, Vector2.zero);
            ring.SetVisible(true);
        }
        Sync(feed, feedOnHand, feedOnHand ? gui.friendProgressTF : null, "PeakXR_HandFeed");

        var progress = 0f;
        var source = ready ? ReadProgress(out progress) : ProgressSource.None;
        if (source != ringSource && source != ProgressSource.None)
        {
            var c = Character.localCharacter;
            var hasItem = c != null && c.data.currentItem != null;
            ringLeft = ProgressHand.PickLeft(
                source,
                hasItem,
                LocalHold.ItemInLeftHand,
                PhysicalInteraction.AimHand
            );
        }
        ringSource = source;
        Haptics(pulses.Update(source != ProgressSource.None, progress), cfg.RingHaptics.Value);

        if (ready)
            Pose(rig, cfg);
    }

    private void OnBeforeRender()
    {
        var rig = VRRig.Instance;
        if (UIWorldRuntime.Ready && rig != null)
            Pose(rig, HandWidgetsFeature.Settings);
    }

    private void Sync(MovedWidget widget, bool onHand, Transform item, string name)
    {
        if (onHand)
            widget.MoveInto(item, (RectTransform)gui.hudCanvas.transform, name);
        else
            widget.Restore();
    }

    private void Pose(VRRig rig, HandWidgetsSettings cfg)
    {
        var head = rig.Head;
        if (head == null)
            return;
        if (ring.Holding)
        {
            var hand = rig.Hand(ringLeft);
            // A held interaction's ring sits under the target's label (ui.spatial), where the action happens.
            if (
                ringSource == ProgressSource.Interactable
                && TargetPrompt.TryRing(rig, out var atTarget, out var width)
            )
                PoseRing(atTarget, head.position, width);
            else if (hand != null)
            {
                // Above the hand and a little toward the eyes, clear of the held item; parked low in view while
                // the hand is at the face (eating), where above the hand is right in front of the eyes.
                var eye = head.position.N();
                var onHand = HandWidgetPlace.AboveHand(hand.position.N(), eye, 0.08f, 0.03f);
                var pos = HandWidgetPlace.ClearOfFace(
                    onHand,
                    hand.position.N(),
                    eye,
                    head.rotation.N(),
                    HandWidgetPlace.RingParked,
                    ringLeft
                );
                PoseRing(pos.U(), head.position, cfg.RingSize.Value);
            }
        }
        if (feed.Active)
        {
            var left = cfg.Feed.Value == FeedPlacement.LeftHand;
            var hand = rig.Hand(left);
            if (hand != null)
            {
                // Higher than the ring and to the outside, so both can show at once.
                var side = Vector3.Cross(Vector3.up, hand.position - head.position).normalized;
                var pos = hand.position + Vector3.up * 0.14f + side * (left ? -0.04f : 0.04f);
                pos = HandWidgetPlace
                    .ClearOfFace(
                        pos.N(),
                        hand.position.N(),
                        head.position.N(),
                        head.rotation.N(),
                        HandWidgetPlace.FeedParked,
                        left
                    )
                    .U();
                feed.Pose(pos, head.position, cfg.FeedWidth.Value);
            }
        }
    }

    /// <summary>Faces the ring toward the eye and scales it to <paramref name="widthMeters"/> wide.</summary>
    private void PoseRing(Vector3 position, Vector3 eye, float widthMeters)
    {
        var item = ring.Item(0);
        var to = position - eye;
        if (item == null || to.sqrMagnitude < 1e-6f)
            return;
        // Layout containers can report zero width before their first row arrives.
        var pixels = item.rect.width >= 8f ? item.rect.width : 200f;
        ring.Pose(position, Quaternion.LookRotation(to, Vector3.up), widthMeters / pixels);
    }

    /// <summary>Same priority as A/UI_UseItemProgress.cs:31 <c>UpdateFillAmount</c>.</summary>
    private static ProgressSource ReadProgress(out float progress)
    {
        progress = 0f;
        var c = Character.localCharacter;
        if (c == null)
            return ProgressSource.None;
        var spike = c.refs.items.climbingSpikeCastProgress;
        if (spike > 0f)
        {
            progress = spike;
            return ProgressSource.ClimbingSpike;
        }
        var item = c.data.currentItem;
        if (item != null && item.shouldShowCastProgress)
        {
            progress = item.progress;
            return progress > 0f ? ProgressSource.Item : ProgressSource.None;
        }
        var interaction = Interaction.instance;
        if (interaction == null || interaction.currentHeldInteractible == null)
            return ProgressSource.None;
        var held = interaction.constantInteractableProgress;
        if (!(held > 0f))
            return ProgressSource.None;
        progress = held;
        return ProgressSource.Interactable;
    }

    private void Haptics(ProgressPulse pulse, float strength)
    {
        if (pulse == ProgressPulse.None || strength <= 0f || !VRSession.Active)
            return;
        if (pulse == ProgressPulse.Complete)
            UIHaptics.Tick(ringLeft, Mathf.Min(1f, strength * 2f), CompleteSeconds);
        // Holds with their own progress ramp (lid, book, summit, social) feel that; the rest (eating, a piton, a
        // plain held interact) have no ramp and keep the quarter ticks.
        else if (UI.Affordances.Affordance.ProgressActive)
            StepsYielded++;
        else
        {
            StepTicks++;
            UIHaptics.Tick(ringLeft, strength * 0.5f, StepSeconds);
        }
    }

    /// <summary>Quarter-step ticks sent, and those left to an <c>Affordance.Progress</c> ramp (self-test).</summary>
    public static int StepTicks { get; private set; }

    public static int StepsYielded { get; private set; }

    /// <summary>One HUD widget re-parented into a box that is posed in the world each frame.</summary>
    private sealed class MovedWidget
    {
        private const float FallbackPixels = 200f;

        private RectTransform item;
        private Transform parent;
        private int sibling;
        private Vector2 anchorMin,
            anchorMax,
            anchoredPosition;
        private RectTransform box;
        private RectTransform root;

        /// <summary>Rows when the feed was last drawn on top: the game adds a row per friend action
        /// (A/GUIManager.cs:458), and each new one needs the modifier too.</summary>
        private int rows = -1;

        public bool Active => item != null && box != null && item.parent == box;

        public void MoveInto(Transform target, RectTransform hudRoot, string name)
        {
            if (Active || target is not RectTransform rect)
                return;
            root = hudRoot;
            if (box == null)
            {
                // Own (nested) Canvas: posing it every frame re-batches only this box, not the whole HUD.
                box = new GameObject(
                    name,
                    typeof(RectTransform),
                    typeof(Canvas)
                ).GetComponent<RectTransform>();
                box.SetParent(hudRoot, false);
                box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0.5f);
                box.sizeDelta = Vector2.zero;
                OnTop.Sort(box);
            }
            box.gameObject.SetActive(true);
            item = rect;
            parent = rect.parent;
            sibling = rect.GetSiblingIndex();
            anchorMin = rect.anchorMin;
            anchorMax = rect.anchorMax;
            anchoredPosition = rect.anchoredPosition;
            rect.SetParent(box, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rows = -1;
        }

        public void Restore()
        {
            if (item != null && parent != null && item.parent == box)
            {
                OnTop.Set(item, false);
                item.SetParent(parent, false);
                item.SetSiblingIndex(sibling);
                item.anchorMin = anchorMin;
                item.anchorMax = anchorMax;
                item.anchoredPosition = anchoredPosition;
            }
            item = null;
            if (box != null)
                box.gameObject.SetActive(false);
        }

        public void Forget()
        {
            item = null;
            box = null;
            root = null;
        }

        /// <summary>Faces the box toward the eye and scales it so the widget is <paramref name="widthMeters"/> wide.</summary>
        public void Pose(Vector3 position, Vector3 eye, float widthMeters)
        {
            var toBox = position - eye;
            if (toBox.sqrMagnitude < 1e-6f)
                return;
            // Layout containers can report zero width before their first row arrives.
            var pixels = item.rect.width >= 8f ? item.rect.width : FallbackPixels;
            var parentScale = root.lossyScale.x;
            var s = widthMeters / pixels;
            if (Mathf.Abs(parentScale) > 1e-6f)
                s /= parentScale;
            box.localScale = new Vector3(s, s, s);
            // Hand UI: the hand and held item under it never cover it.
            if (item.childCount != rows)
            {
                rows = item.childCount;
                OnTop.Set(item, true);
            }
            box.SetPositionAndRotation(position, Quaternion.LookRotation(toBox, Vector3.up));
        }
    }
}
