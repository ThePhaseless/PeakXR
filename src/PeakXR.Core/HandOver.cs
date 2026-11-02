namespace PeakXR.Core;

/// <summary>
/// How far a held item is from a friend's mouth or chest: from the nearest point of its bounds, not its centre, so a
/// long item counts by the end the player puts there (MP run 27: a lollipop's candy at the mouth, its centre 0.2 m off
/// and outside the zone).
/// </summary>
public static class FeedZone
{
    /// <summary>Distance from an axis-aligned box (<paramref name="center"/>, <paramref name="extents"/>) to
    /// <paramref name="target"/>; 0 inside.</summary>
    public static float Gap(
        System.Numerics.Vector3 center,
        System.Numerics.Vector3 extents,
        System.Numerics.Vector3 target
    )
    {
        var d = System.Numerics.Vector3.Abs(target - center) - extents;
        return System.Numerics.Vector3.Max(d, System.Numerics.Vector3.Zero).Length();
    }

    /// <summary><see cref="Gap"/> for the box moved to where its controller puts the item: <paramref name="rig"/> is
    /// the item's body now, <paramref name="place"/> that body's controller-held place. A friend's body can keep the
    /// box on them while the controller is elsewhere (#119), and the body's pivot is no measure of the box: it can lie
    /// at one end, 0.51 m from the other on a Berrynana.</summary>
    public static float PlaceGap(
        System.Numerics.Vector3 center,
        System.Numerics.Vector3 extents,
        System.Numerics.Vector3 rig,
        System.Numerics.Vector3 place,
        System.Numerics.Vector3 target
    ) => Gap(center + (place - rig), extents, target);
}

/// <summary>What a held item at a friend's mouth or chest does this frame (<see cref="HandOver"/>).</summary>
public enum HandOverState : byte
{
    /// <summary>Out of the zone, or a trigger already held on arrival: nothing.</summary>
    None,

    /// <summary>In the zone with the trigger up, or rested just short of it: show where it would go.</summary>
    Hint,

    /// <summary>The trigger was pressed in the zone: use the item on the friend (hold the game's use-on-friend).</summary>
    Apply,

    /// <summary>The trigger was pressed in the zone with an item they cannot take: refuse, once per press.</summary>
    Refuse,
}

/// <summary>
/// Feeding or healing a friend as a hand-over, never by passing close: the item rests at their mouth (food) or on
/// their chest (a bandage) for <see cref="Dwell"/>, or the trigger is squeezed there. A trigger already held when the item arrives (eating on the way,
/// another use) does not count until it is let go and pressed again in the zone. While armed in the zone the
/// trigger belongs to the hand-over, so it must not also start the item's own use (<see cref="SuppressPrimary"/>).
/// </summary>
public sealed class HandOver
{
    private bool armed;
    private bool refused;

    /// <summary>The trigger is the hand-over's this frame: the caller keeps it from the item's primary use.</summary>
    public bool SuppressPrimary { get; private set; }

    /// <summary>Seconds an applying hand may wobble out of the zone, trigger still held, before the hand-over ends
    /// (MP run 15: a frame's wobble past the mouth ended the feed for good, the trigger never re-armed).</summary>
    public float Grace = 0.4f;

    /// <summary>Seconds the item must stay in the zone to apply with no trigger (food at a friend's mouth, as at your
    /// own); 0 needs the trigger.</summary>
    public float Dwell;

    /// <summary>Food rests this long at a friend's mouth before it feeds (issue #14: the squeeze it needed was the
    /// hard part).</summary>
    public const float MouthDwell = 0.3f;

    /// <summary>A healing item rests this long on a friend's chest before it is used: as long as pressing it to your
    /// own body (interact.items' body press), issue #45.</summary>
    public const float ChestDwell = 0.35f;

    public static float DwellAt(bool chest) => chest ? ChestDwell : MouthDwell;

    /// <summary>The feeder's level speed (m/s) under which the dwell counts: walking up to a friend with food at chest
    /// height must not feed them (#93, MP run 13); a squeeze still feeds at once.</summary>
    public const float StillSpeed = 0.5f;

    public static bool Still(float levelSpeed) => levelSpeed < StillSpeed;

