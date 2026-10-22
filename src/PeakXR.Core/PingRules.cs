using System;

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

    /// <summary>
    /// The dotted aim ray: dots every <paramref name="spacing"/> metres (the first half a spacing out), at most
    /// <paramref name="max"/>; a longer ray spreads <paramref name="max"/> dots evenly over its whole length. Dot i
    /// sits at <c>step * (i + 0.5)</c>.
    /// </summary>
    public static void Dots(float length, float spacing, int max, out int count, out float step)
    {
        step = spacing;
        count = length > 0f && spacing > 0f ? (int)MathF.Floor(length / spacing + 1e-4f) : 0;
        if (count > max)
        {
            count = max;
            step = length / max;
        }
    }
}

/// <summary>
/// When the layout's ping button shows the aim preview: while the thumb rests on it or presses it, until the press
/// becomes the long press (the emote wheel on the same button), which hides it until the thumb lets go.
/// </summary>
public sealed class PingButtonPreview
{
    private bool longPressed;

    /// <param name="allowed">Player input is live (no wheel or window blocks it). False hides the preview but still
    /// tracks the thumb, so the emote wheel the long press opened does not clear the long press.</param>
    public bool Update(
        bool touched,
        bool held,
        float heldSeconds,
        float longPressSeconds,
        bool allowed = true
    )
    {
        if (held && heldSeconds >= longPressSeconds)
            longPressed = true;
        if (!touched && !held)
            longPressed = false;
        return allowed && !longPressed && (touched || held);
    }

    public void Reset() => longPressed = false;
}
