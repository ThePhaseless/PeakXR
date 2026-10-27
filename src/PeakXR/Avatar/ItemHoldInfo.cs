using System.Collections.Generic;
using PeakXR.Core;
using UnityEngine;

namespace PeakXR.Avatar;

/// <summary>
/// Hold profile (handedness, grip preset), shape and grip points of a held item. The profile comes from
/// <see cref="ItemHoldTable"/>: known names first, else traits read from the item's components and size, so new
/// items from future game versions still get a sensible profile. Profiles and shapes are cached per item id; grip
/// transforms per instance.
/// </summary>
internal sealed class ItemHoldInfo
{
    public HoldProfile Profile;
    public bool Edible;

    /// <summary>A launcher that fires along its own +Z barrel (the shooters HandAim.TryBarrel aims): two hands lay
    /// it along the two-hand axis (<see cref="GripMath.TwoHandAim"/>).</summary>
    public bool Aimed;

    /// <summary>The drawn meshes' bounds in the item's own frame (the grips' preview hands are inactive by then,
    /// HandVisual.cs:9). Zero size when the item draws nothing.</summary>
    public Vector3 BoundsMin;
    public Vector3 BoundsMax;

    /// <summary>Item children named <c>Hand_R</c>/<c>Hand_L</c>: where vanilla puts the hands
    /// (<c>CharacterItems.GetItemPosRight</c>, CharacterItems.cs:879).</summary>
    public Transform GripRight;
    public Transform GripLeft;

    /// <summary>
    /// World position of the secondary hand's grip on the bound item: <c>Hand_L</c> for a right-primary hold, its
    /// mirror across the item's local X for a left-primary one (<see cref="HoldHands.ItemLocal"/>), since the left
    /// hold is the mirror image of the right, where it is drawn (<see cref="HeldItemScale.ToVisual"/>). False when
    /// the item has no <c>Hand_L</c>.
    /// </summary>
    public bool SecondGrip(bool primaryLeft, out Vector3 world)
    {
        world = default;
        if (item == null || GripLeft == null)
            return false;
        var local = item.transform.InverseTransformPoint(GripLeft.position);
        world = HeldItemScale.ToVisual(
            item,
            item.transform.TransformPoint(HoldHands.ItemLocal(local.N(), primaryLeft).U())
        );
        return true;
    }

    /// <summary>The grip the holding hand takes (<c>Hand_R</c>, else <c>Hand_L</c>; mirrored for the left hand),
    /// in the item frame: the pivot the item is drawn smaller about.</summary>
    public Vector3 GripLocal =>
        GripRight != null ? GripRight.localPosition
        : GripLeft != null ? GripLeft.localPosition
        : Vector3.zero;

    /// <summary>The item's thinnest side, metres: what the fingers close round.</summary>
    public float Thickness
    {
        get
        {
            var s = BoundsMax - BoundsMin;
            return Mathf.Min(s.x, Mathf.Min(s.y, s.z));
        }
    }

    private struct Entry
    {
        public HoldProfile Profile;
        public bool Edible;
        public bool Aimed;
        public Vector3 Min;
        public Vector3 Max;
    }

    private static readonly Dictionary<ushort, Entry> byId = new();
    private static readonly List<MeshFilter> filters = new();
    private static readonly List<SkinnedMeshRenderer> skins = new();

    private Item item;

    /// <summary>Refreshes this cache entry for <paramref name="target"/>; cheap when the item is unchanged.</summary>
    public bool Bind(Item target)
    {
        if (target == null)
        {
            item = null;
            return false;
        }
        if (target == item)
            return true;

        item = target;
        GripRight = target.transform.Find("Hand_R");
        GripLeft = target.transform.Find("Hand_L");
        if (!byId.TryGetValue(target.itemID, out var entry))
        {
            bool any = LocalBounds(target, out entry.Min, out entry.Max);
            var traits = Traits(target, any, entry.Max - entry.Min, out entry.Edible);
            entry.Aimed = (traits & ItemTraits.Shooter) != 0;
            entry.Profile = ItemHoldTable.Classify(
                target.UIData?.itemName + " " + target.name,
                traits
            );
            byId[target.itemID] = entry;
            Plugin.Log.LogDebug(
                $"[avatar] hold profile {target.name}: {entry.Profile} ({traits}), size {entry.Max - entry.Min}"
            );
        }
        Profile = entry.Profile;
        Edible = entry.Edible;
        Aimed = entry.Aimed;
        BoundsMin = entry.Min;
        BoundsMax = entry.Max;
        return true;
    }

    /// <summary>Bounds of the active meshes in the item's frame, every corner carried through its own transform: a
    /// mesh turned on its child (a bottle lying in the prefab) keeps its true extents.</summary>
    private static bool LocalBounds(Item item, out Vector3 min, out Vector3 max)
    {
        min = Vector3.positiveInfinity;
        max = Vector3.negativeInfinity;
        var toItem = item.transform.worldToLocalMatrix;
        item.GetComponentsInChildren(false, filters);
        foreach (var filter in filters)
        {
            if (filter.sharedMesh != null)
                Add(
                    toItem * filter.transform.localToWorldMatrix,
                    filter.sharedMesh.bounds,
                    ref min,
                    ref max
                );
        }
        item.GetComponentsInChildren(false, skins);
        foreach (var skin in skins)
        {
            // Skinned bounds are in the root bone's frame (the renderer's own without one).
            var frame = skin.rootBone != null ? skin.rootBone : skin.transform;
            Add(toItem * frame.localToWorldMatrix, skin.localBounds, ref min, ref max);
        }
        filters.Clear();
        skins.Clear();
        if (min.x <= max.x)
            return true;
        min = max = Vector3.zero;
        return false;
    }

    private static void Add(Matrix4x4 m, Bounds b, ref Vector3 min, ref Vector3 max)
    {
        var c = b.center;
        var e = b.extents;
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                c.x + ((i & 1) == 0 ? -e.x : e.x),
                c.y + ((i & 2) == 0 ? -e.y : e.y),
                c.z + ((i & 4) == 0 ? -e.z : e.z)
            );
            var p = m.MultiplyPoint3x4(corner);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
    }

    private static ItemTraits Traits(Item item, bool measured, Vector3 s, out bool edible)
    {
        var traits = ItemTraits.None;
        if (item.rightHandOnly)
            traits |= ItemTraits.RightHandOnly;
        edible =
            item.GetComponent<Action_Consume>() != null
            || item.GetComponent<Action_RestoreHunger>() != null;
        if (edible)
            traits |= ItemTraits.Food;
        // The launchers HandAim.TryBarrel fires along the item's +Z (RopeShooter.cs:15, Action_RaycastDart.cs:14,
        // Peak/Action_RaycastSpawnSomething.cs:8, RescueHook.cs:79, VineShooter).
        if (
            item.GetComponent<RopeShooter>() != null
            || item.GetComponent<VineShooter>() != null
            || item.GetComponent<Action_RaycastDart>() != null
            || item.GetComponent<RescueHook>() != null
            || item.GetComponent<global::Peak.Action_RaycastSpawnSomething>() != null
        )
            traits |= ItemTraits.Shooter;
        if (item is Backpack)
            traits |= ItemTraits.Backpack;

        if (measured)
        {
            float max = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
            float min = Mathf.Min(s.x, Mathf.Min(s.y, s.z));
            if (max >= 0.9f)
                traits |= ItemTraits.Elongated;
            else if (max < 0.15f)
                traits |= ItemTraits.Small;
            if (min < 0.05f && min < 0.25f * max)
                traits |= ItemTraits.Flat;
        }
        return traits;
    }
}
