using PeakXR.Core;
using PeakXR.UI.Common;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakXR.Inventory;

/// <summary>
/// The hotbar drawn on the body, replacing the game's 2D hotbar in VR (<see cref="HotbarHider"/>). Each belt slot
/// is a ring with its item's icon on top, tilted up toward the eyes so it reads at a glance down; the ring lights
/// up while a hand is in the slot. The shoulder (backpack) anchors show only while a hand comes near, since you
/// cannot see them. Children of <see cref="VRRig.Origin"/>, placed at the tracking-space anchors of
/// <see cref="BodyInventory"/>. Icons change only when a slot's item does.
/// </summary>
internal sealed class SlotGhosts : MonoBehaviour
{
    private static readonly Color EmptyColor = new(0.85f, 0.85f, 0.85f, 1f);
    private static readonly Color FilledColor = new(1f, 0.95f, 0.75f, 1f);
    private static readonly Color SelectedColor = new(0.45f, 0.85f, 1f, 1f);
    private static readonly Color FullColor = new(1f, 0.25f, 0.2f, 1f);

    /// <summary>Ring opacity at rest, and added while a hand is in the slot.</summary>
    private const float RingAlpha = 0.3f;
    private const float HoverAlpha = 0.45f;

    /// <summary>Ring and icon size as a fraction of the slot's catch diameter: smaller than the catch area, so
    /// neighbours keep a clear gap between them.</summary>
    private const float RingSize = 0.65f;
    private const float IconSize = 0.5f;
    private const float HoverScale = 1.15f;

    /// <summary>Faces tilt back from flat by this much (degrees), toward the eyes above and behind them.</summary>
    private const float Tilt = 60f;

    /// <summary>Shoulder markers fade in from this many slot radii away.</summary>
    private const float FadeRadii = 2.5f;

    private const int Max = BodyInventory.MaxSlots;
    private static SlotGhosts instance;
    private static Texture2D ringTexture;

    private readonly Transform[] roots = new Transform[Max];
    private readonly Renderer[] rings = new Renderer[Max];
    private readonly Renderer[] icons = new Renderer[Max];
    private readonly Material[] ringMats = new Material[Max];
    private readonly Material[] iconMats = new Material[Max];
    private readonly Item[] shown = new Item[Max];

    /// <summary>Creates the slots under the rig origin once it exists; cheap to call every frame.</summary>
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

    /// <summary>The body hotbar is drawn this frame (settings on, slots built, anchors tracked), so the 2D one can go.</summary>
    public static bool Showing =>
        InventoryConfig.Instance.BodySlots
        && InventoryConfig.Instance.Ghosts
        && instance != null
        && BodyInventory.Instance.Valid
        && VRSession.HasFocus;

    /// <summary>Self-test: slot <paramref name="i"/>'s ghost while it is drawn, else null.</summary>
    internal static Transform Drawn(int i) =>
        instance != null && i >= 0 && i < Max && instance.roots[i].gameObject.activeInHierarchy
            ? instance.roots[i]
            : null;

    /// <summary>Self-test: the rings skip the depth test, so no body mesh can hide them.</summary>
    internal static bool DrawnOnTop =>
        instance != null
        && instance.ringMats[0] != null
        && instance.ringMats[0].GetInt("unity_GUIZTestMode") == (int)CompareFunction.Always;

    private void Awake()
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var mesh = quad.GetComponent<MeshFilter>().sharedMesh;
        Destroy(quad);
        if (ringTexture == null)
            ringTexture = MakeRing(64);
        for (int i = 0; i < Max; i++)
        {
            var root = new GameObject($"Slot{i}").transform;
            root.SetParent(transform, false);
            roots[i] = root;
            ringMats[i] = TexturedMaterial(ringTexture, 3000);
            iconMats[i] = TexturedMaterial(null, 3001);
            var ring = UIMaterials.MeshObject("Ring", mesh, ringMats[i], root);
            ring.transform.localScale = new Vector3(RingSize, RingSize, 1f);
            rings[i] = ring.GetComponent<Renderer>();
            var icon = UIMaterials.MeshObject("Icon", mesh, iconMats[i], root);
            // The viewer is on the quad's -Z side: lift the icon off the ring toward them.
            icon.transform.localPosition = new Vector3(0f, 0f, -0.02f);
            icon.transform.localScale = new Vector3(IconSize, IconSize, 1f);
            icons[i] = icon.GetComponent<Renderer>();
            // Hand-UI order (OnTop's rule): after smoke, under the comfort overlays.
            rings[i].sortingOrder = icons[i].sortingOrder = DrawOrder.HandUi;
            root.gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        for (int i = 0; i < Max; i++)
        {
            if (ringMats[i] != null)
                Destroy(ringMats[i]);
            if (iconMats[i] != null)
                Destroy(iconMats[i]);
        }
        if (instance == this)
            instance = null;
    }

