using PeakXR.Core;
using UnityEngine;

namespace PeakXR.UI.Guide;

/// <summary>One practice zone in the world: where the player stands, which way they face, and what it is tied to.</summary>
internal struct PracticeSpot
{
    /// <summary>Floor point the player walks to.</summary>
    public Vector3 Feet;

    /// <summary>Flat heading the zone's stations face (towards the wall at the climb zone).</summary>
    public Vector3 Facing;

    /// <summary>Tied to a scene feature: kept when the walk to it is skipped.</summary>
    public bool Fixed;

    /// <summary>What it is tied to, for the log and the self-test note.</summary>
    public string Anchor;
}

/// <summary>
/// Lays the practice zones out in the scene once, when the practice arrives (<see cref="PracticeZones"/> holds the
/// order and the fallback offsets). The climb zone goes to the airport's climbing wall, found by the rope anchor on
/// it; every other zone, or the climb zone when no anchor is found, goes one short walk further out from the zone
/// before it, at the first heading with floor, headroom and a clear line from there.
/// </summary>
internal static class PracticeLayout
{
    /// <summary>The walls and props a spot must keep out of: the same layers WorldCanvases keeps menus in front of
    /// (hall on Map, doors and photo booth on MirrorHidden, mirrors on MIrror, fences and kiosks on Default).</summary>
    private static readonly int Solid = LayerMask.GetMask(
        "Terrain",
        "Map",
        "Default",
        "MirrorHidden",
        "MIrror"
    );

    /// <summary>Arm's length from the wall: holds in reach, the body clear of the face.</summary>
    private const float WallStand = 0.6f;
    private const float StepHeight = 0.7f;

    private static readonly RaycastHit[] hits = new RaycastHit[8];

    public static PracticeSpot[] Plan(Vector3 feet, Vector3 forward)
    {
        var spots = new PracticeSpot[(int)PracticeZone.Social + 1];
        spots[0] = new PracticeSpot
        {
            Feet = feet,
            Facing = forward,
            Anchor = "where you started",
        };
        for (var z = PracticeZone.Body; z <= PracticeZone.Social; z++)
        {
            var prev = spots[(int)z - 1];
            spots[(int)z] =
                z == PracticeZone.Climb && ClimbWall(feet, forward, out var wall)
                    ? wall
                    : Fallback(z, prev, feet, forward);
            var s = spots[(int)z];
            Plugin.Log.LogInfo(
                $"[ui.guide] practice zone {z}: {s.Anchor}, {Flat(s.Feet - feet).magnitude:0.0} m from the start"
            );
        }
        return spots;
    }

    /// <summary>
    /// The airport's climbing wall (Airport scene: <c>Map/Climbing Wall/Climbing wall</c>, a rock pillar on Map with
    /// a <c>RopeAnchorWithRope</c> child on its top corner, RopeAnchorWithRope.cs:6). The rope hangs from
    /// <c>RopeAnchor.anchorPoint</c> (RopeAnchor.cs:10); the spot is at arm's length from the wall face under it, on
    /// the side you come from, so the wall and the rope are both in reach.
    /// </summary>
    private static bool ClimbWall(Vector3 start, Vector3 forward, out PracticeSpot spot)
    {
        spot = default;
        var anchor = Object.FindAnyObjectByType<RopeAnchorWithRope>();
        if (anchor == null)
            return false;
        var top =
            anchor.anchor != null && anchor.anchor.anchorPoint != null
                ? anchor.anchor.anchorPoint.position
                : anchor.transform.position;
        var wall =
            anchor.transform.parent != null
                ? anchor.transform.parent.GetComponentInParent<Collider>()
                : null;
        if (wall == null)
        {
            Plugin.Log.LogInfo("[ui.guide] the rope anchor hangs from no wall collider");
            return false;
        }
        var under = new Vector3(top.x, start.y, top.z);
        var away = Flat(start - under);
        away = away.sqrMagnitude > 0.01f ? away.normalized : -forward;
        // From well outside the pillar's ~12 m base on your side, chest high, back towards the rope's foot, against
        // the wall alone (Collider.Raycast), so fences on the way do not count.
        var from = under + away * 15f + Vector3.up * 1.3f;
        if (!wall.Raycast(new Ray(from, -away), out var hit, 16f))
        {
            Plugin.Log.LogInfo("[ui.guide] no face of the climbing wall towards you");
            return false;
        }
        var normal = Flat(hit.normal);
        normal = normal.sqrMagnitude > 0.01f ? normal.normalized : away;
        if (
            !Floor(hit.point + normal * WallStand, start.y, out var feet)
            // Chest up only: the pillar's foot flares out, and a flare under the knees is still a fine place to stand.
            || Physics.CheckCapsule(
                feet + Vector3.up * 1f,
                feet + Vector3.up * 1.7f,
                0.3f,
                Solid,
                QueryTriggerInteraction.Ignore
            )
        )
        {
            Plugin.Log.LogInfo(
                "[ui.guide] no clear floor at the climbing wall within a step of the start"
            );
            return false;
        }
        spot = new PracticeSpot
        {
            Feet = feet,
            Facing = -normal,
            Fixed = true,
            Anchor = $"the climbing wall under its rope ({anchor.name})",
        };
        return true;
    }

