using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Holds the body's horizontal mass centre still while the avatar leans, against the lean's own contact creep and
/// nothing else. Bending moves the slippery ragdoll's parts against the planted feet, and the contacts push the whole
/// body sideways or toward the bend (run 30: 0.02-0.03 m over a second even with <see cref="BodyLean.LegShift"/>;
/// run 32: about 1.3 m/s^2 on a back bend), which the view rides. A clamped spring-damper acceleration (applied to
/// every part, so it moves only the mass centre, never the pose) pulls it back to where the lean started.
/// Parity with flat players comes first: the caller's <see cref="HoldGate"/> turns it off for slides, slopes, water,
/// carrying, climbing and input; it suspends itself for <see cref="Cooldown"/> after any acceleration it did not
/// cause (knockback, shoves, bounces, wind gusts); its push is capped near the measured creep, so a steady outside
/// force (wind, a tornado's pull) still moves the body; and each lean gets at most <see cref="Budget"/> of correction
/// and <see cref="MaxAfterLean"/> seconds after it.
/// </summary>
public sealed class BodyHold
{
    /// <summary>Spring (1/s^2) and damper (1/s), about critically damped: a 1.3 m/s^2 creep holds within 0.01 m.</summary>
    public float Stiffness = 150f;
    public float Damping = 24f;

    /// <summary>Largest corrective acceleration (m/s^2): the measured creep with some margin, well under a shove,
    /// wind or a tornado's pull.</summary>
    public float MaxAccel = 2f;

    /// <summary>Mass-centre acceleration (m/s^2) the lean's creep never reaches: anything above it is an outside
    /// force, and the hold stays off for <see cref="Cooldown"/>.</summary>
    public float ExternalAccel = 2.5f;

    /// <summary>Seconds the hold stays off after an outside force or a closed <see cref="HoldGate"/>.</summary>
    public float Cooldown = 1f;

    /// <summary>Corrective velocity (m/s, the integral of the push) one lean may spend: about the measured creep
    /// (1.3 m/s^2) over six seconds. A head held off the eyes is one long lean: the hold then fades out and the
    /// body settles on the creep's own rest point, a few centimetres off, instead of being held (or rocked) for good.</summary>
    public float Budget = 8f;

    /// <summary>The last part of <see cref="Budget"/> (m/s) over which the push fades out.</summary>
    public float FadeBudget = 5f;

    /// <summary>Speed (m/s) above which the body is still coasting (a follow walk ending): it is braked and the
    /// anchor moves with it, so the hold keeps the spot it stops on (run 32 pulled it 0.08 m back). Creep is slower.</summary>
    public float CoastSpeed = 0.15f;

    /// <summary>Drift (m) beyond which the hold re-anchors instead of pulling the body back.</summary>
    public float MaxOffset = 0.1f;

    /// <summary>Seconds the hold lasts at least after the lean ends: the body creeps while standing back up.</summary>
    public float ReleaseSeconds = 1f;

    /// <summary>After <see cref="ReleaseSeconds"/>, the hold ends once its push has stayed under
    /// <see cref="QuietAccel"/> (m/s^2) for <see cref="QuietSeconds"/>: the feet a lean planted can keep pushing
    /// after it (run 32 slid 0.026 m once let go). Never later than <see cref="MaxAfterLean"/>.</summary>
    public float QuietAccel = 0.3f;

    public float QuietSeconds = 0.5f;

    public float MaxAfterLean = 3f;

    public Vector2 Anchor { get; private set; }
    public bool Holding { get; private set; }

    const float Never = float.MaxValue;

    float _quiet;
    float _sinceLean = Never;
    float _sinceBlocked = Never;
    float _spent;
    Vector2 _lastVelocity;
    bool _haveVelocity;

