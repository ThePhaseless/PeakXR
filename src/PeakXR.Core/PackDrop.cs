namespace PeakXR.Core;

/// <summary>
/// A held item let go into a backpack on the ground or on a friend's back goes into its first free pocket, through
/// the game's own stash (the backpack wheel's <c>TryStash</c>). Otherwise the release is refused with the reason,
/// and the item stays in hand.
/// </summary>
public static class PackDrop
{
    /// <summary>Why the item cannot go in, or None. A busy hand says why first (the switch gates), then an item the
    /// wheel refuses (<c>canBackpack</c>), then a pack with no free pocket (<paramref name="freePocket"/> below 0).</summary>
    public static DenyReason Refusal(
        bool switchBlocked,
        DenyReason blockReason,
        bool canBackpack,
        int freePocket
    ) =>
        switchBlocked ? blockReason
        : !canBackpack ? DenyReason.CantHolster
        : freePocket < 0 ? DenyReason.PackFull
        : DenyReason.None;
}
