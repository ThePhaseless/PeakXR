using PeakXR.UI.Common;
using PeakXR.UI.World;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.UI.Status;

/// <summary>
/// A9: the double-jump amulet's charges as small gold beads on the back of the wrist. One bead per charge the
/// amulet grants (<c>CharacterAfflictions.totalExtraJumps</c>, CharacterAfflictions.cs:190), lit while that air
/// jump is still left (<c>CharacterData.extraJumps</c>, CharacterData.cs:248; refilled on landing,
/// CharacterMovement.cs:748). No text; nothing shows without an amulet.
/// </summary>
internal sealed class AmuletPips
{
    private const int MaxPips = 5;
    private const float Size = 0.014f;
    private const float Spacing = 0.022f;

    private static readonly Color Lit = new(1f, 0.8f, 0.3f, 0.95f);
    private static readonly Color Spent = new(1f, 0.8f, 0.3f, 0.2f);

    private readonly Transform[] pips = new Transform[MaxPips];
    private readonly Material[] materials = new Material[MaxPips];
    private Transform root;
    private int shownTotal = -1;
    private int shownLit = -1;

    /// <summary>Beads shown and lit (self-test).</summary>
    internal static int Shown { get; private set; }

    internal static int LitCount { get; private set; }

    public void Update(VRRig rig)
    {
        var c = Character.localCharacter;
        var cfg = UIWorldFeature.Settings;
        var hand = rig != null && cfg != null ? rig.Hand(cfg.WristLeft) : null;
        int total =
            VRSession.Active && c != null && hand != null && !c.data.dead
                ? Mathf.Min(c.refs.afflictions.totalExtraJumps, MaxPips)
                : 0;
        int lit = total > 0 ? Mathf.Clamp(c.data.extraJumps, 0, total) : 0;
        if (total == shownTotal && lit == shownLit && (total == 0 || root != null))
            return;
        shownTotal = total;
        shownLit = lit;
        Shown = total;
        LitCount = lit;
        if (total == 0)
        {
            if (root != null)
                root.gameObject.SetActive(false);
            return;
        }
        Ensure(hand);
        if (root.parent != hand)
            root.SetParent(hand, false);
        root.gameObject.SetActive(true);
        for (int i = 0; i < MaxPips; i++)
        {
            bool on = i < total;
            if (pips[i].gameObject.activeSelf != on)
                pips[i].gameObject.SetActive(on);
            materials[i].color = i < lit ? Lit : Spent;
        }
    }

    private void Ensure(Transform hand)
    {
        if (root != null)
            return;
        // The hand (and the beads on it) went with a scene: drop the old materials too.
        for (int i = 0; i < MaxPips; i++)
        {
            if (materials[i] != null)
                Object.Destroy(materials[i]);
        }
        root = new GameObject("PeakXR_AmuletPips").transform;
        root.SetParent(hand, false);
        // Along the back of the wrist, just above the controller ring.
        root.localPosition = new Vector3(0f, 0.04f, -0.07f);
        var mesh = UIMaterials.SphereMesh();
        for (int i = 0; i < MaxPips; i++)
        {
            materials[i] = UIMaterials.Create(Lit, drawOnTop: false, renderQueue: 3000);
            var go = UIMaterials.MeshObject("Pip", mesh, materials[i], root);
            go.transform.localPosition = new Vector3((i - (MaxPips - 1) * 0.5f) * Spacing, 0f, 0f);
            go.transform.localScale = Vector3.one * Size;
            pips[i] = go.transform;
        }
    }

    public void Destroy()
    {
        if (root != null)
            Object.Destroy(root.gameObject);
        for (int i = 0; i < MaxPips; i++)
        {
            if (materials[i] != null)
                Object.Destroy(materials[i]);
            materials[i] = null;
        }
        root = null;
        shownTotal = shownLit = -1;
    }
}
