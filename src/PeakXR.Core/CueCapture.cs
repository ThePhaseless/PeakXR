using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// The last refusal or notice cue PeakXR emitted, channel by channel, so the self-test can prove a player got all of
/// it: the reason, the ring the eye sees, the sound and the haptic. Recording only overwrites a few fields (no
/// allocation), so it stays compiled into normal play. Times are one clock (the game's unscaled time).
/// <para>Stable API, used by ui.affordance and the self-test:</para>
/// <list type="bullet">
/// <item><see cref="Reason"/>, <see cref="Ring"/>, <see cref="Pulse"/>, <see cref="Sound"/>: record one emission.</item>
/// <item><see cref="Fired"/>: every channel fired for that reason on that hand since a time.</item>
/// <item><see cref="Quiet"/>: no channel fired on that hand since a time.</item>
/// <item><see cref="Hand"/>, <see cref="LastSound"/>: the raw records, for notes.</item>
/// </list>
/// Sounds are not per hand (the SFX call has no hand): <see cref="Fired"/> and <see cref="Quiet"/> read the shared one.
/// </summary>
public sealed class CueCapture
{
    private CueHand left;
    private CueHand right;
    private readonly CueSoundShot[] sounds = new CueSoundShot[3];

    public CueCapture() => Clear();

    /// <summary>One hand's record; <c>float.NegativeInfinity</c> times mean never.</summary>
    public ref CueHand Hand(bool isLeft) => ref isLeft ? ref left : ref right;

    /// <summary>The last time <paramref name="sound"/> was asked for.</summary>
    public CueSoundShot LastSound(CueSound sound) => sounds[(int)sound];

    public void Clear()
    {
        left = CueHand.Empty;
        right = CueHand.Empty;
        for (int i = 0; i < sounds.Length; i++)
            sounds[i] = new CueSoundShot { At = float.NegativeInfinity };
    }

    /// <summary>A refusal (or a notice) was accepted for this hand.</summary>
    public void Reason(bool isLeft, DenyReason reason, bool notice, float time)
    {
        ref var h = ref Hand(isLeft);
        h.Reason = reason;
        h.Notice = notice;
        h.ReasonAt = time;
    }

    /// <summary>This frame the cue's ring is drawn: its centre and angular diameter from the eye; whether the red
    /// preview is on.</summary>
    public void Ring(
        bool isLeft,
        DenyReason reason,
        CueGlyph glyph,
        Vector3 center,
        float degrees,
        bool tint,
        float time
    )
    {
        ref var h = ref Hand(isLeft);
        h.RingReason = reason;
        h.Glyph = glyph;
        h.RingCenter = center;
        h.RingDegrees = degrees;
        h.Tint = tint;
        h.RingAt = time;
    }

    /// <summary>A cue haptic pulse; <paramref name="delivered"/> false when the device took no impulse.</summary>
    public void Pulse(bool isLeft, float amplitude, float seconds, bool delivered, float time)
    {
        ref var h = ref Hand(isLeft);
        h.Amplitude = amplitude;
        h.Seconds = seconds;
        h.Delivered = delivered;
        h.PulseAt = time;
    }

    /// <summary>A cue sound; <paramref name="played"/> false when the game's SFX player could not play it.</summary>
    public void Sound(CueSound sound, Vector3 position, bool played, float time) =>
        sounds[(int)sound] = new CueSoundShot
        {
            Sound = sound,
            Position = position,
            Played = played,
            At = time,
        };

    /// <summary>
    /// True when, since <paramref name="since"/>, <paramref name="reason"/> was reported for this hand and its ring was
    /// drawn at a readable size (<see cref="CueLayout.MinDegrees"/>), its sound played (refused, or the notice bell for
    /// a notice) and a haptic pulse reached the hand. <paramref name="why"/> names the first missing channel, or
    /// describes the cue.
    /// </summary>
    public bool Fired(bool isLeft, DenyReason reason, float since, out string why)
    {
        ref var h = ref Hand(isLeft);
        string hand = isLeft ? "left" : "right";
        if (h.ReasonAt < since || h.Reason != reason)
        {
            why =
                $"no reason {reason} on the {hand} hand"
                + (h.ReasonAt >= since ? $" (got {h.Reason})" : "");
            return false;
        }
        if (h.RingAt < since || h.RingReason != reason)
        {
            why = $"no ring drawn for {reason}";
            return false;
        }
        if (h.RingDegrees < CueLayout.MinDegrees)
        {
            why = $"ring too small: {h.RingDegrees:0.0} deg (min {CueLayout.MinDegrees:0.0})";
            return false;
        }
        var kind = h.Notice ? CueSound.Notice : CueSound.Refused;
        var s = sounds[(int)kind];
        if (s.At < since)
        {
            why = $"no sound ({kind})";
            return false;
        }
        if (!s.Played)
        {
            why = $"sound {kind} not played (no SFX player)";
            return false;
        }
        if (h.PulseAt < since)
        {
            why = "no haptic pulse";
            return false;
        }
        if (!h.Delivered)
        {
            why = "haptic pulse not delivered (device took no impulse)";
            return false;
        }
        why =
            $"{reason}: ring {h.Glyph} {h.RingDegrees:0.0} deg{(h.Tint ? " tinted" : "")}, sound {kind}, "
            + $"pulse {h.Amplitude:0.00} x {h.Seconds:0.00} s";
        return true;
    }

    /// <summary>True when nothing fired on this hand since <paramref name="since"/>: no reason, ring or pulse. Sounds
    /// are left out: they have no hand, and a hover elsewhere plays one. <paramref name="why"/> names what fired.</summary>
    public bool Quiet(bool isLeft, float since, out string why)
    {
        ref var h = ref Hand(isLeft);
        why =
            h.ReasonAt >= since ? $"reason {h.Reason} reported"
            : h.RingAt >= since ? $"ring drawn for {h.RingReason}"
            : h.PulseAt >= since ? $"haptic pulse {h.Amplitude:0.00}"
            : null;
        return why == null;
    }
}

/// <summary>One hand's last cue, per channel.</summary>
public struct CueHand
{
    public DenyReason Reason;
    public bool Notice;
    public float ReasonAt;

    public DenyReason RingReason;
    public CueGlyph Glyph;
    public Vector3 RingCenter;
    public float RingDegrees;
    public bool Tint;
    public float RingAt;

    public float Amplitude;
    public float Seconds;
    public bool Delivered;
    public float PulseAt;

    public static CueHand Empty =>
        new()
        {
            ReasonAt = float.NegativeInfinity,
            RingAt = float.NegativeInfinity,
            PulseAt = float.NegativeInfinity,
        };
}

/// <summary>The last request for one cue sound.</summary>
public struct CueSoundShot
{
    public CueSound Sound;
    public Vector3 Position;
    public bool Played;
    public float At;
}
