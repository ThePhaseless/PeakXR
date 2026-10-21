using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace PeakXR.UI.Wheels;

/// <summary>
/// Draws one wheel graphic without the depth test while the wheel is open at a hand. The wheel sits where the
/// hand reaches, so the player's own hand, forearm and whatever the hand is in (a backpack on the ground) would
/// otherwise cover it, as in the headset self-test. A material modifier rather than a swapped material: the
/// game keeps owning the graphic's material, the mask stencil (applied before this) is kept, and TMP text runs
/// it too (TMP_MaterialManager.GetMaterialForRendering calls every IMaterialModifier).
/// </summary>
internal sealed class WheelOnTop : MonoBehaviour, IMaterialModifier
{
    private static readonly Dictionary<Material, Material> Clones = new();
    private static readonly List<Graphic> Scratch = new();

    private Graphic graphic;
    private bool on;

    /// <summary>Turns drawing on top on or off for every graphic under <paramref name="root"/>.</summary>
    public static void Set(Transform root, bool onTop)
    {
        if (root == null)
            return;
        Scratch.Clear();
        root.GetComponentsInChildren(true, Scratch);
        foreach (var g in Scratch)
        {
            if (!g.TryGetComponent<WheelOnTop>(out var m))
            {
                if (!onTop)
                    continue;
                m = g.gameObject.AddComponent<WheelOnTop>();
            }
            m.graphic = g;
            if (m.on == onTop)
                continue;
            m.on = onTop;
            g.SetMaterialDirty();
        }
        Scratch.Clear();
    }

    /// <summary>Self-test: <paramref name="g"/> currently renders with the depth test off.</summary>
    public static bool IsOnTop(Graphic g)
    {
        var m = g != null ? g.materialForRendering : null;
        return m != null && m.GetInt("unity_GUIZTestMode") == (int)CompareFunction.Always;
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
