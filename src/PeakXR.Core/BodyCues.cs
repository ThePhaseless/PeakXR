using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// The felt side of the body: how hard a landing thumps, how fall danger builds before the game's fall damage,
/// and which hand feels a push that came from one side. Pure math for player.locomotion and comfort.events.
/// </summary>
public static class BodyCues
{
    /// <summary>The softest landing thump: any landing the game counts (half a second in the air) is felt.</summary>
    public const float LandingMin = 0.2f;

    /// <summary>Impact speeds (m/s) the thump ramps over: a hop lands near the bottom, a fall that hurts at the top.</summary>
    public const float LandingSoft = 3f;
    public const float LandingHard = 12f;

    /// <summary>Gain on the hand away from a side push: still felt, clearly weaker.</summary>
    public const float FarSide = 0.3f;

    /// <summary>Seconds before the game's fall damage time that the danger cue starts.</summary>
    public const float FallLead = 0.5f;

    /// <summary>Seconds after the damage time that the danger cue reaches full urgency.</summary>
    public const float FallFull = 1f;

    /// <summary>The softest bounce-shroom spring.</summary>
    public const float BounceMin = 0.25f;

    /// <summary>Strongest steady pressure (wind, tornado pull): a lean, never a buzz that drowns other cues.</summary>
    public const float PressureMax = 0.15f;

    /// <summary>Spring amplitude for the game's bounce multiplier <paramref name="bounce"/> (1 up to
    /// <paramref name="maxBounce"/>, CollisionModifier.cs:94-110).</summary>
    public static float BounceAmplitude(float bounce, float maxBounce)
    {
        float t = maxBounce > 1f ? (bounce - 1f) / (maxBounce - 1f) : 1f;
        t = float.IsNaN(t) ? 0f : MathF.Max(0f, MathF.Min(1f, t));
        return BounceMin + (1f - BounceMin) * t;
    }

    /// <summary>Pressure amplitude for a push of <paramref name="strength"/> (0..1). Crouched, the game pulls a
    /// quarter as hard (Tornado.cs:296-300), and the hands feel it drop: the brace works.</summary>
    public static float PullAmplitude(float strength, bool crouched = false)
    {
        float s = float.IsNaN(strength) ? 0f : MathF.Max(0f, MathF.Min(1f, strength));
        return PressureMax * s * (crouched ? 0.25f : 1f);
    }

    /// <summary>Urgency (0..1) of something ending in <paramref name="secondsLeft"/>: 0 until the last
    /// <paramref name="window"/> seconds, then rising to 1 at the end.</summary>
    public static float CountdownUrgency(float secondsLeft, float window)
    {
        if (!(window > 0f) || !(secondsLeft < window))
            return 0f;
        return MathF.Min(1f, 1f - MathF.Max(0f, secondsLeft) / window);
    }

    /// <summary>Thump amplitude (0..1) for a landing at <paramref name="downSpeed"/> m/s.</summary>
    public static float LandingAmplitude(float downSpeed)
    {
        if (!(downSpeed > LandingSoft))
            return LandingMin;
        float t = MathF.Min(1f, (downSpeed - LandingSoft) / (LandingHard - LandingSoft));
        return LandingMin + (1f - LandingMin) * t;
    }

    /// <summary>
    /// Fall danger urgency (0..1) for the game's fall time (CharacterMovement.FallTime), quiet until
    /// <see cref="FallLead"/> before <paramref name="damageTime"/>, full <see cref="FallFull"/> after it.
    /// </summary>
    public static float FallUrgency(float fallTime, float damageTime)
    {
        float start = damageTime - FallLead;
        float t = (fallTime - start) / (FallLead + FallFull);
        return t > 0f ? MathF.Min(1f, t) : 0f;
    }

    /// <summary>
    /// Per-hand gains for something felt from <paramref name="towardSource"/> (flat x/z, any length): the hand on
    /// that side gets 1, the other down to <see cref="FarSide"/>; ahead, behind or unknown gives both 1.
    /// </summary>
    /// <param name="headRight">The head's right axis, flat x/z, unit length.</param>
    public static void SideGains(
        Vector2 towardSource,
        Vector2 headRight,
        out float left,
        out float right
    )
    {
        left = right = 1f;
        float len = towardSource.Length();
        if (!(len > 1e-4f))
            return;
        float side = Vector2.Dot(towardSource / len, headRight);
        if (side > 0f)
            left = 1f - side * (1f - FarSide);
        else
            right = 1f + side * (1f - FarSide);
    }
}

