using System.Collections.Generic;
using PeakXR.Core;
using UnityEngine;

namespace PeakXR.UI.Affordances;

/// <summary>
/// The red preview of a refusal: the refused thing itself turns red while the refusal is up. A held item goes through
/// the game's own highlight path (<c>Item.HoverEnter</c>, Item.cs:1145-1153: a <see cref="MaterialPropertyBlock"/>
/// with <c>_Interactable</c> on <c>mainRenderer</c> and <c>addtlRenderers</c>) with its <c>_Tint</c> (ItemCooking.cs:96)
/// pulled to red; the end restores the item's own block, exactly what the game last set. A refused placement (a
/// piton where no wall is) gets a local ghost of the game's preview prefab. Property blocks and the ghost are
/// client-side only: other players never see them.
/// </summary>
internal static class DenyTint
{
    private static readonly int TintId = Shader.PropertyToID("_Tint");

    private static readonly List<Renderer> Targets = new(16);
    private static readonly List<System.Numerics.Vector4> Bases = new(16);
    private static MaterialPropertyBlock block;

    /// <summary>Item.mpb (Item.cs:207), resolved by <see cref="AffordanceFeature"/>.</summary>
    internal static HarmonyLib.AccessTools.FieldRef<Item, MaterialPropertyBlock> ItemBlock;

    private static Item item;
    private static GameObject ghost;
    private static GameObject ghostPrefab;

    public static bool Active => Targets.Count > 0;

    /// <summary>The renderers tinted now, for the self-test (do not modify).</summary>
    public static List<Renderer> Tinted => Targets;

    /// <summary>Tint <paramref name="target"/>'s renderers, the same set the game highlights.</summary>
    public static void HeldItem(Item target)
    {
        Clear();
        if (target == null || target.mainRenderer == null)
            return;
        item = target;
        Add(target.mainRenderer);
        if (target.addtlRenderers != null)
        {
            for (int i = 0; i < target.addtlRenderers.Length; i++)
                Add(target.addtlRenderers[i]);
        }
    }

    /// <summary>A red ghost of <paramref name="prefab"/> (a placement preview) where the refused placement was.</summary>
    public static void Ghost(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        Clear();
        if (prefab == null)
            return;
        if (ghostPrefab != prefab)
        {
            if (ghost != null)
                Object.Destroy(ghost);
            ghost = Object.Instantiate(prefab);
            ghost.name = "PeakXR_DenyGhost";
            foreach (var col in ghost.GetComponentsInChildren<Collider>(true))
                col.enabled = false;
            ghostPrefab = prefab;
        }
        ghost.transform.SetPositionAndRotation(position, rotation);
        ghost.SetActive(true);
        foreach (var r in ghost.GetComponentsInChildren<Renderer>())
            Add(r);
    }

    /// <summary>Per frame while the refusal shows: red at <paramref name="strength"/> (the cue's opacity, so it fades
    /// with it, never a flash), the game's highlight on.</summary>
    public static void Apply(float strength)
    {
        block ??= new MaterialPropertyBlock();
        for (int i = 0; i < Targets.Count; i++)
        {
            var r = Targets[i];
            if (r == null)
                continue;
            r.GetPropertyBlock(block);
            block.SetFloat(global::Item.PROPERTY_INTERACTABLE, 1f);
            var c = DenyTintMath.Tint(Bases[i], strength);
            block.SetColor(TintId, new Color(c.X, c.Y, c.Z, c.W));
            r.SetPropertyBlock(block);
        }
    }

    /// <summary>Ends the preview: the item gets the game's own block back, the ghost hides.</summary>
    public static void Clear()
    {
        if (item != null)
        {
            var own = ItemBlock != null ? ItemBlock(item) : null;
            for (int i = 0; i < Targets.Count; i++)
            {
                if (Targets[i] != null)
                    Targets[i].SetPropertyBlock(own);
            }
        }
        item = null;
        if (ghost != null)
            ghost.SetActive(false);
        Targets.Clear();
        Bases.Clear();
    }

    /// <summary>The renderer carries the full red tint now.</summary>
    public static bool IsTinted(Renderer r, int index)
    {
        if (r == null || index < 0 || index >= Bases.Count)
            return false;
        block ??= new MaterialPropertyBlock();
        r.GetPropertyBlock(block);
        var c = block.GetColor(TintId);
        return block.GetFloat(global::Item.PROPERTY_INTERACTABLE) > 0.5f
            && DenyTintMath.IsTinted(Bases[index], new System.Numerics.Vector4(c.r, c.g, c.b, c.a));
    }

    /// <summary>The renderer shows no red tint: gone, hidden, or its block holds no <c>_Tint</c> override.</summary>
    public static bool IsClean(Renderer r)
    {
        if (r == null || !r.gameObject.activeInHierarchy)
            return true;
        block ??= new MaterialPropertyBlock();
        r.GetPropertyBlock(block);
        return !block.HasColor(TintId);
    }

    private static void Add(Renderer r)
    {
        if (r == null)
            return;
        var m = r.sharedMaterial;
        var c = m != null && m.HasProperty(TintId) ? m.GetColor(TintId) : Color.white;
        Targets.Add(r);
        Bases.Add(new System.Numerics.Vector4(c.r, c.g, c.b, c.a));
    }
}
