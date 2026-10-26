using System.Collections.Generic;
using System.Reflection;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Inventory;
using PeakXR.UI.Common;
using PeakXR.UI.Spatial;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.UI.World;

/// <summary>
/// Body-locked anchor for the HUD: follows head position at once and head yaw lazily, so the HUD
/// stays put during small glances and re-centres after a real turn.
/// </summary>
internal sealed class HudLayout
{
    private const float ReferenceWidth = 1920f;
    private readonly LazyYawFollow follow = new();

    public Vector3 Position;
    public Quaternion Rotation = Quaternion.identity;
    public float MetersPerPixel = 0.001f;

    public void Update(Transform head, UIWorldSettings cfg, float dt)
    {
        follow.Threshold = cfg.HudFollowAngle.Value;
        var yaw = follow.Update(
            PanelMath.YawOf(head.forward.ToNumerics(), head.up.ToNumerics()),
            dt
        );
        var offset = PanelMath.InFront(yaw, cfg.HudDistance.Value, cfg.HudTilt.Value).ToUnity();
        Position = head.position + offset;
        Rotation = Quaternion.LookRotation(offset, Vector3.up);
        MetersPerPixel = PanelMath.MetersPerPixel(cfg.HudWidth.Value, ReferenceWidth);
    }
}

/// <summary>
/// Moves parts of the converted HUD out of the body panel: the reticle to a head-locked spot and, in
/// wrist mode, the stamina bar (with its numb mushroom group) and hotbar to the wrist. The parts sit in
/// <see cref="HudBox"/> containers under the HUD canvas, so the game keeps driving them; only the
/// containers are posed each frame. Everything moved is put back when the mode changes or VR stops.
/// </summary>
internal sealed class HudSplit
{
    private const float ReticleDistance = 2f;

    private static FieldInfo[] reticleFields = System.Array.Empty<FieldInfo>();

    private readonly HudBox reticleBox = new("PeakXR_Reticle");
    private readonly HudBox wristBox = new("PeakXR_Wrist");
    private GUIManager gui;
    private RectTransform hudRoot;
    private bool applied,
        appliedWrist,
        appliedPlate,
        appliedBodyHotbar;
    private ReticleMode appliedReticle;
    private Vector2 wristCenter;

    // Until something is measured: about the width of the stamina bar, never 1 px (a screen-sized wrist).
    private float wristSpan = 400f;

    /// <summary>0..1 bump that briefly grows the wrist HUD (ui.spatial status alerts).</summary>
    public static float WristPulse;

    /// <summary>The wrist HUD was posed this frame, at <see cref="WristPosition"/> (centre of the status bar) and
    /// <see cref="WristRotation"/> (up toward the fingers), so other wrist parts can sit beside it.</summary>
    public static bool WristValid;
    public static Vector3 WristPosition;
    public static Quaternion WristRotation = Quaternion.identity;

    /// <summary>The watch-face rotation on the wrist before the turn toward the eye (self-test).</summary>
    public static Quaternion WristRest = Quaternion.identity;

    /// <summary>Width of the posed wrist HUD in metres and how far (0..1) it is turned toward the eye.</summary>
    public static float WristWidthMeters,
        WristFacing;

    /// <summary>The wrist container while the HUD is split onto the wrist (self-test).</summary>
    public static RectTransform WristRoot { get; private set; }
    private int measuredMask;

    /// <summary>Dark plate behind the wrist parts (first child of the wrist box): the stamina bar is translucent, and
    /// over the forearm's patterned sleeve it read as sunk into the arm.</summary>
    private UnityEngine.UI.Image wristPlate;

    /// <summary>Plate margin round the wrist parts, in HUD pixels, and its opacity.</summary>
    private const float WristPlatePad = 14f;
    private const float WristPlateOpacity = 0.85f;

    /// <summary>Every public GameObject field on GUIManager named reticle* (A/GUIManager.cs:208-236).</summary>
    public static void Resolve()
    {
        var list = new List<FieldInfo>();
        foreach (var f in typeof(GUIManager).GetFields(BindingFlags.Public | BindingFlags.Instance))
            if (
                f.FieldType == typeof(GameObject)
                && f.Name.StartsWith("reticle", System.StringComparison.Ordinal)
            )
                list.Add(f);
        reticleFields = list.ToArray();
    }

