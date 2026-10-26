using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Rig;
using PeakXR.UI.Common;
using PeakXR.UI.Spatial;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.UI;

namespace PeakXR.UI.Wheels;

/// <summary>
/// Shows where the emotes are, on the controller itself. The tester never found the emote wheel: it opens on a long
/// press of the button that also pings, and nothing hinted at the hold. Resting the thumb on that button (Y by default,
/// the binding table's Emote row) now shows the game's own first emote picture (<c>EmoteWheel.data</c>,
/// A/EmoteWheel.cs:10, <c>EmoteWheelData.emoteSprite</c>) just above the controller, beside the ping preview the same
/// touch brings up; holding the button fills a ring around it (the recenter hold's <see cref="HoldCue"/>) until the
/// wheel opens. Hidden while the game would refuse the wheel (climbing, dead, another wheel or a window up).
/// </summary>
internal sealed class EmoteHint
{
    private const float IconMeters = 0.035f;

    /// <summary>Same spot as <see cref="HoldCue"/>, so the ring fills around the picture.</summary>
    private const float Above = 0.08f;

    private const float RestAlpha = 0.6f;
    private const float CanvasPixels = 100f;

    private static readonly float LongPressSeconds = new ActionMapper().LongPressSeconds;

    private readonly HoldCue ring = new();
    private readonly Transform parent;
    private GameObject icon;
    private Image image;
    private float heldFor;

    public EmoteHint(Transform parent) => this.parent = parent;

    /// <summary>Per frame while VR menus run. <paramref name="wheelOpen"/>: the emote wheel is up, so no hint.</summary>
    public void Tick(GUIManager gui, EmoteWheel wheel, VRRig rig, bool wheelOpen, float dt)
    {
        var c = Character.localCharacter;
        bool touched = false,
            held = false,
            left = false;
        if (
            c != null
            && c.data != null
            && !wheelOpen
            && rig.Head != null
            && ControllerLabels.ForAction(
                VRAction.Emote,
                VRInput.LeftHanded,
                LocalHold.ItemInLeftHand,
                out left,
                out var control
            )
            && EmoteGestures.WheelRefusal(
                c.data.dead,
                c.refs.stats.won,
                c.data.isClimbing,
                gui.backpackWheel != null && gui.backpackWheel.gameObject.activeSelf,
                gui.windowBlockingInput
            ) == DenyReason.None
        )
        {
            ref var s = ref VRInput.Hand(left);
            if (s.Tracked)
            {
                (touched, held) = control switch
                {
                    VRControl.Secondary => (s.SecondaryTouch, s.Secondary),
                    VRControl.Primary => (s.PrimaryTouch, s.Primary),
                    _ => (false, false),
                };
            }
        }
        heldFor = held ? heldFor + dt : 0f;
        var hand = held || touched ? rig.Hand(left) : null;
        ring.Show(held ? hand : null, rig.Head, heldFor, heldFor / LongPressSeconds);

        var sprite = Sprite(wheel);
        bool show = hand != null && sprite != null;
        if (!show)
        {
            if (icon != null && icon.activeSelf)
                icon.SetActive(false);
            return;
        }
        if (icon == null)
            Build();
        if (image.sprite != sprite)
            image.sprite = sprite;
        if (!icon.activeSelf)
            icon.SetActive(true);
        icon.layer = hand.gameObject.layer;
        var at = hand.position + Vector3.up * Above;
        icon.transform.SetPositionAndRotation(at, SpatialRuntime.FaceEye(at, rig.Head.position));
        var alpha = held ? 1f : RestAlpha;
        if (!Mathf.Approximately(image.color.a, alpha))
            image.color = new Color(1f, 1f, 1f, alpha);
    }

    public void Hide()
    {
        heldFor = 0f;
        ring.Show(null, null, 0f, 0f);
        if (icon != null)
            icon.SetActive(false);
    }

    public void Destroy()
    {
        ring.Destroy();
        if (icon != null)
            Object.Destroy(icon);
        icon = null;
        image = null;
    }

    /// <summary>The first emote with a picture in the wheel's data, whatever page the wheel was left on.</summary>
    private static Sprite Sprite(EmoteWheel wheel)
    {
        var data = wheel != null ? wheel.data : null;
        if (data == null)
            return null;
        foreach (var d in data)
            if (d != null && d.emoteSprite != null)
                return d.emoteSprite;
        return null;
    }

    private void Build()
    {
        icon = new GameObject("PeakXR_EmoteHint", typeof(RectTransform));
        icon.transform.SetParent(parent, false);
        var canvas = icon.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = DrawOrder.HandUi;
        var rect = (RectTransform)icon.transform;
        rect.sizeDelta = new Vector2(CanvasPixels, CanvasPixels);
        rect.localScale = Vector3.one * (IconMeters / CanvasPixels);
        var child = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer));
        child.transform.SetParent(rect, false);
        var childRect = (RectTransform)child.transform;
        childRect.anchorMin = Vector2.zero;
        childRect.anchorMax = Vector2.one;
        childRect.offsetMin = childRect.offsetMax = Vector2.zero;
        image = child.AddComponent<Image>();
        image.preserveAspect = true;
        image.raycastTarget = false;
        // Over the player's own hand and the controller, like the hand labels.
        OnTop.Set(rect, true);
    }
}
