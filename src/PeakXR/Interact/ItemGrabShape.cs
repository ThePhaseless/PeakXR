using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Core;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace PeakXR.Interact;

/// <summary>
/// Grab reach measured to what the player sees, for items whose drawn parts and game colliders disagree
/// (<see cref="ItemReach"/>; docs/research/item-grab-shapes.csv). The game's colliders stay as they are (vanilla
/// physics and multiplayer parity); only the hand's reach test changes. Each instance is read once (per item state,
/// since backpacks swap meshes, Backpack.cs:41-42) and measured live after that, so animated parts (a parasol's
/// canopy, an early worm circling on its pivot) are measured where they are drawn now. Allocation-free per frame.
/// </summary>
internal static class ItemGrabShape
{
    /// <summary>The largest item reach looked for around a hand, metres: farther items are not read.</summary>
    public const float MaxReach = 2f;

    private sealed class Part
    {
        public Transform Frame;
        public Bounds Local;
        public SkinnedMeshRenderer Skin;

        /// <summary>A skinned part's bones (more than one): their span stands in for its pose.</summary>
        public Transform[] Bones;
        public bool Extra;
    }

    private sealed class Entry
    {
        public ItemState State;
        public Part[] Parts;
        public bool Extends;
        public bool Clip;

        /// <summary>Radius around the item's origin, item frame, that holds every part.</summary>
        public float Reach;
    }

    private static readonly Dictionary<Item, Entry> entries = new();
    private static readonly List<Renderer> renderers = new();
    private static readonly List<Item> dead = new();
    private static readonly Part[] NoParts = System.Array.Empty<Part>();

    /// <summary>The grab measures <paramref name="item"/> to its drawn parts (some extend past its colliders, or the
    /// colliders reach past everything drawn); <paramref name="reach"/> is the world radius around its origin
    /// that holds them.</summary>
    public static bool Corrected(Item item, out float reach)
    {
        var e = Get(item);
        reach = e != null ? e.Reach * MaxScale(item.transform) : 0f;
        return e != null && (e.Extends || e.Clip);
    }

    /// <summary>
    /// Grab distance from <paramref name="pos"/> to <paramref name="item"/> and the point it is measured to: the
    /// nearest point of its enabled solid colliders (their real surface; a concave mesh's bounds), then, for a
    /// corrected item, an extending part when nearer, and with clipping no nearer than its drawn parts. An item with
    /// no solid collider is measured to its centre.
    /// </summary>
    public static float Distance(Item item, Vector3 pos, out Vector3 point)
    {
        point = item.Center();
        float collider = float.PositiveInfinity;
        foreach (var col in item.colliders)
        {
            // An inactive child (a backpack's other mesh, an early worm's hand collider) keeps enabled set, and
            // its ClosestPoint answers the query point itself: a false 0 m.
            if (col == null || !col.enabled || col.isTrigger || !col.gameObject.activeInHierarchy)
                continue;
            var p = PhysicalInteraction.SurfacePoint(col, pos);
            float d = Vector3.Distance(p, pos);
            if (d < collider)
            {
                collider = d;
                point = p;
            }
        }
        if (float.IsPositiveInfinity(collider))
            return Vector3.Distance(point, pos);

        var e = Get(item);
        if (e == null || !(e.Extends || e.Clip))
            return collider;

        var toItem = item.transform.worldToLocalMatrix;
        var toWorld = item.transform.localToWorldMatrix;
        var local = toItem.MultiplyPoint3x4(pos).N();
        float visual = float.PositiveInfinity,
            extra = float.PositiveInfinity;
        Vector3 visualPoint = point,
            extraPoint = point;
        foreach (var part in e.Parts)
        {
            if (!Box(part, toItem, item.transform, out var box))
                continue;
            var p = toWorld.MultiplyPoint3x4(box.Closest(local).U());
            float d = Vector3.Distance(p, pos);
            if (d < visual)
            {
                visual = d;
                visualPoint = p;
            }
            if (part.Extra && d < extra)
            {
                extra = d;
                extraPoint = p;
            }
        }
        float result = ItemReach.Distance(collider, visual, extra, e.Clip);
        if (extra <= result)
            point = extraPoint;
        else if (e.Clip && visual > collider)
            point = visualPoint;
        return result;
    }

    private static Entry Get(Item item)
    {
        if (item == null)
            return null;
        if (entries.TryGetValue(item, out var e) && e.State == item.itemState)
            return e;
        if (entries.Count > 256)
            Sweep();
        e = Build(item);
        entries[item] = e;
        return e;
    }

    private static void Sweep()
    {
        foreach (var key in entries.Keys)
            if (key == null)
                dead.Add(key);
        foreach (var key in dead)
            entries.Remove(key);
        dead.Clear();
    }

