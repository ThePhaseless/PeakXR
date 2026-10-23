using System;

namespace PeakXR.Core;

/// <summary>
/// How much a pulse matters when two want the same hand: an OpenXR impulse replaces the one playing, so without an
/// order a wind gust or heartbeat could cut a refusal's triple tick in half.
/// </summary>
public enum HapticPriority : byte
{
    /// <summary>Background feel: heartbeat, wind, surface texture, shiver, warmth. Ducks under everything else.</summary>
    Ambient,

    /// <summary>Feedback on what the hand did: contact, progress, a grab, a use.</summary>
    Cue,

    /// <summary>A refusal or a danger warning: never cut by anything lower.</summary>
    Alert,
}

/// <summary><c>Interaction.HapticDetail</c>: Essential keeps only what the hand did and what it must know (cues,
/// refusals, danger), for players who find constant background feel tiring or overwhelming.</summary>
public enum HapticDetail
{
    Essential,
    Full,
}

/// <summary>
/// The one strength slider (<c>Interaction.HapticStrength</c>) for every vibration. Its default was 0.5, which
/// halved every cue; it is now 1 with the cues' built-in amplitudes scaled by <see cref="Cue"/>, so a default player
/// feels exactly what they felt before. UI ticks used to ignore the slider and go out as written, so they scale by
/// <see cref="Ui"/>: unchanged at the default, and now following the slider.
/// </summary>
public static class HapticScale
{
    public const float DefaultStrength = 1f;
    public const float OldDefaultStrength = 0.5f;

    /// <summary>Top of the slider: twice the default, so the old maximum (1 on the old scale) stays reachable.</summary>
    public const float MaxStrength = 2f;

    /// <summary>Gameplay cue amplitudes were tuned at the old 0.5 default.</summary>
    public const float Cue = OldDefaultStrength / DefaultStrength;

    /// <summary>UI tick amplitudes were sent unscaled, as at strength 1.</summary>
    public const float Ui = 1f / DefaultStrength;

    /// <summary>A pulse of <paramref name="priority"/> plays at this <see cref="HapticDetail"/>: Essential drops the
    /// Ambient feel (heartbeat, wind, texture, shiver, warmth).</summary>
    public static bool Plays(HapticDetail detail, HapticPriority priority) =>
        detail == HapticDetail.Full || priority != HapticPriority.Ambient;

    /// <summary>The impulse amplitude sent for <paramref name="amplitude"/> at slider <paramref name="strength"/>.</summary>
    public static float Felt(float amplitude, float strength, float scale) =>
        Math.Clamp(amplitude * strength * scale, 0f, 1f);
}

/// <summary>
/// One hand's haptic channel: which pulse plays now, until when, at what priority. A pulse of lower priority than
/// the one still playing is dropped; ambient feel also waits <see cref="DuckTail"/> after any cue, so the cue reads
/// alone. A pattern (the refusal triple) holds the channel for its whole length with <see cref="Hold"/>, gaps
/// included. Plain fields: allocation-free.
/// </summary>
public struct HapticChannel
{
    /// <summary>Seconds after a cue or alert ends during which ambient feel stays quiet.</summary>
    public const float DuckTail = 0.1f;

    private float until;
    private HapticPriority priority;

    /// <summary>
    /// Whether a pulse of <paramref name="p"/> lasting <paramref name="seconds"/> may go out at
    /// <paramref name="now"/>; when it may, it takes the channel.
    /// </summary>
    public bool Allow(HapticPriority p, float now, float seconds)
    {
        float tail =
            p == HapticPriority.Ambient && priority > HapticPriority.Ambient ? DuckTail : 0f;
        if (now < until + tail && p < priority)
            return false;
        Take(p, now, seconds);
        return true;
    }

    /// <summary>Reserves the channel for <paramref name="seconds"/> at <paramref name="p"/> (a pattern's length)
    /// without sending anything. Ignored while a higher priority is still playing.</summary>
    public void Hold(HapticPriority p, float now, float seconds)
    {
        if (now < until && p < priority)
            return;
        Take(p, now, seconds);
    }

    private void Take(HapticPriority p, float now, float seconds)
    {
        float end = now + Math.Max(seconds, 0f);
        // The same priority keeps a later end (a held pattern's own pulses); a new priority or a free channel starts over.
        until = now < until && p == priority ? Math.Max(until, end) : end;
        priority = p;
    }
}