/// <summary>
/// The stamina limit felt in the body: running out while sprinting or right after a jump, or asking to sprint with
/// an empty bar, fires once; holding the ask, or staying empty, does not repeat. At most once per
/// <see cref="Repeat"/> seconds.
/// </summary>
public sealed class StaminaOutCue
{
    public float Repeat = 1f;

    bool _wasOut;
    bool _wasWanted;
    float _at = float.NegativeInfinity;

    /// <param name="outOfStamina">The game's regular stamina is empty (Character.OutOfRegularStamina).</param>
    /// <param name="exerting">Sprinting last frame or just jumped: the body spent the last of it.</param>
    /// <param name="sprintWanted">A sprint is asked for this frame (button, toggle press or arm swing, moving).</param>
    public bool Update(bool outOfStamina, bool exerting, bool sprintWanted, float now)
    {
        bool fire =
            outOfStamina
            && ((exerting && !_wasOut) || (sprintWanted && !_wasWanted))
            && now - _at >= Repeat;
        _wasOut = outOfStamina;
        _wasWanted = sprintWanted;
        if (fire)
            _at = now;
        return fire;
    }
}

/// <summary>One heartbeat pulse: the first ("lub", <see cref="Beat"/>) or the softer echo ("dub").</summary>
public readonly struct BeatPulse
{
    public readonly bool Beat;
    public readonly float Amplitude;
    public readonly float Seconds;

    public BeatPulse(bool beat, float amplitude, float seconds)
    {
        Beat = beat;
        Amplitude = amplitude;
        Seconds = seconds;
    }
}

/// <summary>
/// The body breathing hard: while stamina is being spent, a lub-dub heartbeat below <see cref="Start"/> of the bar,
/// faster and stronger as it empties. A heavy load (the Weight status) slows and deepens it, and is felt from the
/// first step. Quiet at rest.
/// </summary>
public sealed class Heartbeat
{
    public float Start = 0.6f;
    public float SlowPeriod = 1.1f;
    public float FastPeriod = 0.45f;
    public float Soft = 0.12f;
    public float Hard = 0.3f;
    public float DubDelay = 0.16f;

    float _toBeat;
    float _toDub = -1f;
    float _dubAmplitude;

    /// <param name="exerting">Stamina spent in the last moment (sprint, jump, a carry), not climbing.</param>
    /// <param name="stamina">Current stamina as a fraction of the bar.</param>
    /// <param name="weight">The Weight status (0..1).</param>
    /// <returns>True when a pulse plays this frame.</returns>
    public bool Update(bool exerting, float stamina, float weight, float dt, out BeatPulse pulse)
    {
        pulse = default;
        if (_toDub >= 0f)
        {
            _toDub -= dt;
            if (_toDub < 0f)
            {
                pulse = new BeatPulse(false, _dubAmplitude, 0.04f);
                return true;
            }
        }
        float w = float.IsNaN(weight) ? 0f : MathF.Max(0f, MathF.Min(1f, weight));
        if (!exerting || !(stamina < Start || w > 0.1f))
        {
            _toBeat = 0f;
            return false;
        }
        _toBeat -= dt;
        if (_toBeat > 0f)
            return false;
        float effort = MathF.Max(0f, MathF.Min(1f, (Start - stamina) / Start));
        float amplitude = MathF.Min(1f, Soft + (Hard - Soft) * effort + 0.25f * w);
        _toBeat = (SlowPeriod + (FastPeriod - SlowPeriod) * effort) * (1f + w);
        _toDub = DubDelay;
        _dubAmplitude = amplitude * 0.6f;
        pulse = new BeatPulse(true, amplitude, 0.05f + 0.04f * w);
        return true;
    }

    public void Reset()
    {
        _toBeat = 0f;
        _toDub = -1f;
    }
}

