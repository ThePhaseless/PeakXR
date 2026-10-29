using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Arm climbing as travel owed: the hands' pull (wall right, wall up; metres) is banked and the stick pays it off
/// through the game's own climb (<c>CharacterClimbing.GetRequestedPostition</c>), so the climber ends where the
/// hands took it. A per-frame velocity-to-stick mapping lost distance three ways: a pull faster than the game's
/// full-stick speed was clipped for good, a slow one fell in the dead zone, and any negative stick drops at
/// 3 x climbSpeed (CharacterClimbing.cs:303-305), so a short push overshot several times. Here the stick runs at
/// up to full until the climber catches up (capped at <see cref="MaxOwed"/>), and the drop comes in whole game
/// steps, each only once at least half a step is owed. Allocation-free: three floats of state.
/// </summary>
public sealed class ClimbDebt
{
    /// <summary>Seconds of full-speed travel the stick ramps over: owed / (speed x Lead), so the climber follows
    /// the hand this far behind and slows to a stop on it.</summary>
    public const float Lead = 0.06f;

    /// <summary>Most travel banked, metres: about one long pull, so the body never runs on far past still hands.</summary>
    public const float MaxOwed = 0.5f;

    /// <summary>Owed travel, metres, too small to move for: tracking jitter (about a millimetre) stays inside it, so
    /// still hands neither creep up nor ratchet into a drop.</summary>
    public const float Hold = 0.005f;

    private Vector2 owed;

    /// <summary>A drop overshot by this much (metres): the next push down uses it up first.</summary>
    private float credit;

    /// <summary>Travel still owed (wall right, wall up), metres.</summary>
    public Vector2 Owed => owed;

    /// <summary>Banks the hands' pull this frame: the body travel they ask for, on the wall plane (metres).</summary>
    /// <param name="yMoves">As for <see cref="Pay"/>: if the game ignores the stick's y now, the up and down pull is
    /// dropped.</param>
    public void Add(Vector2 pull, bool yMoves)
    {
        float y = yMoves ? pull.Y : 0f;
        if (y < 0f)
        {
            float used = MathF.Min(credit, -y);
            credit -= used;
            y += used;
        }
        else if (y > 0f)
            credit = 0f;
        owed += new Vector2(pull.X, y);
        float len = owed.Length();
        if (len > MaxOwed)
            owed *= MaxOwed / len;
    }

    /// <summary>
    /// Takes off what the game moved with <paramref name="stick"/> over <paramref name="seconds"/> of fixed steps.
    /// An overpaid axis stops at zero (never turns into travel back); an overshot drop becomes credit.
    /// </summary>
    /// <param name="speed">Wall travel at full stick, x and up (<see cref="ClimbInput.WallSpeed"/>).</param>
    /// <param name="downSpeed">Wall travel for any negative y (<see cref="ClimbInput.DownSpeed"/>).</param>
    /// <param name="yMoves">The game moves along the wall's up this step (CharacterClimbing.cs:297-301: no
    /// slide-only surface, 0.5 s past a climb jump, stamina left). If not, the up debt is dropped, not banked.</param>
    public void Pay(Vector2 stick, float speed, float downSpeed, bool yMoves, float seconds)
    {
        if (seconds <= 0f)
            return;
        owed.X = Toward(owed.X, stick.X * speed * seconds);
        if (!yMoves)
        {
            owed.Y = 0f;
            credit = 0f;
        }
        else if (stick.Y < 0f)
        {
            owed.Y += downSpeed * seconds;
            if (owed.Y > 0f)
            {
                credit += owed.Y;
                owed.Y = 0f;
            }
        }
        else
            owed.Y = Toward(owed.Y, stick.Y * speed * seconds);
    }

    /// <summary>The climb stick for the next frame: x and up proportional to what is owed, full past
    /// <see cref="Lead"/> seconds of travel; y = -1 (a drop) while half a game step down or more is owed.</summary>
    /// <param name="fixedDt">The game's fixed step, seconds: one drop step travels downSpeed x fixedDt.</param>
    public Vector2 Stick(float speed, float downSpeed, float fixedDt)
    {
        if (speed <= 1e-5f)
            return Vector2.Zero;
        float ramp = speed * Lead;
        float x = MathF.Abs(owed.X) > Hold ? Math.Clamp(owed.X / ramp, -1f, 1f) : 0f;
        float y =
            downSpeed > 0f && owed.Y <= -0.5f * downSpeed * fixedDt ? -1f
            : owed.Y > Hold ? MathF.Min(owed.Y / ramp, 1f)
            : 0f;
        var stick = new Vector2(x, y);
        float len = stick.Length();
        return len > 1f ? stick / len : stick;
    }

    public void Reset()
    {
        owed = Vector2.Zero;
        credit = 0f;
    }

    /// <summary><paramref name="owe"/> less <paramref name="paid"/>, stopping at zero; a payment the other way
    /// leaves it alone.</summary>
    private static float Toward(float owe, float paid) =>
        owe > 0f && paid > 0f ? MathF.Max(owe - paid, 0f)
        : owe < 0f && paid < 0f ? MathF.Min(owe - paid, 0f)
        : owe;
}
