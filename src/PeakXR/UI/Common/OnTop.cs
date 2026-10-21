using System.Collections.Generic;
using PeakXR.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace PeakXR.UI.Common;

/// <summary>
/// The rule for UI at the player's own hands (wheels, wrist display, friend feed): draw it without the depth test, so
/// the hand, forearm and held item it belongs to never cover or cut it, and sort it at <see cref="DrawOrder.HandUi"/>,
/// after smoke but under the comfort overlays. Small hand UI showing through a wall the hand is pressed against is
/// the accepted cost. A material modifier rather than a swapped material: the game keeps owning the graphic's
/// material, the mask stencil (applied before this) is kept, and TMP text runs it too
/// (TMP_MaterialManager.GetMaterialForRendering calls every IMaterialModifier).
/// </summary>
internal sealed class OnTop : MonoBehaviour, IMaterialModifier
{
    private static readonly Dictionary<Material, Material> Clones = new();
    private static readonly List<Graphic> Scratch = new();

    private Graphic graphic;
    private bool on;

    /// <summary>Turns drawing on top on or off for every graphic under <paramref name="root"/>, inactive ones
    /// included. Call again after the game adds children (a new feed row).</summary>
    public static void Set(Transform root, bool onTop)
    {
        if (root == null)
            return;
        if (onTop)
            PruneClones();
        Scratch.Clear();
        root.GetComponentsInChildren(true, Scratch);
        foreach (var g in Scratch)
        {
            if (!g.TryGetComponent<OnTop>(out var m))
            {
                if (!onTop)
                    continue;
                m = g.gameObject.AddComponent<OnTop>();
            }
            m.graphic = g;
            if (m.on == onTop)
                continue;
            m.on = onTop;
            g.SetMaterialDirty();
        }
        Scratch.Clear();
    }

    /// <summary>Gives <paramref name="box"/>'s own (nested) canvas the hand-UI sorting order.</summary>
    public static void Sort(Component box)
    {
        if (box == null || !box.TryGetComponent<Canvas>(out var canvas))
            return;
        canvas.overrideSorting = true;
        canvas.sortingOrder = DrawOrder.HandUi;
    }

    /// <summary>Destroys every clone (VR stopped); graphics still on top build fresh ones on their next rebuild.</summary>
    public static void DestroyClones()
    {
        foreach (var clone in Clones.Values)
            if (clone != null)
                Destroy(clone);
        Clones.Clear();
    }

    // Mask stencil materials are rebuilt per open, so their clones go once the stencil material is destroyed.
    private static void PruneClones()
    {
        List<Material> dead = null;
        foreach (var pair in Clones)
            if (pair.Key == null)
                (dead ??= new List<Material>()).Add(pair.Key);
        if (dead == null)
            return;
        foreach (var key in dead)
        {
            if (Clones[key] != null)
                Destroy(Clones[key]);
            Clones.Remove(key);
        }
    }

    /// <summary>Self-test: <paramref name="g"/> currently renders with the depth test off.</summary>
    public static bool IsOnTop(Graphic g)
    {
        var m = g != null ? g.materialForRendering : null;
        return m != null && m.GetInt("unity_GUIZTestMode") == (int)CompareFunction.Always;
    }

    /// <summary>Self-test: every active graphic under <paramref name="root"/> skips the depth test (false when
    /// there is none).</summary>
    public static bool AllOnTop(Transform root)
    {
        if (root == null)
            return false;
        Scratch.Clear();
        root.GetComponentsInChildren(false, Scratch);
        bool any = false,
            all = true;
        foreach (var g in Scratch)
        {
            if (!g.enabled)
                continue;
            any = true;
            all &= IsOnTop(g);
        }
        Scratch.Clear();
        return any && all;
    }

    public Material GetModifiedMaterial(Material baseMaterial)
    {
        if (!on || baseMaterial == null)
            return baseMaterial;
        if (!Clones.TryGetValue(baseMaterial, out var clone) || clone == null)
        {
            clone = new Material(baseMaterial) { name = baseMaterial.name + " (PeakXR on top)" };
            clone.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            Clones[baseMaterial] = clone;
        }
        return clone;
    }

    private void OnDestroy()
    {
        if (on && graphic != null)
            graphic.SetMaterialDirty();
    }
}
