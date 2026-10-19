using System.Collections.Generic;
using PeakXR.Core;
using UnityEngine;

namespace PeakXR.Avatar;

/// <summary>
/// Hold profile (handedness, grip preset) and grip points of a held item. The profile comes from
/// <see cref="ItemHoldTable"/>: known names first, else traits read from the item's components and size, so new
/// items from future game versions still get a sensible profile. Profiles are cached per item id; grip
/// transforms per instance.
/// </summary>
internal sealed class ItemHoldInfo
{
    public HoldProfile Profile;
    public bool Edible;
    /// <summary>Item children named <c>Hand_R</c>/<c>Hand_L</c>: where vanilla puts the hands
    /// (<c>CharacterItems.GetItemPosRight</c>, CharacterItems.cs:879).</summary>
    public Transform GripRight;
    public Transform GripLeft;

    private static readonly Dictionary<ushort, (HoldProfile profile, bool edible)> byId = new();

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
            var traits = Traits(target, out entry.edible);
            entry.profile = ItemHoldTable.Classify(target.UIData?.itemName + " " + target.name, traits);
            byId[target.itemID] = entry;
            Plugin.Log.LogDebug($"[avatar] hold profile {target.name}: {entry.profile} ({traits})");
        }
        Profile = entry.profile;
        Edible = entry.edible;
        return true;
    }

    private static ItemTraits Traits(Item item, out bool edible)
    {
        var traits = ItemTraits.None;
        if (item.rightHandOnly)
            traits |= ItemTraits.RightHandOnly;
        edible = item.GetComponent<Action_Consume>() != null || item.GetComponent<Action_RestoreHunger>() != null;
        if (edible)
            traits |= ItemTraits.Food;
        if (item.GetComponent<RopeShooter>() != null || item.GetComponent<VineShooter>() != null ||
            item.GetComponent<Action_RaycastDart>() != null)
            traits |= ItemTraits.Shooter;
        if (item is Backpack)
            traits |= ItemTraits.Backpack;

        // Size from render meshes in the item's frame (colliders are disabled while the item is being equipped).
        var bounds = new Bounds();
        bool any = false;
        foreach (var filter in item.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null)
                continue;
            var b = filter.sharedMesh.bounds;
            var scale = filter.transform.lossyScale;
            var size = Vector3.Scale(b.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            var center = item.transform.InverseTransformPoint(filter.transform.TransformPoint(b.center));
            var local = new Bounds(center, size);
            if (any)
                bounds.Encapsulate(local);
            else
                bounds = local;
            any = true;
        }
        if (any)
        {
            var s = bounds.size;
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