/// <summary>Shivering: fine, fast pulses while the Cold status is rising (and for <see cref="Hold"/> seconds after
/// a step up), once it is past <see cref="MinCold"/>. Steady or warming cold is quiet. The gaps jitter between 36 and
/// 57 ms (<see cref="Gaps"/>): a trembling texture, never the refusal's even 70 ms triple
/// (<see cref="DenyHaptics.Deny"/>).</summary>
public sealed class Shiver
{
    public float MinCold = 0.05f;
    public float Hold = 1f;

    /// <summary>The pulse gaps (s), played in turn: irregular and all well off the refusal's 70 ms.</summary>
    static readonly float[] Gaps = { 0.045f, 0.038f, 0.057f, 0.041f, 0.052f, 0.036f, 0.049f };

    int _gap;

    float _last = float.NaN;
    float _since = float.PositiveInfinity;
    float _timer;

    /// <returns>Pulse amplitude this frame, 0 for none.</returns>
    public float Update(float cold, float dt)
    {
        _since = !float.IsNaN(_last) && cold > _last + 1e-5f ? 0f : _since + dt;
        _last = cold;
        if (!(cold >= MinCold) || _since > Hold)
        {
            _timer = 0f;
            return 0f;
        }
        _timer -= dt;
        if (_timer > 0f)
            return 0f;
        _timer = Gaps[_gap];
        _gap = (_gap + 1) % Gaps.Length;
        return 0.05f + 0.1f * MathF.Min(1f, cold);
    }
}

/// <summary>
/// A real slope slide: the game's slide flag (sinceFallSlide reset, CharacterMovement.cs:906-919) while off the
/// ground and moving down, held for <see cref="Hold"/>. The game also resets the flag when its hip ray meets a steep
/// edge under feet that stand (a rock lip, debris): that is no slide. Fires once; re-arms after
/// <see cref="Rearm"/> seconds without one.
/// </summary>
public sealed class SlideCue
{
    public float Hold = 0.2f;
    public float Rearm = 1f;
    public float DownSpeed = 0.5f;

    float _for;
    float _quiet = float.PositiveInfinity;
    bool _armed = true;

    /// <param name="flag">The game's slide flag this frame.</param>
    /// <param name="grounded">CharacterData.isGrounded.</param>
    /// <param name="downSpeed">Downward speed (m/s, positive down).</param>
    public bool Update(bool flag, bool grounded, float downSpeed, float dt)
    {
        if (flag && !grounded && downSpeed > DownSpeed)
        {
            _for += dt;
            _quiet = 0f;
        }
        else
        {
            _for = 0f;
            _quiet += dt;
            if (_quiet >= Rearm)
                _armed = true;
        }
        if (!_armed || _for < Hold)
            return false;
        _armed = false;
        return true;
    }
}

/// <summary>
/// Which hand shows a body refusal (a slide, a refused jump, out of breath). ui.affordance draws one refusal ring
/// at a time, so the body's cue goes to a hand that is not busy (holding or gripping) and has no cue of its own up;
/// with both hands busy it takes the preferred one only when that hand's own cue is not showing, else it yields.
/// </summary>
public static class BodyCueHand
{
    public static bool Pick(
        bool preferLeft,
        bool leftBusy,
        bool rightBusy,
        bool leftCueUp,
        bool rightCueUp,
        out bool left
    )
    {
        bool prefBusy = preferLeft ? leftBusy : rightBusy;
        bool prefCue = preferLeft ? leftCueUp : rightCueUp;
        bool otherBusy = preferLeft ? rightBusy : leftBusy;
        bool otherCue = preferLeft ? rightCueUp : leftCueUp;
        left = preferLeft;
        if (!prefBusy && !prefCue)
            return true;
        if (!otherBusy && !otherCue)
        {
            left = !preferLeft;
            return true;
        }
        return !prefCue;
    }
}

/// <summary>Footing back: ragdoll control (CharacterData.currentRagdollControll) climbs past
/// <see cref="Regained"/> after it fell below <see cref="Lost"/>. A wobble above <see cref="Lost"/> is no fall.</summary>
public sealed class FootingCue
{
    public float Lost = 0.5f;
    public float Regained = 0.9f;

    bool _down;

    public bool Update(float control)
    {
        if (control < Lost)
            _down = true;
        else if (_down && control > Regained)
        {
            _down = false;
            return true;
        }
        return false;
    }
}
