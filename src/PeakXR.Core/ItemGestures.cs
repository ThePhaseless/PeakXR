using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Hysteresis and dwell for "held against something" gestures (item over a fire, flame on a fuse, bandage on the
/// chest): active once the distance has stayed at or under <see cref="Enter"/> for <see cref="Dwell"/> seconds, and
/// until it goes past <see cref="Exit"/>, so tracking jitter at the edge does not cancel a vanilla hold. Call
/// <see cref="Update"/> once per frame.
/// </summary>
public sealed class ContactDwell
{
    public float Enter = 0.1f;
    public float Exit = 0.15f;
    public float Dwell = 0.2f;

    private bool inside;
    private float timer;

    public bool Active { get; private set; }

    public bool Update(float distance, float dt)
    {
        inside = distance <= (inside ? Exit : Enter);
        if (!inside)
        {
            timer = 0f;
            Active = false;
            return false;
        }
        timer += dt;
        Active = timer >= Dwell;
        return Active;
    }

    public void Reset()
    {
        inside = false;
        timer = 0f;
        Active = false;
    }
}

/// <summary>Distance to a vertical cylinder, flat-ended; 0 inside. The heat column over a campfire, and the torso.</summary>
public static class HeatZone
{
    /// <param name="point">World point to test.</param>
    /// <param name="bottom">Centre of the cylinder's axis line at height 0.</param>
    /// <param name="radius">Cylinder radius, metres.</param>
    /// <param name="minUp">Lowest height above <paramref name="bottom"/> that counts.</param>
    /// <param name="maxUp">Highest height above <paramref name="bottom"/> that counts.</param>
    public static float Distance(
        Vector3 point,
        Vector3 bottom,
        float radius,
        float minUp,
        float maxUp
    )
    {
        var d = point - bottom;
        float radial = MathF.Max(MathF.Sqrt(d.X * d.X + d.Z * d.Z) - radius, 0f);
        float vertical = MathF.Max(MathF.Max(minUp - d.Y, d.Y - maxUp), 0f);
        return MathF.Sqrt(radial * radial + vertical * vertical);
    }

    /// <summary>The point of the cylinder nearest <paramref name="point"/> (itself when inside), for a cue showing
    /// where the zone starts. Same parameters as <see cref="Distance"/>.</summary>
    public static Vector3 Nearest(
        Vector3 point,
        Vector3 bottom,
        float radius,
        float minUp,
        float maxUp
    )
    {
        var d = point - bottom;
        float flat = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
        float scale = flat > radius ? radius / flat : 1f;
        return bottom + new Vector3(d.X * scale, Math.Clamp(d.Y, minUp, maxUp), d.Z * scale);
    }
}

/// <summary>
/// Body zones of the local player derived from tracked poses only, so they work in seated mode and do not lag behind
/// the avatar: the torso as an upright cylinder under the head, and the forearm behind a tracked hand.
/// </summary>
public static class BodyZones
{
    /// <summary>Top of the torso below the eyes, metres.</summary>
    public const float TorsoTop = 0.25f;

    /// <summary>Bottom of the torso (belt) below the eyes, metres.</summary>
    public const float TorsoBottom = 0.7f;

    /// <summary>The spine runs this far behind the eyes, metres.</summary>
    public const float SpineBack = 0.08f;

    /// <summary>The forearm runs from this far behind the grip pose ...</summary>
    public const float ForearmStart = 0.05f;

    /// <summary>... to this far behind it, metres.</summary>
    public const float ForearmEnd = 0.3f;

    /// <summary>How far out from the torso (m) the hand itself may be for a held item resting there to count.</summary>
    public const float HandOffTorso = 0.2f;

    /// <summary>The hand itself is at the torso, not just the item it holds: an item reaches up to 0.77 m from the grip
    /// (Aloe Vera), so by the item alone a front hold or an arm hanging beside the hip is "on the body" (#147). Fatter
    /// than the torso but no taller, since below the belt or above the shoulders a hand is not pressing anything.</summary>
    public static bool HandAtTorso(Vector3 hand, Vector3 head, Vector3 flatForward, float radius) =>
        TorsoDistance(hand, head, flatForward, radius + HandOffTorso) <= 0f;

    /// <summary>How far off the forearm's press radius (m) the hand itself may be, as <see cref="HandOffTorso"/>.</summary>
    public const float HandOffForearm = 0.1f;

    /// <summary>The hand itself is at the other forearm, not just the item: hands idling side by side in front are
    /// about 0.3 m apart, and a big item reaches across that (#147).</summary>
    public static bool HandAtForearm(
        Vector3 hand,
        Vector3 otherHand,
        Vector3 otherForward,
        float radius
    ) => ForearmDistance(hand, otherHand, otherForward) - radius <= HandOffForearm;

    /// <param name="point">World point to test.</param>
    /// <param name="head">Tracked eye position.</param>
    /// <param name="flatForward">Head forward with the pitch removed (unit length, Y = 0).</param>
    /// <param name="radius">Torso radius around the spine, including the hand's own thickness.</param>
    public static float TorsoDistance(
        Vector3 point,
        Vector3 head,
        Vector3 flatForward,
        float radius
    )
    {
        var spine = head - flatForward * SpineBack;
        spine.Y -= TorsoBottom;
        return HeatZone.Distance(point, spine, radius, 0f, TorsoBottom - TorsoTop);
    }

    /// <summary>Distance from <paramref name="point"/> to the forearm line behind a tracked hand.</summary>
    /// <param name="handForward">The hand's forward (unit), toward the fingers: <see cref="GripFrame.Fingers"/> in the
    /// grip's frame, not its +Z (that runs along the handle).</param>
    public static float ForearmDistance(Vector3 point, Vector3 hand, Vector3 handForward) =>
        SegmentDistance(point, hand - handForward * ForearmStart, hand - handForward * ForearmEnd);

    public static float SegmentDistance(Vector3 point, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        float len2 = ab.LengthSquared();
        float t = len2 > 1e-8f ? Math.Clamp(Vector3.Dot(point - a, ab) / len2, 0f, 1f) : 0f;
        return Vector3.Distance(point, a + ab * t);
    }
}

/// <summary>
/// A hollow case's solid box, in the case's own frame. Nothing stops a tracked hand going into a case, and an overlap
/// sphere in its hollow meets nothing, so the box stands in for the hollow. It has to be the case's own frame: a
/// world-axis box round a yawed or tilted case reaches about a metre past its walls, into air and rock (#54).
/// </summary>
public struct CaseBox
{
    private bool any;

    public Vector3 Min { get; private set; }
    public Vector3 Max { get; private set; }

    /// <summary>Grows the box round a collider's box at <paramref name="centre"/> with these half axes, all in the
    /// case's frame; each half axis's size per case axis adds up to the same box as the 8 corners.</summary>
    public void Add(Vector3 centre, Vector3 halfX, Vector3 halfY, Vector3 halfZ)
    {
        var reach = Vector3.Abs(halfX) + Vector3.Abs(halfY) + Vector3.Abs(halfZ);
        Min = any ? Vector3.Min(Min, centre - reach) : centre - reach;
        Max = any ? Vector3.Max(Max, centre + reach) : centre + reach;
        any = true;
    }

    /// <summary><paramref name="point"/>, in the case's frame, is inside the box or within
    /// <paramref name="radius"/> of it. Never with nothing added.</summary>
    public readonly bool Within(Vector3 point, float radius) =>
        any && Vector3.Distance(point, Vector3.Clamp(point, Min, Max)) <= radius;
}

/// <summary>
/// Lid lift for luggage: maps the hand's rise onto the game's hold-to-open timer. The timer still runs on time
/// (<c>Interaction.currentInteractableHeldTime</c>), so a lift never opens faster than holding the button; the lid
/// only caps how far it may go, and only a fully raised lid lets it finish.
/// </summary>
public static class LidLift
{
    /// <summary>The lowest point, measured along <paramref name="up"/>, of a box at <paramref name="centre"/> with these
    /// half axes: a lid collider's underside along its case's own up, the seam of a case however it lies (#127).</summary>
    public static float LowerFace(
        Vector3 centre,
        Vector3 halfX,
        Vector3 halfY,
        Vector3 halfZ,
        Vector3 up
    ) =>
        Vector3.Dot(centre, up)
        - MathF.Abs(Vector3.Dot(halfX, up))
        - MathF.Abs(Vector3.Dot(halfY, up))
        - MathF.Abs(Vector3.Dot(halfZ, up));

    /// <summary>A hand on the lid half: at or over the lid's <paramref name="face"/> (<see cref="LowerFace"/>) along the
    /// case's <paramref name="up"/>, less <paramref name="give"/>.</summary>
    public static bool OnLid(Vector3 hand, float face, Vector3 up, float give) =>
        Vector3.Dot(hand, up) >= face - give;

    public static float Fraction(float rise, float height) =>
        height <= 0f ? 1f : Math.Clamp(rise / height, 0f, 1f);

    /// <summary>The held time to leave for the game, which then adds <paramref name="dt"/>; afterwards it is at most
    /// <paramref name="fraction"/> of <paramref name="total"/>, so the hold cannot complete before the lid is up.</summary>
    public static float CapHeldTime(float held, float total, float fraction, float dt)
    {
        if (fraction >= 1f)
            return held;
        // May go below 0 by up to dt: the game's add then lands exactly on the cap, so a long frame cannot run (or
        // with dt >= total, finish) a lid that is not up (two-player run 23: 0.17 with the hand still).
        return MathF.Min(held, fraction * total - dt);
    }
}

/// <summary>
/// A hand sliding along a held shaft, like a parasol's runner: +1 each time it travels <see cref="Travel"/> up from
/// where the stroke began, -1 down; each stroke then starts again from there. Call <see cref="Update"/> per frame.
/// </summary>
public sealed class RunnerSlide
{
    public float Travel = 0.15f;

    private float start;
    private bool active;

    public void Begin(float along)
    {
        start = along;
        active = true;
    }

    public void End() => active = false;

    public int Update(float along)
    {
        if (!active)
            return 0;
        float d = along - start;
        if (d < Travel && d > -Travel)
            return 0;
        start = along;
        return d > 0f ? 1 : -1;
    }
}
