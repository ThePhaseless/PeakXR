using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// A lid that needs both hands (the respawn chest): a hand alone on it for <see cref="Delay"/> seconds is a refusal,
/// reported once until the hands change, so a player who grabbed with one hand learns why nothing lifts instead of
/// pulling on a lid that silently stays shut. Call <see cref="Update"/> once per frame.
/// </summary>
public sealed class TwoHandGate
{
    public float Delay = 0.6f;

    private float alone;
    private bool fired;

    /// <summary>True on the one frame a single hand has been on the lid for <see cref="Delay"/> seconds.</summary>
    public bool Update(int handsOn, float dt)
    {
        if (handsOn != 1)
        {
            alone = 0f;
            fired = false;
            return false;
        }
        alone += dt;
        if (fired || alone < Delay)
            return false;
        fired = true;
        return true;
    }
}

/// <summary>Where to point a player toward a friend they cannot see: a spot a short way from the hand, level with
/// it, in the friend's compass direction, so the ring reads as an arrow at the hand rather than a mark through a
/// cliff.</summary>
public static class FriendBearing
{
    public static Vector3 Spot(Vector3 hand, Vector3 friend, float reach)
    {
        var d = friend - hand;
        var flat = new Vector3(d.X, 0f, d.Z);
        if (flat.LengthSquared() > 1e-6f)
            return hand + Vector3.Normalize(flat) * reach;
        return d.LengthSquared() > 1e-6f ? hand + Vector3.Normalize(d) * reach : hand;
    }
}

/// <summary>Why food held over a campfire does not cook: the fire first (an unlit or spent fire is the thing to
/// fix), then the food (Campfire.cs:341-356 only takes a cookable item).</summary>
public static class CookCheck
{
    public static DenyReason Reason(bool cookable, bool lit, bool spent) =>
        spent ? DenyReason.UsedUp
        : !lit ? DenyReason.NotLit
        : !cookable ? DenyReason.CantUse
        : DenyReason.None;
}

/// <summary>The win credits ignore every key for their first 20 s (PeakHandler.cs:231); the moment a press starts
/// to work gets its own cue.</summary>
public static class CreditsSkip
{
    public const float Delay = 20f;

    /// <summary>True when the credits' age crossed <see cref="Delay"/> between two frames.</summary>
    public static bool BecameReady(float before, float now) => before < Delay && now >= Delay;

    /// <summary>A press now would be ignored.</summary>
    public static bool TooSoon(float age) => age < Delay;
}

/// <summary>How close a held item is to a fire's heat (WS-W12), 0 at <c>range</c> outside the column, 1 at its
/// edge, so food can be guided into the heat by feel before cooking starts.</summary>
public static class HeatGradient
{
    public static float Closeness(float distance, float range) =>
        distance <= 0f ? 1f
        : distance >= range ? 0f
        : 1f - distance / range;
}

/// <summary>A palm laid on a surface (the soul pillar): the hand within <c>maxDistance</c> of the surface point
/// nearest it (or inside the surface, where that point is the hand itself), with the palm facing it.</summary>
public static class PalmContact
{
    public const float MinFacing = 0.5f;

    public static bool On(Vector3 palmNormal, Vector3 hand, Vector3 surface, float maxDistance)
    {
        var to = surface - hand;
        float d = to.Length();
        if (d > maxDistance)
            return false;
        return d < 1e-4f || Vector3.Dot(palmNormal, to / d) >= MinFacing;
    }
}

/// <summary>
/// Which altar socket an amulet goes into (Peak/ScoutStatue.cs:172-185, RPC_InsertAmulet): its own type's socket when
/// that type is not in yet, else the first type not in yet; -1 when every slot is filled or the type is not an
/// amulet. Sockets are indexed by amulet type (<c>amuletObjects</c>), slots fill in order (<c>hasAmulets</c>).
/// </summary>
public static class AltarSocket
{
    public static int For(int type, int[] slots, int sockets)
    {
        if (type < 0 || type >= slots.Length)
            return -1;
        bool free = false;
        foreach (int s in slots)
            free |= s == -1;
        if (!free)
            return -1;
        if (!Has(slots, type))
            return type;
        for (int i = 0; i < sockets; i++)
        {
            if (!Has(slots, i))
                return i;
        }
        return -1;
    }

    private static bool Has(int[] slots, int type)
    {
        foreach (int s in slots)
        {
            if (s == type)
                return true;
        }
        return false;
    }
}
