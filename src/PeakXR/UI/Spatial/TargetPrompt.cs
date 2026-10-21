using System.Collections.Generic;
using PeakXR.Core;
using PeakXR.Interact;
using PeakXR.UI.Common;
using PeakXR.VR;
using UnityEngine;
using Zorro.Core;

namespace PeakXR.UI.Spatial;

/// <summary>
/// U1: the interact prompt at the thing it is about. <c>GUIManager.RefreshInteractablePrompt</c> (A/GUIManager.cs:1045)
/// fills <c>interactName</c> and <c>interactPromptPrimary/Secondary/Hold</c> (A/GUIManager.cs:84-92) for
/// <c>Interaction.currentHovered</c> (A/Interaction.cs:15); those groups move into a box posed in front of
/// <c>IInteractible.Center()</c> (A/IInteractible.cs:13), level, facing you, at a constant angular size. Prompts
/// that have no target sit above the acting hand instead: the piton prompt while climbing (SETPITON,
/// A/GUIManager.cs:1080-1084), the lunge prompt (<c>interactPromptLunge</c>, :94, set at :371) and the web
/// struggle prompt (<c>strugglePrompt</c>, :100, shown from A/CharacterAfflictions.cs:803). The hold ring of a held
/// interaction (ui.hand) goes just below the label through <see cref="TryRing"/>.
/// </summary>
internal sealed class TargetPrompt
{
    private const float Spacing = 6f;
    private const float RingDegrees = 3.5f;
    private const int Parts = 6;

    private static readonly List<Renderer> renderers = new();
    private static TargetPrompt current;

    private readonly SpatialBox box = new("PeakXR_TargetPrompt");
    private readonly GameObject[] parts = new GameObject[Parts];
    private readonly bool[] shown = new bool[Parts];
    private IInteractible target;
    private float radius = 0.1f,
        height;
    private bool anyShown,
        warned;
    private int resolvedFrame = -1;

    /// <summary>
    /// Where ui.hand puts the progress ring of a held interaction: under the target's label. Computed on demand from
    /// this frame's hover, so the ring never trails a frame behind a target change.
    /// </summary>
    public static bool TryRing(VRRig rig, out Vector3 position, out float width)
    {
        position = default;
        width = 0f;
        var prompt = current;
        if (prompt == null || !prompt.box.Holding || rig == null || rig.Head == null)
            return false;
        prompt.Resolve();
        if (prompt.target == null || !prompt.TargetPose(rig, out var p, out var mpp, out var d))
            return false;
        width = SpatialMath.SpanMeters(d, RingDegrees);
        position = p + Vector3.down * (prompt.height * 0.5f * mpp + width * 0.7f);
        return true;
    }

    public void Release()
    {
        box.Restore();
        target = null;
        if (current == this)
            current = null;
    }

    public void Forget()
    {
        box.Forget();
        target = null;
        if (current == this)
            current = null;
    }

    public void Tick(GUIManager gui, RectTransform hudRoot, VRRig rig)
    {
        if (!box.Holding)
        {
            Grab(gui, hudRoot);
            if (!box.Holding)
                return;
        }
        current = this;
        Resolve();

        bool changed = false;
        anyShown = false;
        for (int i = 0; i < Parts; i++)
        {
            bool on = parts[i] != null && parts[i].activeSelf;
            anyShown |= on;
            if (on != shown[i])
            {
                shown[i] = on;
                changed = true;
            }
        }
        if (changed)
            height = box.Stack(Spacing);

        box.SetVisible(target != null || anyShown);
        Pose(rig);
    }

    public void Pose(VRRig rig)
    {
        if (!box.Holding)
            return;
        var eye = rig.Head.position;
        if (target != null && TargetPose(rig, out var p, out var mpp, out _))
        {
            box.Pose(p, SpatialRuntime.FaceEyeYaw(p, eye, rig.Head.forward), mpp);
            return;
        }
        if (!anyShown)
            return;
        // No target: above the hand that acts (the pointing hand, else the dominant one).
        var aim = PhysicalInteraction.AimHand;
        var hand = rig.Hand(aim >= 0 ? aim == 0 : VRInput.LeftHanded);
        if (hand == null)
            return;
        var at = hand.position + Vector3.up * 0.14f;
        at += (eye - at).normalized * 0.04f;
        box.Pose(
            at,
            SpatialRuntime.FaceEyeYaw(at, eye, rig.Head.forward),
            SpatialMath.MetersPerPixel(Vector3.Distance(at, eye), SpatialRuntime.DegreesPerPixel)
        );
    }

    /// <summary>Follows this frame's hovered target; a new one gets measured and one soft tick.</summary>
    private void Resolve()
    {
        if (resolvedFrame == Time.frameCount)
            return;
        resolvedFrame = Time.frameCount;
        var interaction = Interaction.instance;
        var hovered = interaction != null ? interaction.currentHovered : null;
        if (!hovered.UnityObjectExists())
            hovered = null;
        if (hovered == target)
            return;
        target = hovered;
        if (target == null)
            return;
        radius = Radius(target);
        var aim = PhysicalInteraction.AimHand;
        if (aim >= 0)
            UIHaptics.Tick(aim == 0, 0.12f, 0.015f);
    }

    private bool TargetPose(VRRig rig, out Vector3 position, out float mpp, out float distance)
    {
        position = default;
        mpp = distance = 0f;
        if (!target.UnityObjectExists())
            return false;
        var eye = rig.Head.position;
        position = SpatialMath
            .TargetLabel(target.Center().ToNumerics(), eye.ToNumerics(), radius)
            .ToUnity();
        distance = Vector3.Distance(position, eye);
        mpp = SpatialMath.MetersPerPixel(distance, SpatialRuntime.DegreesPerPixel);
        return true;
    }

    private void Grab(GUIManager gui, RectTransform hudRoot)
    {
        // Stack order, top to bottom.
        parts[0] = gui.interactName;
        parts[1] = gui.interactPromptPrimary;
        parts[2] = gui.interactPromptSecondary;
        parts[3] = gui.interactPromptHold;
        parts[4] = gui.interactPromptLunge;
        parts[5] = gui.strugglePrompt != null ? gui.strugglePrompt.gameObject : null;
        for (int i = 0; i < Parts; i++)
        {
            shown[i] = parts[i] != null && parts[i].activeSelf;
            if (parts[i] != null)
                box.Move(hudRoot, parts[i].transform, Vector2.zero);
        }
        if (
            !warned
            && gui.interactPromptText != null
            && gui.interactPromptPrimary != null
            && !gui.interactPromptText.transform.IsChildOf(gui.interactPromptPrimary.transform)
        )
        {
            warned = true;
            Plugin.Log.LogWarning(
                "[ui.spatial] interactPromptText is not under interactPromptPrimary; the action text stays on the HUD"
            );
        }
        height = box.Stack(Spacing);
    }

    /// <summary>Rough size of the target from its renderers, measured once per new target.</summary>
    private static float Radius(IInteractible target)
    {
        if (target is not Component comp)
            return 0.1f;
        comp.GetComponentsInChildren(false, renderers);
        bool any = false;
        var bounds = default(Bounds);
        foreach (var r in renderers)
        {
            if (r is ParticleSystemRenderer || !r.enabled)
                continue;
            if (any)
                bounds.Encapsulate(r.bounds);
            else
                bounds = r.bounds;
            any = true;
        }
        renderers.Clear();
        return any ? bounds.extents.magnitude : 0.1f;
    }
}
