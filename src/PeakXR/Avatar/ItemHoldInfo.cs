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
    /// World position of the secondary hand's grip on the bound item: <c>Hand_L</c> for a right-primary hold, where
    /// the left hold puts it for a left-primary one (<see cref="HoldLocal"/>), where it is drawn
    /// (<see cref="HeldItemScale.ToVisual"/>). False when the item has no <c>Hand_L</c>.
    /// </summary>
    public bool SecondGrip(bool primaryLeft, out Vector3 world)
    {
        world = default;
        if (item == null || GripLeft == null)
            return false;
        var local = item.transform.InverseTransformPoint(GripLeft.position);
        world = HeldItemScale.ToVisual(
            item,
            item.transform.TransformPoint(HoldLocal(local, primaryLeft, primaryGrip: false))
        );
        return true;
    }

    /// <summary>
    /// A grip point of the item as the game authored it for a right-primary hold (<c>Hand_R</c> for the holding hand,
    /// <c>Hand_L</c> for the second) where this hold has it: reflected across the item's left-hold plane and shifted
    /// back onto the item for a left-primary hold (<see cref="LeftHold"/>).
    /// </summary>
    public Vector3 HoldLocal(Vector3 gameLocal, bool primaryLeft, bool primaryGrip)
    {
        if (!primaryLeft || item == null)
            return gameLocal;
        ref readonly var left = ref Left();
        return LeftHold.Reflect(gameLocal.N(), left.Plane, left.Centre.N()).U()
            + (primaryGrip ? left.Shift : left.SecondShift);
    }

    /// <summary>How far this hold's palm (the holding hand's with <paramref name="primaryGrip"/>, else the second
    /// hand's) is from the item's colliders, metres; NaN without that grip or an enabled collider. Self-test vr.244.
    /// </summary>
    internal float HoldGap(bool primaryLeft, bool primaryGrip)
    {
        var grip = primaryGrip ? GripRight : GripLeft;
        if (item == null || grip == null)
            return float.NaN;
        float gap = Gap(HoldLocal(Palm(grip), primaryLeft, primaryGrip), out _);
        return float.IsInfinity(gap) ? float.NaN : gap;
    }

    /// <summary>The plane the bound item's left hold mirrors across (self-test notes).</summary>
    internal LeftPlane LeftPlaneUsed => item != null ? Left().Plane : LeftPlane.Origin;

    /// <summary>The left hold turns the bound item's far end round: only across a plane the item is even about, where
    /// that end looks the same as the other (self-test vr.244 skips its far-end check then).</summary>
    internal bool LeftTurnsEnd
    {
        get
        {
            if (item == null || GripRight == null)
                return false;
            ref readonly var left = ref Left();
            var grip = GripRight.localPosition.N();
            return LeftHold.TurnsEnd(
                left.Plane,
                left.Centre.N(),
                grip,
                HeldItemShape.FarEnd(BoundsMin.N(), BoundsMax.N(), grip)
            );
        }
    }

    /// <summary>The holding hand's grip in the item frame for this hold: the pivot the item is drawn smaller
    /// about.</summary>
    public Vector3 HoldPivot(bool primaryLeft) =>
        HoldLocal(GripLocal, primaryLeft, primaryGrip: true);

    /// <summary>The item-frame pose that follows the plain mirror of the right hold for a left-primary hold
    /// (<see cref="LeftHold.Correction"/>).</summary>
    public void LeftCorrection(out Vector3 pos, out Quaternion rot)
    {
        ref readonly var left = ref Left();
        LeftHold.Correction(left.Plane, left.Centre.N(), left.Shift.N(), out var p, out var r);
        pos = p.U();
        rot = r.U();
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
        public LeftFit Left;
    }

    /// <summary>The left-primary hold of an item id, measured once its colliders are on.</summary>
    private struct LeftFit
    {
        public bool Ready;
        public LeftPlane Plane;
        public Vector3 Centre;
        public Vector3 Shift;
        public Vector3 SecondShift;
    }

    private static readonly LeftFit PlainMirror;

    private static readonly Dictionary<ushort, Entry> byId = new();
    private static readonly List<MeshFilter> filters = new();
    private static readonly List<SkinnedMeshRenderer> skins = new();

    private Item item;

    private static readonly System.Numerics.Vector3[] probes = new System.Numerics.Vector3[
        LeftHold.ProbeCount
    ];
    private static readonly float[] probeGaps = new float[LeftHold.ProbeCount];
    private static readonly float[] reflectedGaps = new float[LeftHold.ProbeCount];

    /// <summary>The bound item's left hold: measured on the first call its colliders allow (Item.SetState makes them
    /// solid while held, Item.cs:779), cached per item id; the plain mirror until then.</summary>
    private ref readonly LeftFit Left()
    {
        if (!byId.TryGetValue(item.itemID, out var entry))
            return ref PlainMirror;
        if (!entry.Left.Ready && MeasureLeft(ref entry.Left))
        {
            byId[item.itemID] = entry;
            Plugin.Log.LogDebug(
                $"[avatar] left hold {item.name}: mirror {entry.Left.Plane}, shift {entry.Left.Shift.magnitude:0.000} m, second {entry.Left.SecondShift.magnitude:0.000} m"
            );
        }
        leftFit = entry.Left;
        return ref leftFit;
    }

    private LeftFit leftFit;

    /// <summary>
    /// Fits the left hold to the item's colliders (the drawn meshes are not readable at runtime; the colliders of
    /// most items are their own convex meshes): the plane by <see cref="LeftHold.Choose"/>, then the shifts that put
    /// the left palm and the second hand's palm back as near the item as in the right hold. Palms are the centres of
    /// the game's preview hands under each grip (HandVisual.cs:9 only hides them). False while no collider is on.
    /// </summary>
    private bool MeasureLeft(ref LeftFit fit)
    {
        var cols = item.colliders;
        bool any = false;
        if (cols != null)
        {
            foreach (var col in cols)
                any |= col != null && col.enabled && col.gameObject.activeInHierarchy;
        }
        if (!any || GripRight == null)
            return false;
        var size = BoundsMax - BoundsMin;
        float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
        var centre = ((BoundsMin + BoundsMax) * 0.5f).N();

        var palm = Palm(GripRight);
        float rightGap = Gap(palm, out _);
        float plainGap = Gap(LeftHold.Reflect(palm.N(), LeftPlane.Origin, centre).U(), out _);

        LeftHold.Probes(BoundsMin.N(), BoundsMax.N(), probes);
        for (int i = 0; i < probes.Length; i++)
            probeGaps[i] = Gap(probes[i].U(), out _);
        float Asym(LeftPlane plane)
        {
            for (int i = 0; i < probes.Length; i++)
                reflectedGaps[i] = Gap(LeftHold.Reflect(probes[i], plane, centre).U(), out _);
            return LeftHold.Asymmetry(probeGaps, reflectedGaps, longest);
        }
        // The far end along the longest side, away from the grip: a plane that turns it round is out (vr.244: the
        // colliders read the checkpoint flag nearly as even across x as across z, and the x plane put its pole's far
        // end behind the fist).
        var grip = GripRight.localPosition.N();
        var end = HeldItemShape.FarEnd(BoundsMin.N(), BoundsMax.N(), grip);
        fit.Plane = LeftHold.Choose(
            rightGap,
            plainGap,
            Asym(LeftPlane.Origin),
            Asym(LeftPlane.CentreX),
            Asym(LeftPlane.CentreZ),
            Aimed,
            LeftHold.TurnsEnd(LeftPlane.Origin, centre, grip, end),
            LeftHold.TurnsEnd(LeftPlane.CentreX, centre, grip, end),
            LeftHold.TurnsEnd(LeftPlane.CentreZ, centre, grip, end)
        );
        fit.Centre = centre.U();
        fit.Shift = ShiftFor(palm, rightGap, fit.Plane, centre);
        if (GripLeft != null)
        {
            var second = Palm(GripLeft);
            fit.SecondShift = ShiftFor(second, Gap(second, out _), fit.Plane, centre);
        }
        fit.Ready = true;
        return true;

        Vector3 ShiftFor(Vector3 rightPalm, float gap, LeftPlane plane, System.Numerics.Vector3 c)
        {
            var leftPalm = LeftHold.Reflect(rightPalm.N(), plane, c).U();
            Gap(leftPalm, out var closest);
            return LeftHold.Shift(gap, leftPalm.N(), closest.N()).U();
        }
    }

    /// <summary>Centre of the game's preview hand under <paramref name="grip"/> in the item frame, else the grip.</summary>
    private Vector3 Palm(Transform grip)
    {
        var t = item.transform;
        if (
            grip.childCount > 0
            && grip.GetChild(0).TryGetComponent(out MeshFilter preview)
            && preview.sharedMesh != null
        )
            return t.InverseTransformPoint(
                preview.transform.TransformPoint(preview.sharedMesh.bounds.center)
            );
        return t.InverseTransformPoint(grip.position);
    }

    /// <summary>Distance from an item-frame point to the item's enabled colliders, and the nearest point on them
    /// (Collider.ClosestPoint: the point itself inside one).</summary>
    private float Gap(Vector3 local, out Vector3 closest)
    {
        var t = item.transform;
        var world = t.TransformPoint(local);
        float best = float.PositiveInfinity;
        closest = local;
        foreach (var col in item.colliders)
        {
            // ClosestPoint takes no concave mesh; no item has one (docs/research/item-grab-shapes.md).
            if (
                col == null
                || !col.enabled
                || !col.gameObject.activeInHierarchy
                || col is MeshCollider { convex: false }
            )
                continue;
            var q = col.ClosestPoint(world);
            float d = (q - world).sqrMagnitude;
            if (d < best)
            {
                best = d;
                closest = t.InverseTransformPoint(q);
            }
        }
        return Mathf.Sqrt(best);
    }

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
