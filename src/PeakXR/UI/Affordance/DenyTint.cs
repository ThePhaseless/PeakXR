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
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private static readonly List<Renderer> Targets = new(16);
    private static readonly List<System.Numerics.Vector4> Bases = new(16);

    /// <summary>Per target: the colour it is tinted through, <c>_Tint</c> (items, most world props), <c>_Color0</c>
    /// (a preview ghost) or <c>_BaseColor</c> (URP shaders without <c>_Tint</c>).</summary>
    private static readonly List<int> Props = new(16);

    /// <summary>Per target that is not the item (world props, the ghost): its block before the tint, restored at the
    /// end; null when it had none. Pooled blocks, so a refusal allocates only the first time.</summary>
    private static readonly List<MaterialPropertyBlock> Saved = new(16);
    private static readonly List<MaterialPropertyBlock> SavedPool = new(16);
    private static readonly List<Renderer> Scratch = new(16);

    /// <summary>UI parts tinted (the wrist stamina bar, a button), their colour before and the colour last written.</summary>
    private static readonly List<UnityEngine.UI.Graphic> GraphicTargets = new(4);
    private static readonly List<Color> GraphicBases = new(4);
    private static readonly List<Color> GraphicWritten = new(4);
    private static MaterialPropertyBlock block;

    /// <summary>Item.mpb (Item.cs:207), resolved by <see cref="AffordanceFeature"/>.</summary>
    internal static HarmonyLib.AccessTools.FieldRef<Item, MaterialPropertyBlock> ItemBlock;

    private static Item item;

    /// <summary>This refusal's tint read-back was logged (<see cref="LogReadBack"/>).</summary>
    private static bool logged;
    private static GameObject ghost;
    private static GameObject ghostPrefab;

    public static bool Active => Targets.Count > 0 || GraphicTargets.Count > 0;

    /// <summary>
    /// The red preview on <paramref name="target"/>: an <see cref="Item"/> through the game's highlight
    /// (<see cref="HeldItem"/>), a UI <see cref="UnityEngine.UI.Graphic"/> by its colour (<see cref="Graphic"/>),
    /// anything else by its renderers (<see cref="Renderers"/>). A part of a character's body is never tinted
    /// (decision D10: a refusal about another player shows the ring only). Null clears.
    /// </summary>
    public static void Target(Component target)
    {
        switch (target)
        {
            case null:
                Clear();
                break;
            case Item i:
                HeldItem(i);
                break;
            case UnityEngine.UI.Graphic g:
                Graphic(g);
                break;
            default:
                Renderers(target);
                break;
        }
    }

    /// <summary>Tint every renderer under <paramref name="root"/> (a world prop: a luggage lid, campfire logs, a
    /// rope). Allocation-free after the first refusal. Skips anything on a character (D10).</summary>
    public static void Renderers(Component root)
    {
        Clear();
        if (root == null || root.GetComponentInParent<Character>() != null)
            return;
        root.GetComponentsInChildren(Scratch);
        for (int i = 0; i < Scratch.Count; i++)
            Add(Scratch[i]);
        Scratch.Clear();
    }

    /// <summary>Tint a UI part (the wrist stamina bar's backing, a menu control) toward red by its colour.</summary>
    public static void Graphic(UnityEngine.UI.Graphic target)
    {
        Clear();
        if (target == null)
            return;
        GraphicTargets.Add(target);
        GraphicBases.Add(target.color);
        GraphicWritten.Add(target.color);
    }

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
                prop == PreviewColorId
                    ? DenyTintMath.Preview(Bases[i], strength)
                    : DenyTintMath.Tint(Bases[i], strength);
            var set = new Color(c.X, c.Y, c.Z, c.W);
            block.SetColor(prop, set);
            r.SetPropertyBlock(block);
            if (!logged && strength >= 1f)
                LogReadBack(r, i, set);
        }
        for (int i = 0; i < GraphicTargets.Count; i++)
        {
            var g = GraphicTargets[i];
            if (g == null)
                continue;
            var b = GraphicBases[i];
            var c = DenyTintMath.Flat(new System.Numerics.Vector4(b.r, b.g, b.b, b.a), strength);
            var set = new Color(c.X, c.Y, c.Z, c.W);
            g.color = set;
            GraphicWritten[i] = set;
        }
        logged |= strength >= 1f;
    }

    /// <summary>Once per refusal at full strength: what was written and what the renderer's block reads back in
    /// the same frame, so a failing vr.6x check shows whether the tint landed or only its read-back differs.</summary>
    private static void LogReadBack(Renderer r, int i, Color set)
    {
        r.GetPropertyBlock(block);
        int prop = Props[i];
        var back = block.GetColor(prop);
        Plugin.Log.LogInfo(
            $"[ui.affordance] tint {r.name} {(prop == TintId ? "_Tint" : prop == PreviewColorId ? "_Color0" : "_BaseColor")}: base {Bases[i]}, wrote {set}, "
                + $"read {back} (as gamma {back.gamma}), _Interactable {block.GetFloat(global::Item.PROPERTY_INTERACTABLE):0.##}, "
                + $"block stores linear {StoresLinear()}, tinted {IsTinted(r, i)}, "
                + $"gap to target {TargetGap(i, back):0.###}"
        );
    }

    /// <summary>Ends the preview: the item gets the game's own block back, the ghost hides.</summary>
    public static void Clear()
    {
        var own = item != null && ItemBlock != null ? ItemBlock(item) : null;
        for (int i = 0; i < Targets.Count; i++)
        {
            if (Targets[i] != null)
                Targets[i].SetPropertyBlock(item != null ? own : Saved[i]);
            if (Saved[i] != null)
                SavedPool.Add(Saved[i]);
        }
        for (int i = 0; i < GraphicTargets.Count; i++)
        {
            // Only our own colour goes back: if the game recoloured it meanwhile, its colour stays.
            var g = GraphicTargets[i];
            if (g != null && g.color == GraphicWritten[i])
                g.color = GraphicBases[i];
        }
        GraphicTargets.Clear();
        GraphicBases.Clear();
        GraphicWritten.Clear();
        item = null;
        logged = false;
        if (ghost != null)
            ghost.SetActive(false);
        Targets.Clear();
        Bases.Clear();
        Props.Clear();
        Saved.Clear();
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

    private static float TargetGap(int i, Color c)
    {
        var now = new System.Numerics.Vector4(c.r, c.g, c.b, c.a);
        var target =
            Props[i] == PreviewColorId
                ? DenyTintMath.Preview(Bases[i], 1f)
                : DenyTintMath.Tint(Bases[i], 1f);
        return DenyTintMath.Gap(now, target);
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
        // SetColor stores the colour converted to linear (linear colour space) and GetColor hands that back
        // unconverted: compare in the gamma space it was set in, or no value ever matches.
        if (StoresLinear())
            c = c.gamma;
        var now = new System.Numerics.Vector4(c.r, c.g, c.b, c.a);
        if (prop == PreviewColorId)
            return DenyTintMath.IsPreview(Bases[index], now);
        return block.GetFloat(global::Item.PROPERTY_INTERACTABLE) > 0.5f
            && DenyTintMath.IsTinted(Bases[index], now);
    }

    private static int storesLinear = -1;

    /// <summary>Probed once: does a property block read a colour back as linear?</summary>
    private static bool StoresLinear()
    {
        if (storesLinear < 0)
        {
            var probe = new MaterialPropertyBlock();
            probe.SetColor(TintId, new Color(0.5f, 0.5f, 0.5f, 1f));
            storesLinear = Mathf.Abs(probe.GetColor(TintId).r - 0.5f) > 0.01f ? 1 : 0;
        }
        return storesLinear == 1;
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
        // _Tint where the shader has it (W/Peak_Standard, W/Character); else the preview shader's _Color0; else
        // URP's _BaseColor. A shader with none of them still gets _Tint written (harmless) and the read-back log.
        int prop =
            m == null || m.HasProperty(TintId) ? TintId
            : m.HasProperty(PreviewColorId) ? PreviewColorId
            : m.HasProperty(BaseColorId) ? BaseColorId
            : TintId;
        var c = m != null && m.HasProperty(prop) ? m.GetColor(prop) : Color.white;
        MaterialPropertyBlock saved = null;
        if (item == null && r.HasPropertyBlock())
        {
            int last = SavedPool.Count - 1;
            if (last >= 0)
            {
                saved = SavedPool[last];
                SavedPool.RemoveAt(last);
            }
            else
                saved = new MaterialPropertyBlock();
            r.GetPropertyBlock(saved);
        }
        Targets.Add(r);
        Props.Add(prop);
        Bases.Add(new System.Numerics.Vector4(c.r, c.g, c.b, c.a));
        Saved.Add(saved);
    }
}