    public void Sync(UIWorldSettings cfg, VRRig rig)
    {
        var current = GUIManager.instance;
        if (current != gui)
        {
            // The old HUD died with its scene, and the moved parts with it.
            reticleBox.Forget();
            wristBox.Forget();
            applied = false;
            gui = current;
        }
        if (gui == null || gui.hudCanvas == null)
            return;

        var reticle = cfg.Reticle.Value;
        var wrist = cfg.Hud.Value == HudMode.Wrist && rig.Hand(cfg.WristLeft) != null;
        // While spectating the status bar rides on the friend's nameplate (ui.spatial), and with the body hotbar
        // (inventory.vr) the 2D hotbar stays hidden on the panel instead of filling the wrist.
        var plate = SpectateView.ClaimsStatus;
        // Keyed on the settings, not on SlotGhosts.Showing, so focus loss (dashboard) does not re-lay the wrist.
        var bodyHotbar = InventoryConfig.Instance.BodySlots && InventoryConfig.Instance.Ghosts;
        if (
            applied
            && reticle == appliedReticle
            && wrist == appliedWrist
            && plate == appliedPlate
            && bodyHotbar == appliedBodyHotbar
        )
            return;

        Restore();
        Apply(reticle, wrist, plate, bodyHotbar);
    }

    public void Restore()
    {
        WristValid = false;
        reticleBox.Restore();
        if (wristBox.Rect != null)
            OnTop.Set(wristBox.Rect, false);
        wristBox.Restore();
        if (gui != null && gui.heroBG != null)
            gui.heroBG.enabled = true;
        applied = false;
    }

    private void Apply(ReticleMode reticle, bool wrist, bool plate, bool bodyHotbar)
    {
        hudRoot = (RectTransform)gui.hudCanvas.transform;
        // The biome title dims the whole screen (A/GUIManager.cs:511-512); on a floating panel that is
        // just a dark rectangle, so the title text shows without it.
        if (gui.heroBG != null)
            gui.heroBG.enabled = false;
        if (reticle != ReticleMode.Hud)
        {
            reticleBox.Ensure(hudRoot);
            reticleBox.SetActive(reticle == ReticleMode.Head);
            foreach (var field in reticleFields)
                if (field.GetValue(gui) is GameObject go && go != null)
                    reticleBox.Move(go.transform);
        }

        if (wrist)
        {
            var box = wristBox.Ensure(hudRoot);
            EnsureWristPlate(box);
            if (!plate)
            {
                MoveGroup(gui.bar != null ? gui.bar.transform : null);
                MoveAfflictions(gui.bar);
                // Numb swaps the stamina bar for a mushroom group (A/GUIManager.cs:1249); it belongs with the bar.
                MoveGroup(
                    gui.mushroomsCanvasGroup != null ? gui.mushroomsCanvasGroup.transform : null
                );
            }
            if (!bodyHotbar)
            {
                MoveGroup(
                    gui.items != null && gui.items.Length > 0 && gui.items[0] != null
                        ? gui.items[0].transform
                        : null
                );
                MoveGroup(gui.backpack != null ? gui.backpack.transform : null);
            }
            // The dying bar (A/GUIManager.cs:578) belongs with the wrist bar, not on the body panel.
            MoveGroup(gui.dyingBarObject != null ? gui.dyingBarObject.transform : null);
            MeasureWrist(box);
            // Hand UI: the forearm and hand it rides on never cover it.
            OnTop.Sort(box);
            OnTop.Set(box, true);
        }

        applied = true;
        appliedReticle = reticle;
        appliedWrist = wrist;
        appliedPlate = plate;
        appliedBodyHotbar = bodyHotbar;
    }

