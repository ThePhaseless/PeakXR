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
/// <c>IInteractible.Center()</c> (A/IInteractible.cs:13), level, facing you, at a constant angular size. The hold
/// ring of a held interaction (ui.hand) goes just below it through <see cref="TryRing"/>.
/// </summary>
internal sealed class TargetPrompt
{
    private const float Spacing = 6f;
    private const float RingDegrees = 3.5f;

    private static readonly List<Renderer> renderers = new();
    private static bool ringValid;
    private static Vector3 ringPosition;
    private static float ringWidth;

    private readonly SpatialBox box = new("PeakXR_TargetPrompt");
    private readonly GameObject[] parts = new GameObject[4];
    private readonly bool[] shown = new bool[4];
    private IInteractible target;
    private float radius = 0.1f,
        height;
    private bool warned;

    /// <summary>Where ui.hand puts the progress ring of a held interaction: under the target's label.</summary>
    public static bool TryRing(out Vector3 position, out float width)
    {
        position = ringPosition;
        width = ringWidth;
        return ringValid;
    }

    public void Release()
    {
        box.Restore();
        target = null;
        ringValid = false;
    }

    public void Forget()
    {
        box.Forget();
        target = null;
        ringValid = false;
    }

    public void Tick(GUIManager gui, RectTransform hudRoot, VRRig rig)
    {
        if (!box.Holding)
        {
            Grab(gui, hudRoot);
            if (!box.Holding)
                return;
        }

        var interaction = Interaction.instance;
        var current = interaction != null ? interaction.currentHovered : null;
        if (!current.UnityObjectExists())
            current = null;
        if (current != target)
        {
            target = current;
            if (target != null)
            {
                radius = Radius(target);
                // One soft tick on the pointing hand when it lands on something new.
                var aim = PhysicalInteraction.AimHand;
                if (aim >= 0)
                    UIHaptics.Tick(aim == 0, 0.12f, 0.015f);
            }
        }

        bool changed = false;
        for (int i = 0; i < parts.Length; i++)
        {
            bool on = parts[i] != null && parts[i].activeSelf;
            if (on != shown[i])
            {
                shown[i] = on;
                changed = true;
            }
        }
        if (changed)
            Stack();

        box.SetVisible(target != null);
        Pose(rig);
    }

    public void Pose(VRRig rig)
    {
        if (target == null || !box.Holding || !target.UnityObjectExists())
        {
            ringValid = false;
            return;
        }
        var eye = rig.Head.position;
        var p = SpatialMath
            .TargetLabel(target.Center().ToNumerics(), eye.ToNumerics(), radius)
            .ToUnity();
        var d = Vector3.Distance(p, eye);
        var mpp = SpatialMath.MetersPerPixel(d, SpatialRuntime.DegreesPerPixel);
        box.Pose(p, SpatialRuntime.FaceEyeYaw(p, eye, rig.Head.forward), mpp);

        ringWidth = SpatialMath.SpanMeters(d, RingDegrees);
        ringPosition = p + Vector3.down * (height * 0.5f * mpp + ringWidth * 0.7f);
        ringValid = true;
    }

    private void Grab(GUIManager gui, RectTransform hudRoot)
    {
        parts[0] = gui.interactName;
        parts[1] = gui.interactPromptPrimary;
        parts[2] = gui.interactPromptSecondary;
        parts[3] = gui.interactPromptHold;
        for (int i = 0; i < parts.Length; i++)
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
        Stack();
    }

    /// <summary>Stacks the shown parts top to bottom (name, then actions) around the box origin.</summary>
    private void Stack()
    {
        height = 0f;
        for (int i = 0; i < parts.Length; i++)
        {
            if (shown[i])
                height += Height(i) + (height > 0f ? Spacing : 0f);
        }
        float y = height * 0.5f;
        bool first = true;
        for (int i = 0; i < box.Count; i++)
        {
            var item = box.Item(i);
            int part = IndexOf(item);
            if (part < 0 || !shown[part])
                continue;
            if (!first)
                y -= Spacing;
            first = false;
            float h = Height(part);
            box.Place(i, new Vector2(0f, y - h * 0.5f));
            y -= h;
        }
    }

    private float Height(int part) =>
        parts[part] != null && parts[part].transform is RectTransform r ? r.rect.height : 0f;

    private int IndexOf(RectTransform item)
    {
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] != null && parts[i].transform == item)
                return i;
        }
        return -1;
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