    private static Entry Build(Item item)
    {
        var e = new Entry { State = item.itemState, Parts = NoParts };
        // A snowball grows by rescaling its mesh and collider apart from its root (ItemScaleSyncer.cs:47-50).
        if (item.GetComponent<ItemScaleSyncer>() != null)
            return e;
        var toItem = item.transform.worldToLocalMatrix;
        if (!ColliderBox(item, toItem, out var colMin, out var colMax))
            return e;

        var parts = new List<Part>();
        item.GetComponentsInChildren(false, renderers);
        foreach (var r in renderers)
        {
            if (!r.enabled)
                continue;
            if (r is MeshRenderer)
            {
                var filter = r.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                    parts.Add(new Part { Frame = r.transform, Local = filter.sharedMesh.bounds });
            }
            else if (r is SkinnedMeshRenderer skin && skin.sharedMesh != null)
            {
                var bones = skin.bones;
                parts.Add(
                    bones.Length > 1
                        ? new Part { Bones = bones }
                        : new Part
                        {
                            Skin = skin,
                            Frame = skin.rootBone != null ? skin.rootBone : skin.transform,
                        }
                );
            }
        }
        renderers.Clear();
        if (parts.Count == 0)
            return e;

        var visMin = new NVector3(float.PositiveInfinity);
        var visMax = new NVector3(float.NegativeInfinity);
        bool posedByBones = false;
        foreach (var part in parts)
        {
            if (!Box(part, toItem, item.transform, out var box))
                continue;
            var min = new NVector3(float.PositiveInfinity);
            var max = new NVector3(float.NegativeInfinity);
            box.Encapsulate(ref min, ref max);
            box.Encapsulate(ref visMin, ref visMax);
            part.Extra = ItemReach.Extends(colMin, colMax, min, max);
            e.Extends |= part.Extra;
            posedByBones |= part.Bones != null;
        }
        // Bones stand short of the posed mesh, so they may extend reach but never clip it.
        e.Clip = !posedByBones && ItemReach.Clips(colMin, colMax, visMin, visMax);
        e.Parts = parts.ToArray();
        var far = NVector3.Max(
            NVector3.Max(NVector3.Abs(visMin), NVector3.Abs(visMax)),
            NVector3.Max(NVector3.Abs(colMin), NVector3.Abs(colMax))
        );
        e.Reach = far.Length();
        if (e.Extends || e.Clip)
            Plugin.Log.LogDebug(
                $"[interact] grab shape {item.name}: drawn {visMin}..{visMax} vs colliders {colMin}..{colMax}, extends {e.Extends}, clip {e.Clip}"
            );
        return e;
    }

    /// <summary>The part's box in the item frame as it is posed now.</summary>
    private static bool Box(Part part, Matrix4x4 toItem, Transform item, out ReachBox box)
    {
        if (part.Bones != null)
        {
            var min = new NVector3(float.PositiveInfinity);
            var max = new NVector3(float.NegativeInfinity);
            foreach (var bone in part.Bones)
            {
                if (bone == null)
                    continue;
                var p = toItem.MultiplyPoint3x4(bone.position).N();
                min = NVector3.Min(min, p);
                max = NVector3.Max(max, p);
            }
            var pad = new NVector3(ItemReach.BonePad / Mathf.Max(MaxScale(item), 1e-4f));
            box = ReachBox.Aligned(min - pad, max + pad);
            return min.X <= max.X;
        }
        if (part.Frame == null)
        {
            box = default;
            return false;
        }
        // Skinned bounds are in the root bone's frame (the renderer's own without one).
        var b = part.Skin != null ? part.Skin.localBounds : part.Local;
        var m = toItem * part.Frame.localToWorldMatrix;
        Vector3 x = m.GetColumn(0),
            y = m.GetColumn(1),
            z = m.GetColumn(2);
        float lx = Mathf.Max(x.magnitude, 1e-6f),
            ly = Mathf.Max(y.magnitude, 1e-6f),
            lz = Mathf.Max(z.magnitude, 1e-6f);
        box = new ReachBox(
            m.MultiplyPoint3x4(b.center).N(),
            (x / lx).N(),
            (y / ly).N(),
            (z / lz).N(),
            new NVector3(b.extents.x * lx, b.extents.y * ly, b.extents.z * lz)
        );
        return true;
    }

    /// <summary>The item-frame box of the item's enabled solid colliders on active objects.</summary>
    private static bool ColliderBox(Item item, Matrix4x4 toItem, out NVector3 min, out NVector3 max)
    {
        min = new NVector3(float.PositiveInfinity);
        max = new NVector3(float.NegativeInfinity);
        foreach (var col in item.colliders)
        {
            if (col == null || !col.enabled || col.isTrigger || !col.gameObject.activeInHierarchy)
                continue;
            Bounds local;
            switch (col)
            {
                case BoxCollider bc:
                    local = new Bounds(bc.center, bc.size);
                    break;
                case SphereCollider sc:
                    local = new Bounds(sc.center, Vector3.one * (2f * sc.radius));
                    break;
                case CapsuleCollider cc:
                    var size = Vector3.one * (2f * cc.radius);
                    size[cc.direction] = Mathf.Max(cc.height, 2f * cc.radius);
                    local = new Bounds(cc.center, size);
                    break;
                case MeshCollider mc when mc.sharedMesh != null:
                    local = mc.sharedMesh.bounds;
                    break;
                default:
                    continue;
            }
            var m = toItem * col.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                var c = local.center;
                var ex = local.extents;
                var corner = new Vector3(
                    c.x + ((i & 1) == 0 ? -ex.x : ex.x),
                    c.y + ((i & 2) == 0 ? -ex.y : ex.y),
                    c.z + ((i & 4) == 0 ? -ex.z : ex.z)
                );
                var p = m.MultiplyPoint3x4(corner).N();
                min = NVector3.Min(min, p);
                max = NVector3.Max(max, p);
            }
        }
        return min.X <= max.X;
    }

    private static float MaxScale(Transform t)
    {
        var s = t.lossyScale;
        return Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
    }
}
