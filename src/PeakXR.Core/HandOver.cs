namespace PeakXR.Core;

/// <summary>What a held item at a friend's mouth or chest does this frame (<see cref="HandOver"/>).</summary>
public enum HandOverState : byte
{
    /// <summary>Out of the zone, or a trigger already held on arrival: nothing.</summary>
    None,

    /// <summary>In the zone with the trigger up: show where it would go.</summary>
    Hint,

    /// <summary>The trigger was pressed in the zone: use the item on the friend (hold the game's use-on-friend).</summary>
    Apply,

    /// <summary>The trigger was pressed in the zone with an item they cannot take: refuse, once per press.</summary>
    Refuse,
}

/// <summary>
/// Feeding or healing a friend as a hand-over, never by proximity alone: bring the item to their mouth (food) or
/// chest (a bandage), then squeeze the trigger. A trigger already held when the item arrives (eating on the way,
/// another use) does not count until it is let go and pressed again in the zone. While armed in the zone the
/// trigger belongs to the hand-over, so it must not also start the item's own use (<see cref="SuppressPrimary"/>).
/// </summary>
public sealed class HandOver
{
    private bool armed;
    private bool refused;

    /// <summary>The trigger is the hand-over's this frame: the caller keeps it from the item's primary use.</summary>
    public bool SuppressPrimary { get; private set; }

    /// <param name="usable">The friend can take the item (canUseOnFriend).</param>
    /// <param name="ownUse">The item has a trigger use of its own (a torch, a spool): when it is not
    /// <paramref name="usable"/> there is no hand-over at all, so its trigger stays its own and nothing is refused.</param>
    /// <summary>Seconds an applying hand may wobble out of the zone, trigger still held, before the hand-over ends
    /// (MP run 15: a frame's wobble past the mouth ended the feed for good, the trigger never re-armed).</summary>
    public float Grace = 0.4f;

    private bool applying;
    private float outFor;

    /// <param name="dt">Seconds since the last update, for the <see cref="Grace"/>; 0 gives none.</param>
    public HandOverState Update(
        bool inZone,
        bool trigger,
        bool usable,
        bool ownUse = false,
        float dt = 0f
    )
    {
        if (!inZone && applying && trigger && usable && (outFor += dt) < Grace)
            return HandOverState.Apply;
        outFor = 0f;
        if (!inZone || (!usable && ownUse))
        {
            Reset();
            return HandOverState.None;
        }
        var state = Step(trigger, usable);
        applying = state == HandOverState.Apply;
        return state;
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
    /// As above, and with the held item near a friend's mouth or chest (<paramref name="nearFriendMouth"/>, a little
    /// wider than the feed zone): the own-mouth eat refusal (NotHere) is silent there, because the trigger is the hand-over's
    /// squeeze, not a try at eating it yourself (MP run 26: a feed that worked also said NotHere). None means no cue.
    /// </summary>
    public static DenyReason ContextReason(DenyReason own, bool atFriendMouth, bool nearFriendMouth)
    {
        if (atFriendMouth)
            return ContextReason(own, true);
        return nearFriendMouth && own == DenyReason.NotHere ? DenyReason.None : own;
    }

    public void Reset()
    {
        armed = false;
        refused = false;
        applying = false;
        outFor = 0f;
        SuppressPrimary = false;
    }
}
