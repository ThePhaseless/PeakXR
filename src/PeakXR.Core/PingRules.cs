namespace PeakXR.Core;

/// <summary>What a ping press gets: the ping, or the reason PointPinger would drop it silently.</summary>
public enum PingVerdict : byte
{
    Fire,

    /// <summary>Not fully conscious (passed out, dying): PointPinger.canPing is false.</summary>
    NotNow,

    /// <summary>Still in the pinger's cooldown.</summary>
    TooSoon,
}

/// <summary>Which hand aims a ping (setting <c>Interaction.Ping.Hand</c>).</summary>
public enum PingAimHand : byte
{
    Pointing,
    Right,
    Left,
}

/// <summary>
/// The game's ping rules, so a VR press that the game would ignore gets a cue instead of nothing. Mirrors
/// PointPinger.canPing (fully conscious and not in cooldown, PointPinger.cs:24,38-48); the sky (no terrain under the
/// ray) is decided by the ray cast itself.
/// </summary>
public static class PingRules
{
    public static PingVerdict Press(
        bool fullyConscious,
        float now,
        float lastPinged,
        float coolDown
    ) =>
        !fullyConscious ? PingVerdict.NotNow
        : now - lastPinged < coolDown ? PingVerdict.TooSoon
        : PingVerdict.Fire;

    /// <summary>
    /// The hand a button ping aims from: the fixed hand, else the one hand that points, else the off hand of the layout
    /// (where the table's ping button sits).
    /// </summary>
    public static bool ButtonHandLeft(
        PingAimHand setting,
        bool leftPointing,
        bool rightPointing,
        bool dominantLeft
    ) =>
        setting switch
        {
            PingAimHand.Left => true,
            PingAimHand.Right => false,
            _ => leftPointing || (!rightPointing && !dominantLeft),
        };

    /// <summary>
    /// A holding hand made the finger gun, but only an empty hand pings: when its squeeze did nothing (the item has no
    /// use) and the other hand is free, that free hand gets a notice so the next try goes there.
    /// </summary>
    public static bool UseFreeHand(bool gesture, bool itemHasUse, bool otherHandFree) =>
        gesture && !itemHasUse && otherHandFree;
}
