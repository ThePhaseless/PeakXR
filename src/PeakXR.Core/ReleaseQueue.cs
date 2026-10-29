namespace PeakXR.Core;

/// <summary>What a hand's <see cref="ReleaseQueue"/> does this frame.</summary>
public enum ReleaseStep : byte
{
    /// <summary>Nothing queued, or still waiting for the item or for the game.</summary>
    Wait,

    /// <summary>The item is in the hand and the game takes a drop now: carry out the queued release.</summary>
    Run,

    /// <summary>The item's use kept running past <see cref="ReleaseQueue.CastSeconds"/>: the game will not drop it,
    /// so the release is refused (TooSoon) and the item stays.</summary>
    Refused,
}

/// <summary>
/// One hand's drop or throw that the game cannot carry out the moment the grip opens, so a release always lets go of
/// what that grip closed on (issue #13). Two cases: the pickup is still on its way (<c>Item.Interact</c> hides the item
/// and asks the master, Item.cs:589-591, and the equip lands an RPC later, CharacterItems.cs:441-457, a round trip for
/// a client), or the item is casting a use the same opening ends (the game ignores a drop mid-cast,
/// CharacterItems.cs:254-258; a fist's trigger let go with the grip cancels it on the game's next use read, :209-212).
/// </summary>
public sealed class ReleaseQueue
{
    /// <summary>How long a release waits for its pickup to land; past it the pickup is taken as lost.</summary>
    public const float ArriveSeconds = 2f;

    /// <summary>How long a release in hand waits for a running use to end before it is refused.</summary>
    public const float CastSeconds = 0.3f;

    private float queuedAt;
    private float heldAt;
    private bool held;

    /// <summary>The queued release: <see cref="GripReleaseAction.Keep"/> when none.</summary>
    public GripReleaseAction Action { get; private set; }

    public bool Pending => Action is GripReleaseAction.Drop or GripReleaseAction.Throw;

    /// <summary>The grip opened with a drop or throw the game cannot take yet. Anything else queues nothing.</summary>
    public void Queue(GripReleaseAction action, float now)
    {
        if (action is not (GripReleaseAction.Drop or GripReleaseAction.Throw))
            return;
        Action = action;
        queuedAt = now;
        held = false;
    }

    /// <summary>A fresh close keeps the item, or the hand's state was reset.</summary>
    public void Clear()
    {
        Action = GripReleaseAction.Keep;
        held = false;
    }

    /// <param name="inHand">The item the grip let go of is in that hand.</param>
    /// <param name="casting">The game ignores a drop now (<c>Item.progress</c> above 0).</param>
    /// <param name="now">Clock time.</param>
    /// <param name="action">The release to carry out on <see cref="ReleaseStep.Run"/>.</param>
    public ReleaseStep Step(bool inHand, bool casting, float now, out GripReleaseAction action)
    {
        action = Action;
        if (!Pending)
            return ReleaseStep.Wait;
        if (!inHand)
        {
            // Never landed, or it left the hand another way (eaten, stashed, put away): nothing to let go of.
            if (held || now - queuedAt > ArriveSeconds)
                Clear();
            return ReleaseStep.Wait;
        }
        if (!held)
        {
            held = true;
            heldAt = now;
        }
        if (!casting)
        {
            Clear();
            return ReleaseStep.Run;
        }
        if (now - heldAt < CastSeconds)
            return ReleaseStep.Wait;
        Clear();
        return ReleaseStep.Refused;
    }
}