    /// <param name="centre">Horizontal mass centre (x, z).</param>
    /// <param name="velocity">Its horizontal velocity.</param>
    /// <param name="leaning">The avatar is bent by the lean this tick.</param>
    /// <param name="free"><see cref="HoldGate.Free"/>: nothing but the lean is moving the body.</param>
    /// <returns>Acceleration (x, z) to add to every part.</returns>
    public Vector2 Update(Vector2 centre, Vector2 velocity, bool leaning, bool free, float dt)
    {
        if (!float.IsFinite(centre.X) || !float.IsFinite(centre.Y))
        {
            Holding = false;
            _haveVelocity = false;
            return Vector2.Zero;
        }
        if (!float.IsFinite(velocity.X) || !float.IsFinite(velocity.Y))
            velocity = Vector2.Zero;
        // An acceleration the creep (and the capped push) cannot make is someone else's: let it act.
        bool kicked =
            _haveVelocity && dt > 0f && (velocity - _lastVelocity).Length() > ExternalAccel * dt;
        _lastVelocity = velocity;
        _haveVelocity = true;
        if (!free || kicked)
            _sinceBlocked = 0f;
        else if (_sinceBlocked < Never)
            _sinceBlocked += dt;

        if (_sinceBlocked < Cooldown)
            _sinceLean = Never;
        else if (leaning)
        {
            if (_sinceLean == Never)
                _spent = 0f;
            _sinceLean = 0f;
        }
        else if (_sinceLean < Never)
            _sinceLean += dt;
        if (
            _sinceLean >= MaxAfterLean
            || _sinceLean >= ReleaseSeconds && (!Holding || _quiet >= QuietSeconds)
        )
            _sinceLean = Never;

        // Pushed past MaxOffset: something real moves the body. Give up for the rest of this lean rather than
        // re-anchor and pull again, which under a steady push is a sawtooth the body rocks on (run 32).
        if (Holding && Vector2.Distance(centre, Anchor) > MaxOffset)
            _spent = Budget;
        bool done = _sinceLean == Never || _spent >= Budget - 0.01f * FadeBudget;
        if (done)
        {
            Holding = false;
            _quiet = 0f;
            Anchor = centre;
            return Vector2.Zero;
        }
        if (!Holding)
        {
            Holding = true;
            Anchor = centre;
        }
        if (velocity.Length() > CoastSpeed)
            Anchor = centre;
        var a = -Stiffness * (centre - Anchor) - Damping * velocity;
        float len = a.Length();
        _quiet = len < QuietAccel ? _quiet + dt : 0f;
        // The last FadeBudget of the budget fades the push out, so the body eases onto the creep's own rest point
        // (a few centimetres, run 28) instead of being let go at once (a lurch that read as rocking).
        float scale =
            (len > MaxAccel ? MaxAccel / len : 1f)
            * Math.Clamp((Budget - _spent) / FadeBudget, 0f, 1f);
        a *= scale;
        _spent += len * scale * dt;
        return a;
    }
}

/// <summary>What the game says about the body this tick, for <see cref="HoldGate"/>.</summary>
public struct HoldInputs
{
    public bool Grounded;

    /// <summary>rig.camera's upright: under ragdoll control, not climbing (wall, rope, vine, handle), not gliding.</summary>
    public bool Upright;
    public bool MoveInput;
    public bool Carried;
    public bool InWater;
    public bool Warping;

    /// <summary>Seconds since the game's fall-slide (CharacterMovement.cs:915-919).</summary>
    public float SinceFallSlide;

    /// <summary>Seconds since a bounce surface launched the body (CollisionModifier.cs:108).</summary>
    public float SinceBounce;

    public float SlopeDeg;

    /// <summary>The game's walkable limit (<c>CharacterMovement.maxAngle</c>, CharacterMovement.cs:130).</summary>
    public float MaxSlopeDeg;
}

/// <summary>When <see cref="BodyHold"/> may act at all: only when nothing but the lean is moving the body, so a VR
/// player keeps a flat player's slides, carries, swims and climbs.</summary>
public static class HoldGate
{
    /// <summary>Seconds after a fall-slide or a bounce before the hold may act.</summary>
    public const float SlideCooldown = 0.5f;
    public const float BounceCooldown = 1f;

    public static bool Free(in HoldInputs i) =>
        i.Grounded
        && i.Upright
        && !i.MoveInput
        && !i.Carried
        && !i.InWater
        && !i.Warping
        && i.SinceFallSlide >= SlideCooldown
        && i.SinceBounce >= BounceCooldown
        && i.SlopeDeg < i.MaxSlopeDeg;
}
