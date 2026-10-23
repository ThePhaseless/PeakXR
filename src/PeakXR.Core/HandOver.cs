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
    public HandOverState Update(bool inZone, bool trigger, bool usable, bool ownUse = false)
    {
        if (!inZone || (!usable && ownUse))
        {
            Reset();
            return HandOverState.None;
        }
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

    public void Reset()
    {
        armed = false;
        refused = false;
        SuppressPrimary = false;
    }
}
