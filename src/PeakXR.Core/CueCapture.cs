using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// The last refusal or notice cue PeakXR emitted, channel by channel, so the self-test can prove a player got all of
/// it: the reason, the ring the eye sees, the sound and the haptic. Recording only overwrites a few fields (no
/// allocation), so it stays compiled into normal play. Times are one clock (the game's unscaled time).
/// <para>Stable API, used by ui.affordance and the self-test:</para>
/// <list type="bullet">
/// <item><see cref="Reason"/>, <see cref="Ring"/>, <see cref="Pulse"/>, <see cref="Sound"/>: record one emission.</item>
/// <item><see cref="Feel"/>: record a non-refusal haptic (<see cref="CueFeel"/>); kept apart from refusals.</item>
/// <item><see cref="Fired(bool, DenyReason, float, out string)"/>: every channel fired for that reason on that hand
/// since a time; the overload with <c>tint</c> also needs the red preview seen.</item>
/// <item><see cref="Felt"/>: that feel reached that hand since a time.</item>
/// <item><see cref="Quiet"/>: no refusal channel fired on that hand since a time (feels do not count).</item>
/// <item><see cref="Hand"/>, <see cref="LastSound"/>: the raw records, for notes.</item>
/// </list>
/// Sounds are not per hand (the SFX call has no hand): <see cref="Fired(bool, DenyReason, float, out string)"/> and
/// <see cref="Quiet"/> read the shared one. A pulse withheld because the player turned haptics off
/// (<c>muted</c>, decision D9) counts as delivered: the visual and audio channels carry the cue then.
/// </summary>
public sealed class CueCapture
{
    private CueHand left;
    private CueHand right;
    private readonly CueSoundShot[] sounds = new CueSoundShot[(int)CueSound.Danger + 1];

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
        if (tint)
            h.TintAt = time;
    }

    /// <summary>A refusal's haptic pulse; <paramref name="delivered"/> false when the device took no impulse,
    /// <paramref name="muted"/> true when <c>Interaction.Haptics</c> is off and it was withheld on purpose.</summary>
    public void Pulse(
        bool isLeft,
        float amplitude,
        float seconds,
        bool delivered,
        float time,
        bool muted = false
    )
    {
        ref var h = ref Hand(isLeft);
        h.Amplitude = amplitude;
        h.Seconds = seconds;
        h.Delivered = delivered;
        h.Muted = muted;
        h.PulseAt = time;
    }

    /// <summary>A non-refusal haptic (progress, contact, cancel, end stop, danger) on this hand.</summary>
    public void Feel(
        bool isLeft,
        CueFeel feel,
        float amplitude,
        bool delivered,
        bool muted,
        float time
    )
    {
        ref var h = ref Hand(isLeft);
        int i = (int)feel;
        if (i < 0 || i >= CueHand.FeelCount)
            return;
        h.FeelAt[i] = time;
        h.FeelAmplitude[i] = amplitude;
        h.FeelDelivered[i] = delivered || muted;
    }

    /// <summary>A cue sound; <paramref name="played"/> false when neither the game's SFX player nor the fallback
    /// source could play it.</summary>
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
    /// a notice) and a haptic pulse reached the hand (or was muted by the setting). <paramref name="why"/> names the
    /// first missing channel, or describes the cue.
    /// </summary>
    public bool Fired(bool isLeft, DenyReason reason, float since, out string why) =>
        Fired(isLeft, reason, since, false, out why);

    /// <summary>As <see cref="Fired(bool, DenyReason, float, out string)"/>; with <paramref name="tint"/> the red
    /// preview of the refused thing must also have been on while the ring was drawn.</summary>
    public bool Fired(bool isLeft, DenyReason reason, float since, bool tint, out string why)
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
        if (tint && h.TintAt < since)
        {
            why = $"no red preview on the refused thing for {reason}";
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
        if (!h.Delivered && !h.Muted)
        {
            why = "haptic pulse not delivered (no device or no impulse support)";
            return false;
        }
        why =
            $"{reason}: ring {h.Glyph} {h.RingDegrees:0.0} deg{(h.TintAt >= since ? " tinted" : "")}, sound {kind}, "
            + (
                h.Muted
                    ? "pulse muted (Interaction.Haptics off)"
                    : $"pulse {h.Amplitude:0.00} x {h.Seconds:0.00} s"
            );
        return true;
    }

    /// <summary>True when <paramref name="feel"/> reached this hand (or was muted) since <paramref name="since"/>.</summary>
    public bool Felt(bool isLeft, CueFeel feel, float since, out string why)
    {
        ref var h = ref Hand(isLeft);
        int i = (int)feel;
        string hand = isLeft ? "left" : "right";
        if (i < 0 || i >= CueHand.FeelCount || h.FeelAt[i] < since)
        {
            why = $"no {feel} on the {hand} hand";
            return false;
        }
        if (!h.FeelDelivered[i])
        {
            why = $"{feel} pulse not delivered (no device or no impulse support)";
            return false;
        }
        why = $"{feel} pulse {h.FeelAmplitude[i]:0.00}";
        return true;
    }

    /// <summary>True when no refusal fired on this hand since <paramref name="since"/>: no reason, ring or refusal
    /// pulse. Sounds are left out (they have no hand, and a hover elsewhere plays one), and so are feels (a contact
    /// tick is not a refusal). <paramref name="why"/> names what fired.</summary>
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
    public const int FeelCount = (int)CueFeel.Danger + 1;

    public DenyReason Reason;
    public bool Notice;
    public float ReasonAt;

    public DenyReason RingReason;
    public CueGlyph Glyph;
    public Vector3 RingCenter;
    public float RingDegrees;
    public bool Tint;
    public float RingAt;

    /// <summary>Last time a drawn ring had the red preview on.</summary>
    public float TintAt;

    public float Amplitude;
    public float Seconds;
    public bool Delivered;
    public bool Muted;
    public float PulseAt;

    /// <summary>Per <see cref="CueFeel"/>: when, how strong, and whether it reached the hand (or was muted).</summary>
    public float[] FeelAt;
    public float[] FeelAmplitude;
    public bool[] FeelDelivered;

    public static CueHand Empty
    {
        get
        {
            var h = new CueHand
            {
                ReasonAt = float.NegativeInfinity,
                RingAt = float.NegativeInfinity,
                TintAt = float.NegativeInfinity,
                PulseAt = float.NegativeInfinity,
                FeelAt = new float[FeelCount],
                FeelAmplitude = new float[FeelCount],
                FeelDelivered = new bool[FeelCount],
            };
            for (int i = 0; i < FeelCount; i++)
                h.FeelAt[i] = float.NegativeInfinity;
            return h;
        }
    }
}

/// <summary>The last request for one cue sound.</summary>
public struct CueSoundShot
{
    public CueSound Sound;
    public Vector3 Position;
    public bool Played;
    public float At;
}