    /// <summary>Measures the moved groups that are shown: the box itself is HUD-sized and would make the span the
    /// whole HUD. Runs on apply and when a group shows or hides (the dying bar), not every frame.</summary>
    private void MeasureWrist(RectTransform box)
    {
        measuredMask = ShownMask(box);
        bool any = false;
        var bounds = default(Bounds);
        var plate = wristPlate != null ? wristPlate.transform : null;
        for (int i = 0; i < box.childCount; i++)
        {
            var child = box.GetChild(i);
            if (!child.gameObject.activeSelf || child == plate)
                continue;
            var b = RectTransformUtility.CalculateRelativeRectTransformBounds(box, child);
            if (any)
                bounds.Encapsulate(b);
            else
                bounds = b;
            any = true;
        }
        if (any)
        {
            wristCenter = bounds.center;
            wristSpan = Mathf.Max(1f, bounds.size.x);
        }
        if (wristPlate != null)
        {
            var rt = wristPlate.rectTransform;
            rt.anchoredPosition = bounds.center;
            rt.sizeDelta = (Vector2)bounds.size + 2f * WristPlatePad * Vector2.one;
            if (wristPlate.gameObject.activeSelf != any)
                wristPlate.gameObject.SetActive(any);
        }
    }

    /// <summary>Puts <see cref="wristPlate"/> first in <paramref name="box"/> (a new box after a scene change gets a
    /// new plate); <see cref="MeasureWrist"/> fits it. Drawn on top with the box, under the parts.</summary>
    private void EnsureWristPlate(RectTransform box)
    {
        if (wristPlate == null || wristPlate.transform.parent != box)
        {
            var go = new GameObject(
                "PeakXR_WristPlate",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(UnityEngine.UI.Image),
                typeof(PanelPart)
            );
            go.layer = box.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(box, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            wristPlate = go.GetComponent<UnityEngine.UI.Image>();
            wristPlate.sprite = BackplateView.PlateSprite();
            wristPlate.type = UnityEngine.UI.Image.Type.Sliced;
            wristPlate.raycastTarget = false;
            var c = MenuBackplate.Color;
            wristPlate.color = new Color(c.X, c.Y, c.Z, WristPlateOpacity);
        }
        wristPlate.transform.SetAsFirstSibling();
    }

    private int ShownMask(RectTransform box)
    {
        int mask = 0;
        int n = Mathf.Min(box.childCount, 31);
        var plate = wristPlate != null ? wristPlate.transform : null;
        for (int i = 0; i < n; i++)
        {
            var child = box.GetChild(i);
            if (child != plate && child.gameObject.activeSelf)
                mask |= 1 << i;
        }
        return mask;
    }

    /// <summary>The stamina bar finds its affliction segments anywhere under its parent (A/StaminaBar.cs:85), not
    /// only under itself, so moving the bar's group can leave them on the body panel, where the wrist never showed
    /// the Cold segment (VR run 25). Moves their own group too, after the bar's.</summary>
    private void MoveAfflictions(StaminaBar bar)
    {
        if (bar == null || bar.afflictions == null || wristBox.Rect == null)
            return;
        foreach (var a in bar.afflictions)
        {
            if (a == null || a.transform.IsChildOf(wristBox.Rect))
                continue;
            var group = AfflictionGroup(a, bar, hudRoot);
            if (group != null)
                wristBox.Move(group);
        }
    }

    /// <summary>The HUD group to move so affliction segment <paramref name="a"/> follows <paramref name="bar"/>:
    /// segments in a container of their own move with it, keeping their layout (see <see cref="GroupOf"/>).</summary>
    internal static Transform AfflictionGroup(
        BarAffliction a,
        StaminaBar bar,
        RectTransform hudRoot
    )
    {
        var parent = a.transform.parent;
        return GroupOf(
            parent != null && parent != bar.transform.parent ? parent : a.transform,
            hudRoot
        );
    }

    /// <summary>Moves the HUD group holding <paramref name="target"/>: its ancestor directly under the HUD
    /// root, or a smaller one when that ancestor is a full-screen container.</summary>
    private void MoveGroup(Transform target)
    {
        var group = GroupOf(target, hudRoot);
        if (group != null)
            wristBox.Move(group);
    }

    /// <summary>The HUD group holding <paramref name="target"/>: its ancestor directly under
    /// <paramref name="hudRoot"/>, or a smaller one when that ancestor is a full-screen container. Inside a
    /// full-HUD <see cref="HudBox"/> that is the box's child; null when the target is not in the HUD or sits in a
    /// small PeakXR box (ui.spatial), whose owner keeps it.</summary>
    internal static Transform GroupOf(Transform target, RectTransform hudRoot)
    {
        if (target == null || hudRoot == null)
            return null;
        var group = target;
        while (group.parent != null && group.parent != hudRoot)
            group = group.parent;
        if (group.parent != hudRoot)
            return null;
        // Runs only when the layout is re-applied, so reading the name (it allocates) is fine.
        if (
            group != target
            && group is RectTransform box
            && !CoversHud(box, hudRoot)
            && group.name.StartsWith("PeakXR_", System.StringComparison.Ordinal)
        )
            return null;

        while (group != target && group is RectTransform r && CoversHud(r, hudRoot))
        {
            var next = target;
            while (next.parent != group)
                next = next.parent;
            group = next;
        }
        return group;
    }

    private static bool CoversHud(RectTransform r, RectTransform hudRoot)
    {
        var hud = hudRoot.rect;
        return r.rect.width >= hud.width * 0.9f && r.rect.height >= hud.height * 0.9f;
    }

    /// <summary>Poses the wrist container. Called once per frame after the canvases are placed.</summary>
    public void PoseWrist(VRRig rig, UIWorldSettings cfg)
    {
        WristValid = false;
        if (!applied || !appliedWrist || wristBox.Rect == null)
            return;
        var hand = rig.Hand(cfg.WristLeft);
        if (hand == null)
            return;
        if (ShownMask(wristBox.Rect) != measuredMask)
        {
            MeasureWrist(wristBox.Rect);
            // A part the game showed for the first time may carry a graphic not yet drawn on top.
            OnTop.Set(wristBox.Rect, true);
        }

        // Like a watch face: on the back of the wrist, the side that faces the eye when the wrist is raised to read
        // it, the bar across the wrist and text up toward the fingers (WristHud.Rest, mirrored per hand). Raised off
        // the arm and drawn on top, so the forearm never cuts it. It turns toward the eye (WristHud.Facing: halfway
        // at rest, fully when looked at or on an alert) and keeps its angular size.
        bool left = cfg.WristLeft;
        var anchor = WristHud.Anchor(hand.position.N(), hand.rotation.N(), left).U();
        var rotation = WristHud.Rest(hand.rotation.N(), left).U();
        WristRest = rotation;
        float baseWidth = WristHud.ScaledWidth(cfg.WristWidth.Value, cfg.TextScale.Value);
        var width = WristHud.Width(baseWidth, WristHud.ReadDistance, WristPulse);
        var head = rig.Head;
        var toEye = head != null ? head.position - anchor : Vector3.zero;
        float distance = toEye.magnitude;
        if (distance > 0.01f)
        {
            float gaze = Vector3.Angle(head.forward, -toEye);
            WristFacing = Mathf.MoveTowards(
                WristFacing,
                WristHud.Facing(gaze, WristPulse),
                Time.unscaledDeltaTime * 4f
            );
            var face = Quaternion.LookRotation(-toEye, head.up);
            rotation = Quaternion.Slerp(rotation, face, WristFacing);
            width = WristHud.Width(baseWidth, distance, WristPulse);
        }
        wristBox.Pose(anchor, rotation, wristCenter, width / wristSpan);
        WristValid = true;
        WristWidthMeters = width;
        WristRoot = wristBox.Rect;
        WristPosition = anchor;
        WristRotation = rotation;
    }

    /// <summary>Poses the head-locked reticle. Called late and again right before rendering.</summary>
    public void PoseReticle(Transform head, HudLayout hud, UIWorldSettings cfg)
    {
        if (!applied || appliedReticle != ReticleMode.Head || reticleBox.Rect == null)
            return;
        reticleBox.SetScale(hud.MetersPerPixel * ReticleDistance / cfg.HudDistance.Value);
        reticleBox.Rect.SetPositionAndRotation(
            head.position + head.forward * ReticleDistance,
            head.rotation
        );
    }
}
