using PeakXR.Core;
using PeakXR.UI.Common;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Inventory;

/// <summary>
/// Faint spheres at the body slots, shown only while a hand comes near one so the view stays clear. Children of
/// <see cref="VRRig.Origin"/>, placed at the tracking-space anchors of <see cref="BodyInventory"/>.
/// </summary>
internal sealed class SlotGhosts : MonoBehaviour
{
    private static readonly Color EmptyColor = new(0.8f, 0.8f, 0.8f, 1f);
    private static readonly Color FilledColor = new(1f, 0.95f, 0.75f, 1f);
    private static readonly Color SelectedColor = new(0.45f, 0.85f, 1f, 1f);
    private static readonly Color FullColor = new(1f, 0.25f, 0.2f, 1f);

    /// <summary>Peak opacity of a marker with a hand inside it.</summary>
    private const float MaxAlpha = 0.35f;

    /// <summary>Markers fade in from this many slot radii away.</summary>
    private const float FadeRadii = 2.5f;

    private static SlotGhosts instance;

    private readonly Transform[] markers = new Transform[BodyInventory.SlotCount];
    private readonly Renderer[] renderers = new Renderer[BodyInventory.SlotCount];
    private readonly Material[] materials = new Material[BodyInventory.SlotCount];

    /// <summary>Creates the markers under the rig origin once it exists; cheap to call every frame.</summary>
    public static void Ensure()
    {
        if (instance != null)
            return;
        var rig = VRRig.Instance;
        if (rig == null || rig.Origin == null)
            return;
        var go = new GameObject("PeakXR.SlotGhosts");
        go.transform.SetParent(rig.Origin, false);
        instance = go.AddComponent<SlotGhosts>();
    }

    private void Awake()
    {
        var mesh = UIMaterials.SphereMesh();
        for (int i = 0; i < markers.Length; i++)
        {
            materials[i] = UIMaterials.Create(EmptyColor, false, 3000);
            var go = UIMaterials.MeshObject($"Slot{i}", mesh, materials[i], transform);
            markers[i] = go.transform;
            renderers[i] = go.GetComponent<Renderer>();
            renderers[i].enabled = false;
        }
    }

    private void OnDestroy()
    {
        for (int i = 0; i < materials.Length; i++)
        {
            if (materials[i] != null)
                Destroy(materials[i]);
        }
        if (instance == this)
            instance = null;
    }

    private void LateUpdate()
    {
        var inv = BodyInventory.Instance;
        var c = Character.localCharacter;
        bool show =
            inv.Valid
            && InventoryConfig.Instance.Ghosts
            && VRSession.Active
            && c != null
            && c.data.fullyConscious;
        for (int i = 0; i < markers.Length; i++)
        {
            var look = show ? inv.Look(c, i) : SlotLook.Hidden;
            if (look == SlotLook.Hidden)
            {
                renderers[i].enabled = false;
                continue;
            }
            var slot = inv.Slot(i);
            float alpha = BodySlotLayout.GhostAlpha(
                inv.NearestHandDistance(i),
                slot.Radius,
                slot.Radius * FadeRadii
            );
            if (alpha <= 0f)
            {
                renderers[i].enabled = false;
                continue;
            }
            var color = look switch
            {
                SlotLook.Filled => FilledColor,
                SlotLook.Selected => SelectedColor,
                SlotLook.Full => FullColor,
                _ => EmptyColor,
            };
            color.a = alpha * MaxAlpha;
            materials[i].color = color;
            markers[i].localPosition = inv.Anchor(i);
            markers[i].localScale = Vector3.one * slot.Radius;
            renderers[i].enabled = true;
        }
    }
}
