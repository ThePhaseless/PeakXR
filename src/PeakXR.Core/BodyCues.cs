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