    /// <summary>Both the feeder and the friend: a friend who walks into food held out is not being fed either.</summary>
    public static bool Still(float feederSpeed, float friendSpeed) =>
        Still(feederSpeed) && Still(friendSpeed);

    /// <summary>The feed zone (m): how near the item's nearest point comes to the mouth, and how near it would be at its
    /// controller-held place (<see cref="FeedZone.PlaceGap"/>) for the dwell to count: the player holds it there, it did
    /// not only come to rest there. The body weld keeps an item on a friend with the controller elsewhere (#119: 0.63 m
    /// off, fed in 1.15 s).</summary>
    public const float PlaceRadius = 0.22f;

    /// <summary>The same two measures just outside the zone, out to where a press says "closer": a hand offering the item
    /// there, not one whose item a friend's body keeps while it is elsewhere.</summary>
    public const float NearPlaceRadius = 0.45f;

    /// <summary>A visit to the near band ends only this far (m) past it: a wobble over its edge refuses nothing twice.</summary>
    public const float NearExitMargin = 0.05f;

    /// <summary>The near band's refusal comes with the view at most this far (degrees) off the spot.</summary>
    public const float NearLookDegrees = 45f;

    /// <summary>A friend's body holds the item off the spot its controller has reached: it cannot get closer, and
    /// letting go would only snap it inside them (#116, #119), so it counts as there (#112: a Frisbee 0.24 m off on a
    /// 0.22 m zone, with the hand 1.00 m short).</summary>
    public static bool HeldOffAtSpot(bool keptByThem, float placeGap) =>
        keptByThem && placeGap <= PlaceRadius;

    private bool applying;
    private float outFor;
    private float inFor;

    /// <summary>The dwell applied or refused on this visit to the zone.</summary>
    private bool dwelt;

    /// <summary>Seconds at rest just outside the zone, and whether this visit to it or the zone has been refused.</summary>
    private float nearFor;
    private bool nearRefused;

    /// <param name="usable">The friend can take the item (canUseOnFriend).</param>
    /// <param name="ownUse">The item has a trigger use of its own (a torch, a spool): when it is not
    /// <paramref name="usable"/> there is no hand-over at all, so its trigger stays its own and nothing is refused.</param>
    /// <param name="dt">Seconds since the last update, for the <see cref="Grace"/>; 0 gives none.</param>
    /// <param name="still">The feeder and the friend are nearly still (<see cref="Still(float, float)"/>): the
    /// <see cref="Dwell"/> only counts then, and starts over when one moves before it is done.</param>
    /// <param name="placeGap">The item at its controller-held place from the mouth or chest (m,
    /// <see cref="FeedZone.PlaceGap"/>): the <see cref="Dwell"/> only counts within <see cref="PlaceRadius"/>. The
    /// trigger does not read it.</param>
    /// <param name="nearGap">The item from the nearest friend's mouth or chest (m): out of the zone, within
    /// <see cref="NearPlaceRadius"/> it is just short of it (<see cref="NearStep"/>).</param>
    /// <param name="looking">The view is on the spot (<see cref="NearLookDegrees"/>).</param>
    public HandOverState Update(
        bool inZone,
        bool trigger,
        bool usable,
        bool ownUse = false,
        float dt = 0f,
        bool still = true,
        float placeGap = 0f,
        float nearGap = float.PositiveInfinity,
        bool looking = true
    )
    {
        if (!inZone && applying && usable && (trigger || dwelt) && (outFor += dt) < Grace)
            return HandOverState.Apply;
        outFor = 0f;
        if (!inZone || (!usable && ownUse))
        {
            ResetZone();
            if (nearGap > NearPlaceRadius + NearExitMargin)
                nearRefused = false;
            if (nearGap <= NearPlaceRadius && !inZone && (usable || !ownUse))
                return NearStep(trigger, usable, dt, still && placeGap <= NearPlaceRadius, looking);
            nearFor = 0f;
            return HandOverState.None;
        }
        nearFor = 0f;
        var state = Step(trigger, usable);
        if (Dwell > 0f && state is HandOverState.None or HandOverState.Hint)
            state = DwellStep(usable, state, dt, still && placeGap <= PlaceRadius);
        applying = state == HandOverState.Apply;
        nearRefused |= state == HandOverState.Refuse;
        return state;
    }