    private static PracticeSpot Fallback(
        PracticeZone zone,
        PracticeSpot prev,
        Vector3 start,
        Vector3 forward
    )
    {
        var outward = Flat(prev.Feet - start);
        outward = outward.sqrMagnitude > 0.25f ? outward.normalized : forward;
        for (int i = 0; i < PracticeZones.FallbackAttempts; i++)
        {
            var (yaw, distance) = PracticeZones.Fallback(zone, i);
            var dir = Quaternion.Euler(0f, yaw, 0f) * outward;
            if (!Walkable(prev.Feet, dir, distance, out var feet))
                continue;
            return new PracticeSpot
            {
                Feet = feet,
                Facing = dir,
                Anchor = $"open floor {distance:0.0} m from the {zone - 1} zone, {yaw:0} deg off",
            };
        }
        return new PracticeSpot
        {
            Feet = prev.Feet,
            Facing = prev.Facing,
            Anchor = $"the {zone - 1} zone's spot (no clear floor nearby)",
        };
    }

    /// <summary>A straight walk: nothing at knee or chest height on the way, floor at the end within a step of the
    /// start, and room for a body there.</summary>
    private static bool Walkable(Vector3 from, Vector3 dir, float distance, out Vector3 feet)
    {
        feet = default;
        foreach (float h in new[] { 0.5f, 1.4f })
            if (NearestSolid(from + Vector3.up * h, dir, distance + 0.4f, out _))
                return false;
        if (!Floor(from + dir * distance, from.y, out feet))
            return false;
        return !Physics.CheckCapsule(
            feet + Vector3.up * 0.45f,
            feet + Vector3.up * 1.7f,
            0.35f,
            Solid,
            QueryTriggerInteraction.Ignore
        );
    }

    private static bool Floor(Vector3 at, float nearY, out Vector3 feet)
    {
        feet = default;
        var top = new Vector3(at.x, nearY + 1.5f, at.z);
        if (
            !Physics.Raycast(
                top,
                Vector3.down,
                out var hit,
                1.5f + StepHeight,
                Solid,
                QueryTriggerInteraction.Ignore
            )
            || hit.normal.y < 0.7f
        )
            return false;
        feet = hit.point;
        return Mathf.Abs(feet.y - nearY) <= StepHeight;
    }

    /// <summary>The nearest hit on scenery, skipping loose things (items, bodies: anything with a rigidbody).</summary>
    private static bool NearestSolid(Vector3 from, Vector3 dir, float distance, out RaycastHit best)
    {
        best = default;
        int n = Physics.RaycastNonAlloc(
            from,
            dir,
            hits,
            distance,
            Solid,
            QueryTriggerInteraction.Ignore
        );
        float nearest = float.MaxValue;
        for (int i = 0; i < n; i++)
            if (hits[i].rigidbody == null && hits[i].distance < nearest)
            {
                nearest = hits[i].distance;
                best = hits[i];
            }
        return nearest < float.MaxValue;
    }

    /// <summary>How far the floor-level view runs ahead of a spot before scenery, at chest height.</summary>
    public static float ClearAhead(PracticeSpot spot, float height) =>
        NearestSolid(spot.Feet + Vector3.up * height, spot.Facing, 3f, out var hit)
            ? hit.distance
            : 3f;

    /// <summary>Floor under a point, for the start spot; the point itself when there is none in reach.</summary>
    public static Vector3 FloorUnder(Vector3 head) =>
        Physics.Raycast(
            head,
            Vector3.down,
            out var hit,
            2.5f,
            Solid,
            QueryTriggerInteraction.Ignore
        )
            ? hit.point
            : head + Vector3.down * 1.6f;

    /// <summary>Whether a sphere there overlaps scenery, for the self-test.</summary>
    public static bool InScenery(Vector3 at, float radius) =>
        Physics.CheckSphere(at, radius, Solid, QueryTriggerInteraction.Ignore);

    /// <summary>Whether a straight line between two points is clear of scenery.</summary>
    public static bool Clear(Vector3 a, Vector3 b) =>
        !NearestSolid(a, (b - a).normalized, (b - a).magnitude, out _);

    public static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);
}
