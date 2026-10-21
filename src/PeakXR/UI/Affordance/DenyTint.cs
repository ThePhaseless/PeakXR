using System.Collections.Generic;
using PeakXR.Core;
using UnityEngine;

namespace PeakXR.UI.Affordances;

/// <summary>
/// The red preview of a refusal: the refused thing itself turns red while the refusal is up. A held item goes through
/// the game's own highlight path (<c>Item.HoverEnter</c>, Item.cs:1145-1153: a <see cref="MaterialPropertyBlock"/>
/// with <c>_Interactable</c> on <c>mainRenderer</c> and <c>addtlRenderers</c>) with its <c>_Tint</c> (ItemCooking.cs:96)
/// pulled to red; the end restores the item's own block, exactly what the game last set. A refused placement (a
/// piton where no wall is) gets a local ghost of the game's preview prefab (ClimbingSpikePreview: shader
/// <c>Scouts/ItemPreview</c>, no <c>_Tint</c>; its HDR blue <c>_Color0</c> turns red). Property blocks and the ghost are
/// client-side only: other players never see them.
/// </summary>
internal static class DenyTint
{
    private static readonly int TintId = Shader.PropertyToID("_Tint");
    private static readonly int PreviewColorId = Shader.PropertyToID("_Color0");

    private static readonly List<Renderer> Targets = new(16);
    private static readonly List<System.Numerics.Vector4> Bases = new(16);

    /// <summary>Per target: the colour it is tinted through, <c>_Tint</c> (items) or <c>_Color0</c> (a preview ghost).</summary>
    private static readonly List<int> Props = new(16);
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
        // The ghost dies with its scene while the prefab (an asset) stays the same: re-create it then too.
        if (ghost == null || ghostPrefab != prefab)
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
            int prop = Props[i];
            var c =
                prop == TintId
                    ? DenyTintMath.Tint(Bases[i], strength)
                    : DenyTintMath.Preview(Bases[i], strength);
            block.SetColor(prop, new Color(c.X, c.Y, c.Z, c.W));
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
        Props.Clear();
    }

    /// <summary>Clears the tint and destroys the ghost (scene unload, VR stop).</summary>
    public static void Dispose()
    {
        if (Active)
            Clear();
        if (ghost != null)
            Object.Destroy(ghost);
        ghost = null;
        ghostPrefab = null;
    }

    /// <summary>The renderer carries the full red tint now.</summary>
    public static bool IsTinted(Renderer r, int index)
    {
        if (r == null || index < 0 || index >= Bases.Count)
            return false;
        block ??= new MaterialPropertyBlock();
        r.GetPropertyBlock(block);
        int prop = Props[index];
        var c = block.GetColor(prop);
        var now = new System.Numerics.Vector4(c.r, c.g, c.b, c.a);
        if (prop != TintId)
            return DenyTintMath.IsPreview(Bases[index], now);
        return block.GetFloat(global::Item.PROPERTY_INTERACTABLE) > 0.5f
            && DenyTintMath.IsTinted(Bases[index], now);
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
        // _Tint where the shader has it (W/Peak_Standard, W/Character); else the preview shader's _Color0.
        int prop =
            m == null || m.HasProperty(TintId) || !m.HasProperty(PreviewColorId)
                ? TintId
                : PreviewColorId;
        var c = m != null && m.HasProperty(prop) ? m.GetColor(prop) : Color.white;
        Targets.Add(r);
        Props.Add(prop);
        Bases.Add(new System.Numerics.Vector4(c.r, c.g, c.b, c.a));
    }
}
