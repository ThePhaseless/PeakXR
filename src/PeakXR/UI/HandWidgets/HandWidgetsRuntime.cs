using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Interact;
using PeakXR.UI.Common;
using PeakXR.UI.World;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.UI.HandWidgets;

/// <summary>
/// Moves the progress ring (A/UI_UseItemProgress.cs:5) and the friend feed container
/// (A/GUIManager.cs:56 <c>friendProgressTF</c>, filled at :458) into hand-posed boxes on the converted
/// HUD canvas. The widgets stay on that canvas, so the game keeps driving them and they die with the
/// scene; everything moved is put back when a setting returns to Hud or VR UI stops.
/// </summary>
[DefaultExecutionOrder(31100)]
internal sealed class HandWidgetsRuntime : MonoBehaviour
{
    private const float StepSeconds = 0.02f;
    private const float CompleteSeconds = 0.08f;

    private readonly MovedWidget ring = new();
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
        Sync(ring, ringOnHand, ringOnHand ? ringWidget.transform : null, "PeakXR_HandRing");
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
        if (ring.Active)
        {
            var hand = rig.Hand(ringLeft);
            if (hand != null)
            {
                // Above the hand and a little toward the eyes, clear of the held item.
                var pos = hand.position + Vector3.up * 0.08f;
                pos += (head.position - pos).normalized * 0.03f;
                ring.Pose(pos, head.position, cfg.RingSize.Value);
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
                feed.Pose(pos, head.position, cfg.FeedWidth.Value);
            }
        }
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
        else
            UIHaptics.Tick(ringLeft, strength * 0.5f, StepSeconds);
    }

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
        }

        public void Restore()
        {
            if (item != null && parent != null && item.parent == box)
            {
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
            box.SetPositionAndRotation(position, Quaternion.LookRotation(toBox, Vector3.up));
        }
    }
}
