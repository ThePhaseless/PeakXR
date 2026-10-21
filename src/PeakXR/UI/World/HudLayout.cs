using System.Collections.Generic;
using System.Reflection;
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
        var bodyHotbar = SlotGhosts.Showing;
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
        reticleBox.Restore();
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
            if (!plate)
            {
                MoveGroup(gui.bar != null ? gui.bar.transform : null);
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
            // Measure the moved groups only: the box itself is HUD-sized and would make the span the whole HUD.
            bool any = false;
            var bounds = default(Bounds);
            for (int i = 0; i < box.childCount; i++)
            {
                var child = box.GetChild(i);
                if (!child.gameObject.activeSelf)
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
        }

        applied = true;
        appliedReticle = reticle;
        appliedWrist = wrist;
        appliedPlate = plate;
        appliedBodyHotbar = bodyHotbar;
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
        if (!applied || !appliedWrist || wristBox.Rect == null)
            return;
        var hand = rig.Hand(cfg.WristLeft);
        if (hand == null)
            return;

        // Back of the wrist, facing up out of the forearm; text up toward the fingers. Needs in-headset tuning.
        var rotation = hand.rotation * Quaternion.Euler(90f, 0f, 0f);
        var anchor = hand.position + hand.rotation * new Vector3(0f, 0.04f, -0.12f);
        var width = cfg.WristWidth.Value * (1f + 0.15f * WristPulse);
        wristBox.Pose(anchor, rotation, wristCenter, width / wristSpan);
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
