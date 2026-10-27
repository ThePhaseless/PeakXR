namespace PeakXR.Core;

/// <summary>
/// The game's put-away of a held item it cannot pocket (<c>CharacterItems.EquipSlot</c>, CharacterItems.cs:498-505)
/// drops it at once, then sends its own <c>EquipSlotRpc</c> <see cref="Delay"/> later (:510-538). That RPC stashes
/// and destroys whatever is in the hand by then (:568-572). An item the player equips in between (a grab with the
/// other hand as the climb starts, a belt draw) would vanish from the hand. While this is pending, PeakXR holds its
/// own equips back and lets them through once it has landed.
/// </summary>
public sealed class PutAwayWindow
{
    /// <summary>The put-away's wait before its RPC (WaitForSecondsRealtime, CharacterItems.cs:512), seconds.</summary>
    public const float Delay = 0.15f;

    /// <summary>How long past <see cref="Delay"/> an equip is held back when the RPC never shows, seconds.</summary>
    public const float Slack = 0.25f;

    /// <summary>The slot id the put-away's RPC carries for empty hands (EquipSlot(None), CharacterItems.cs:536).</summary>
    public const int NoSlot = -1;

    private float deadline = float.NegativeInfinity;
    private int slot;

    /// <summary>A put-away that drops the held item started at <paramref name="now"/> (realtime), toward
    /// <paramref name="toSlot"/> (<see cref="NoSlot"/> for empty hands).</summary>
    public void Begin(float now, int toSlot)
    {
        deadline = now + Delay + Slack;
        slot = toSlot;
    }

    /// <summary>The local character's <c>EquipSlotRpc</c> for <paramref name="rpcSlot"/> arrived.</summary>
    public void Landed(int rpcSlot)
    {
        if (rpcSlot == slot)
            deadline = float.NegativeInfinity;
    }

    /// <summary>An equip now would be stashed by the pending RPC: hold it back.</summary>
    public bool Pending(float now) => now < deadline;
}