    private void LateUpdate()
    {
        var inv = BodyInventory.Instance;
        var c = Character.localCharacter;
        bool show = inv.Valid && Showing && VRSession.Active && c != null && c.data.fullyConscious;
        int count = show ? inv.Count : 0;
        for (int i = 0; i < Max; i++)
        {
            var look = i < count ? inv.Look(c, i) : SlotLook.Hidden;
            if (look == SlotLook.Hidden || !Pose(inv, c, i, look))
            {
                if (roots[i].gameObject.activeSelf)
                    roots[i].gameObject.SetActive(false);
                continue;
            }
            if (!roots[i].gameObject.activeSelf)
                roots[i].gameObject.SetActive(true);
        }
    }

    /// <summary>Places and colours one slot; false when it should not be drawn this frame.</summary>
    private bool Pose(BodyInventory inv, Character c, int i, SlotLook look)
    {
        var slot = inv.Slot(i);
        bool hovered = inv.Hovered(i);
        float alpha = RingAlpha + (hovered ? HoverAlpha : 0f);
        Item item = null;
        if (slot.GameSlot == BodySlotLayout.MaxHotbar)
        {
            // Shoulder anchor: behind you, so only a fading hint while a hand is near.
            alpha = BodySlotLayout.GhostAlpha(
                inv.NearestHandDistance(i),
                slot.Radius,
                slot.Radius * FadeRadii
            );
            if (alpha <= 0f)
                return false;
        }
        else
        {
            var itemSlot = c.player.GetItemSlot(slot.GameSlot);
            item = itemSlot != null && !itemSlot.IsEmpty() ? itemSlot.prefab : null;
        }

        var color = look switch
        {
            SlotLook.Filled => FilledColor,
            SlotLook.Selected => SelectedColor,
            SlotLook.Full => FullColor,
            _ => EmptyColor,
        };
        color.a = Mathf.Min(1f, alpha);
        ringMats[i].color = color;

        // GetIcon reads GUIManager settings (Item.cs:83); without a HUD, retry next frame.
        if (shown[i] != item && (item == null || GUIManager.instance != null))
        {
            shown[i] = item;
            iconMats[i].mainTexture = item != null ? item.UIData.GetIcon() : null;
        }
        bool hasIcon = item != null && iconMats[i].mainTexture != null;
        if (icons[i].enabled != hasIcon)
            icons[i].enabled = hasIcon;
        // The item that is out in a hand leaves a faded icon behind: its slot is still taken.
        if (hasIcon)
            iconMats[i].color = new Color(1f, 1f, 1f, look == SlotLook.Selected ? 0.35f : 0.95f);

        float size = slot.Radius * 2f * (hovered ? HoverScale : 1f);
        roots[i]
            .SetLocalPositionAndRotation(
                inv.Anchor(i),
                inv.Facing(i) * Quaternion.Euler(Tilt, 0f, 0f)
            );
        roots[i].localScale = new Vector3(size, size, size);
        return true;
    }

    /// <summary>Alpha-blended textured material on a shader every player build ships (as <c>CinemaScreen</c>). Drawn on
    /// top: the belt rides in front of the belly, and the visible VR body (round torso, straps, backpack) would
    /// otherwise hide the rings from the eyes, as it did in the headset.</summary>
    private static Material TexturedMaterial(Texture texture, int queue)
    {
        var shader = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");
        var material = new Material(shader) { mainTexture = texture, renderQueue = queue };
        material.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
        return material;
    }

    /// <summary>White ring with a faint filled middle, alpha in the texture so the material colour tints it.</summary>
    private static Texture2D MakeRing(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "PeakXR.SlotRing",
            wrapMode = TextureWrapMode.Clamp,
        };
        var pixels = new Color32[size * size];
        float half = size / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = new Vector2(x + 0.5f - half, y + 0.5f - half).magnitude / half;
                float ring = Mathf.Clamp01(1f - Mathf.Abs(d - 0.88f) / 0.08f);
                float fill = d < 0.82f ? 0.18f : 0f;
                pixels[y * size + x] = new Color32(
                    255,
                    255,
                    255,
                    (byte)(255 * Mathf.Max(ring, fill))
                );
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return tex;
    }
}