    /// <summary>Just short of the zone with the trigger up, for <see cref="Dwell"/>: say so instead of nothing (#112).
    /// An item they can take gets the ring on the spot for as long as it rests there; one they cannot is refused once
    /// per visit, and only with the view on the spot: it is a cue, and an arm resting past a friend asked nothing. The
    /// ring stays silent, so it needs no look. The trigger stays the item's own here, and its own refusal speaks
    /// (<see cref="Context"/>).</summary>
    private HandOverState NearStep(bool trigger, bool usable, float dt, bool counts, bool looking)
    {
        if (trigger || !counts)
        {
            nearFor = 0f;
            return HandOverState.None;
        }
        nearFor += dt;
        if (nearFor < Dwell)
            return HandOverState.None;
        if (usable)
            return HandOverState.Hint;
        if (nearRefused || !looking)
            return HandOverState.None;
        nearRefused = true;
        return HandOverState.Refuse;
    }

    /// <summary>Held in the zone for <see cref="Dwell"/>: an item they can take is applied for as long as it stays;
    /// one they cannot is refused once per visit. Anything but <paramref name="counts"/> restarts a dwell not yet done.</summary>
    private HandOverState DwellStep(bool usable, HandOverState state, float dt, bool counts)
    {
        if (counts)
            inFor += dt;
        else if (inFor < Dwell)
            inFor = 0f;
        if (inFor < Dwell)
            return state;
        if (usable)
        {
            dwelt = true;
            SuppressPrimary = true;
            return HandOverState.Apply;
        }
        if (dwelt)
            return state;
        dwelt = true;
        return HandOverState.Refuse;
    }

    private HandOverState Step(bool trigger, bool usable)
    {
        if (!trigger)
        {
            armed = true;
            refused = false;
            // Only an item the friend can take claims the trigger; anything else keeps it until a refused press.
            SuppressPrimary = usable;
            return usable ? HandOverState.Hint : HandOverState.None;
        }
        SuppressPrimary = armed && usable;
        if (!armed)
            return HandOverState.None;
        if (usable)
            return HandOverState.Apply;
        if (refused)
            return HandOverState.None;
        refused = true;
        return HandOverState.Refuse;
    }

    /// <summary>
    /// The refusal a player needs for the held item: at a friend's mouth or chest, the context is a hand-over, so any
    /// refusal of the item's own use there says "can't feed this" (CantUse); elsewhere the item's own reason stands
    /// (MP run 22: a piton at the host's mouth said NoWall).
    /// </summary>
    public static DenyReason ContextReason(DenyReason own, bool atFriendMouth) =>
        atFriendMouth && own != DenyReason.None ? DenyReason.CantUse : own;

    /// <summary>
    /// The refusal for a press of the held item, given where it is: at a friend's mouth or chest with an item they
    /// cannot take, CantUse (<see cref="ContextReason(DenyReason, bool)"/>); just beside it
    /// (<paramref name="nearFriendMouth"/>, out to a little past the feed zone), the own-mouth eat refusal becomes
    /// "closer" (OutOfReach) with <paramref name="hintAtFriendMouth"/> set, so the ring points at the friend's mouth
    /// instead of the player's own (MP run 26: NotHere there; silencing it left a squeeze beside a friend's face
    /// with no cue at all). Anything else stands.
    /// </summary>
    public static DenyReason Context(
        DenyReason own,
        bool atFriendMouth,
        bool nearFriendMouth,
        out bool hintAtFriendMouth
    )
    {
        hintAtFriendMouth = false;
        if (atFriendMouth)
            return ContextReason(own, true);
        if (nearFriendMouth && own == DenyReason.NotHere)
        {
            hintAtFriendMouth = true;
            return DenyReason.OutOfReach;
        }
        return own;
    }

    public void Reset()
    {
        ResetZone();
        nearFor = 0f;
        nearRefused = false;
    }

    private void ResetZone()
    {
        armed = false;
        refused = false;
        applying = false;
        outFor = 0f;
        inFor = 0f;
        dwelt = false;
        SuppressPrimary = false;
    }
}
