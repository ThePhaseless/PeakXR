using System.Collections.Generic;
using System.Reflection;
using PeakXR.Core;
using PeakXR.UI.Common;
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
        var yaw = follow.Update(PanelMath.YawOf(head.forward.ToNumerics(), head.up.ToNumerics()), dt);
        var offset = PanelMath.InFront(yaw, cfg.HudDistance.Value, cfg.HudTilt.Value).ToUnity();
        Position = head.position + offset;
        Rotation = Quaternion.LookRotation(offset, Vector3.up);
        MetersPerPixel = PanelMath.MetersPerPixel(cfg.HudWidth.Value, ReferenceWidth);
    }
}

/// <summary>
/// Moves parts of the converted HUD out of the body panel: the reticle to a head-locked spot and, in
/// wrist mode, the stamina bar and hotbar to the wrist. The parts stay children of the HUD canvas (in
/// containers the size of the HUD), so the game keeps driving them and they die with the scene; only
/// the containers are posed each frame. Everything moved is put back when the mode changes or VR stops.
/// </summary>
internal sealed class HudSplit
{
    private const float ReticleDistance = 2f;

    private static FieldInfo[] reticleFields = System.Array.Empty<FieldInfo>();

    private struct Moved
    {
        public Transform Item;
        public Transform Parent;
        public int Sibling;
    }

    private readonly List<Moved> moved = new();
    private GUIManager gui;
    private RectTransform hudRoot, reticleBox, wristBox;
    private bool applied, appliedWrist;
    private ReticleMode appliedReticle;
    private Vector2 wristCenter;
    private float wristSpan = 1f;

    /// <summary>Every public GameObject field on GUIManager named reticle* (A/GUIManager.cs:208-236).</summary>
    public static void Resolve()
    {
        var list = new List<FieldInfo>();
        foreach (var f in typeof(GUIManager).GetFields(BindingFlags.Public | BindingFlags.Instance))
            if (f.FieldType == typeof(GameObject) && f.Name.StartsWith("reticle", System.StringComparison.Ordinal))
                list.Add(f);
        reticleFields = list.ToArray();
    }

    public void Sync(UIWorldSettings cfg, VRRig rig)
    {
        var current = GUIManager.instance;
        if (current != gui)
        {
            // The old HUD died with its scene, and the moved parts with it.
            moved.Clear();
            reticleBox = wristBox = null;
            applied = false;
            gui = current;
        }
        if (gui == null || gui.hudCanvas == null)
            return;

        var reticle = cfg.Reticle.Value;
        var wrist = cfg.Hud.Value == HudMode.Wrist && rig.Hand(cfg.WristHand.Value == WristSide.Left) != null;
        if (applied && reticle == appliedReticle && wrist == appliedWrist)
            return;

        Restore();
        Apply(reticle, wrist);
    }

    public void Restore()
    {
        for (int i = moved.Count - 1; i >= 0; i--)
        {
            var m = moved[i];
            if (m.Item != null && m.Parent != null)
            {
                m.Item.SetParent(m.Parent, false);
                m.Item.SetSiblingIndex(m.Sibling);
            }
        }
        moved.Clear();
        if (reticleBox != null)
            reticleBox.gameObject.SetActive(false);
        if (wristBox != null)
            wristBox.gameObject.SetActive(false);
        applied = false;
    }

    private void Apply(ReticleMode reticle, bool wrist)
    {
        hudRoot = (RectTransform)gui.hudCanvas.transform;
        if (reticle != ReticleMode.Hud)
        {
            reticleBox = reticleBox != null ? reticleBox : Box("PeakXR_Reticle");
            reticleBox.gameObject.SetActive(reticle == ReticleMode.Head);
            foreach (var field in reticleFields)
                if (field.GetValue(gui) is GameObject go && go != null)
                    Move(go.transform, reticleBox);
        }

        if (wrist)
        {
            wristBox = wristBox != null ? wristBox : Box("PeakXR_Wrist");
            wristBox.gameObject.SetActive(true);
            MoveGroup(gui.bar != null ? gui.bar.transform : null);
            MoveGroup(gui.items != null && gui.items.Length > 0 && gui.items[0] != null ? gui.items[0].transform : null);
            MoveGroup(gui.backpack != null ? gui.backpack.transform : null);
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(wristBox);
            wristCenter = bounds.center;
            wristSpan = Mathf.Max(1f, bounds.size.x);
        }

        applied = true;
        appliedReticle = reticle;
        appliedWrist = wrist;
    }

    private RectTransform Box(string name)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(hudRoot, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = hudRoot.sizeDelta;
        return rect;
    }

    private void Move(Transform item, Transform box)
    {
        if (item.parent == box)
            return;
        moved.Add(new Moved { Item = item, Parent = item.parent, Sibling = item.GetSiblingIndex() });
        item.SetParent(box, false);
    }

    /// <summary>Moves the HUD group holding <paramref name="target"/>: its ancestor directly under the HUD
    /// root, or a smaller one when that ancestor is a full-screen container.</summary>
    private void MoveGroup(Transform target)
    {
        if (target == null)
            return;
        var group = target;
        while (group.parent != null && group.parent != hudRoot && group.parent != wristBox)
            group = group.parent;
        if (group.parent == wristBox)
            return;
        if (group.parent != hudRoot)
            return;

        while (group != target && group is RectTransform r && CoversHud(r))
        {
            var next = target;
            while (next.parent != group)
                next = next.parent;
            group = next;
        }
        Move(group, wristBox);
    }

    private bool CoversHud(RectTransform r)
    {
        var hud = hudRoot.rect;
        return r.rect.width >= hud.width * 0.9f && r.rect.height >= hud.height * 0.9f;
    }

    /// <summary>Poses the wrist container. Called once per frame after the canvases are placed.</summary>
    public void PoseWrist(VRRig rig, UIWorldSettings cfg)
    {
        if (!applied || !appliedWrist || wristBox == null)
            return;
        var hand = rig.Hand(cfg.WristHand.Value == WristSide.Left);
        if (hand == null)
            return;

        // Back of the wrist, facing up out of the forearm; text up toward the fingers. Needs in-headset tuning.
        var rotation = hand.rotation * Quaternion.Euler(90f, 0f, 0f);
        var anchor = hand.position + hand.rotation * new Vector3(0f, 0.04f, -0.12f);
        var metersPerPixel = cfg.WristWidth.Value / wristSpan;
        SetScale(wristBox, metersPerPixel);
        wristBox.SetPositionAndRotation(anchor - rotation * (wristCenter * metersPerPixel), rotation);
    }

    /// <summary>Poses the head-locked reticle. Called late and again right before rendering.</summary>
    public void PoseReticle(Transform head, HudLayout hud, UIWorldSettings cfg)
    {
        if (!applied || appliedReticle != ReticleMode.Head || reticleBox == null)
            return;
        SetScale(reticleBox, hud.MetersPerPixel * ReticleDistance / cfg.HudDistance.Value);
        reticleBox.SetPositionAndRotation(head.position + head.forward * ReticleDistance, head.rotation);
    }

    private void SetScale(RectTransform box, float metersPerPixel)
    {
        var parent = hudRoot.lossyScale.x;
        var s = Mathf.Abs(parent) > 1e-6f ? metersPerPixel / parent : metersPerPixel;
        box.localScale = new Vector3(s, s, s);
    }
}
