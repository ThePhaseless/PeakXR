using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// The part of the lean's share of the stance debt that a turn made, which the lean leaves to the stance walk. The body
/// turns about its mass centre with its eyes 0.24 m ahead, a real head turns about its neck, and a
/// snap or smooth turn pivots on the eye, so every turn moves the debt without the head leaving the body. Run 13 bent
/// the spine 9.5 degrees for a glance (vr.302) and 15-19 under a held turned head (vr.45b). The debt's change while the
/// look turns (and for <see cref="SwingSeconds"/> after, while the ragdoll torso swings onto the new heading) goes
/// into the skip; the skip never exceeds the share along it, so a walk that closes the debt closes the skip with it.
/// A stale skip once bent the lean away from the view (vr.300, run 13). Once the walk has been and gone, or none
/// came for <see cref="ReleaseAfter"/>, the skip lets go and the lean takes what is left: a held offset. A look coming
/// back up gives up the step back the same way (<see cref="GiveUpSpeed"/>): the debt's target moves, the body does not.
/// The creep that follows a large turn stays out too, by the eyes' own travel: the head has not moved (T2-2: 16.5 degrees).
/// </summary>
public sealed class TurnSkip
{
    /// <summary>The look turns faster than this (degrees/s): a glance or head turn is 100-250, idle sway a few.</summary>
    public float TurningSpeed = 30f;

    /// <summary>Seconds after the look stops turning that the debt's change still counts as the turn's.</summary>
    public float SwingSeconds = 0.5f;

    /// <summary>Seconds after a turn with no stance walk before the lean takes the skip (past the walk's 1.5 s delay).</summary>
    public float ReleaseAfter = 3f;

    /// <summary>Rate (1/s) at which a released skip goes to the lean.</summary>
    public float ReleaseRate = 2f;

    /// <summary>Most of a turn's leftover the lean takes (m, about 6 degrees): the rest stays skipped, so a walk that
    /// left 0.10-0.14 m under a turned head no longer bends the body 7-12 degrees (run 15, vr.45b; want 8).</summary>
    public float MaxLean = 0.09f;

    /// <summary>The step back shrinks faster than this (m/s): about 7 degrees/s of look mid-band, past a held look's sway.
    /// The body stands behind a view that no longer wants it there, which the walk owes (run 23: 17.8-25.2 degrees).</summary>
    public float GiveUpSpeed = 0.1f;

    Vector3 _last;
    float _lastYaw;
    float _lastLook;
    float _lastBack;
    bool _have;
    bool _walked;
    bool _lastSettling;
    bool _turnWalk;
    bool _byTurn;
    float _since = float.MaxValue;
    float _kept;

    /// <summary>The level offset the lean leaves out (world, m).</summary>
    public Vector3 Skip { get; private set; }

    /// <param name="share">The lean's share of the debt before this skip (world, m).</param>
    /// <param name="yaw">The body's heading (degrees).</param>
    /// <param name="walking">The stance walk is stepping the body.</param>
    /// <param name="back">The step back the look wants now (m, <see cref="BodyStance.StepBack"/>).</param>
    /// <param name="settling">The body still creeps after a large look change (<see cref="OwnMotion.SettlingTurn"/>).</param>
    /// <param name="eyeTravel">The body's eyes' travel since the last update (world, m).</param>
    /// <param name="look">The look's heading (degrees), when the body doesn't keep to it (<see cref="BodyTurn"/>).</param>
    /// <returns><see cref="Skip"/>.</returns>
    public Vector3 Update(
        Vector3 share,
        float yaw,
        bool walking,
        float dt,
        float back = 0f,
        bool settling = false,
        Vector3 eyeTravel = default,
        float? look = null
    )
    {
        share.Y = 0f;
        if (!_have)
        {
            _have = true;
            _last = share;
            _lastYaw = yaw;
            _lastLook = look ?? yaw;
            _lastBack = back;
            _lastSettling = settling;
            return Skip;
        }
        if (dt <= 0f)
            return Skip;
        // The neck moves the debt as the body's turn does, each on its own.
        bool turning =
            MathF.Abs(BodySlotLayout.DeltaAngle(_lastYaw, yaw)) > TurningSpeed * dt
            || MathF.Abs(BodySlotLayout.DeltaAngle(_lastLook, look ?? yaw)) > TurningSpeed * dt;
        float gaveUp = _lastBack - back;
        if (turning || gaveUp > GiveUpSpeed * dt)
        {
            _byTurn = turning || (_byTurn && _since <= SwingSeconds);
            _since = 0f;
            _walked = false;
        }
        else if (_since < float.MaxValue)
            _since += dt;
        _turnWalk = walking && (_turnWalk || _since <= ReleaseAfter);
        // A give-up's share of the change: only what the step back moved, the head's own travel stays the lean's.
        var moved = share - _last;
        float most = MathF.Abs(gaveUp);
        var stepped =
            moved.LengthSquared() <= most * most ? moved : Vector3.Normalize(moved) * most;
        // The step back growing again takes back what its giving up skipped: a nod keeps the lean.
        bool regrown = gaveUp < 0f && Vector3.Dot(stepped, Skip) < 0f;
        // A body creeping on after the look, before any walk: the eye moved and the view did not. A lean moves the view,
        // so it keeps bending. After a walk the body is settling over its feet, which the lean takes as before; a look
        // large enough to arm settling again starts a new creep, which that walk says nothing about.
        if (settling && !_lastSettling)
            _walked = false;
        _lastSettling = settling;
        bool creeping = settling && !_walked;
        if (walking)
            _walked = true;
        else if (_since <= SwingSeconds)
            Skip += _byTurn ? moved : stepped;
        else if (creeping && !regrown)
            Skip -= eyeTravel with { Y = 0f };
        else if (_walked || _since > ReleaseAfter)
            Skip *= MathF.Max(0f, 1f - ReleaseRate * dt);
        else if (regrown)
            Skip += stepped;
        // Along the share and no longer than it: the lean then bends toward the view by at most the debt. Released,
        // never shorter than the skip's own part of the share past MaxLean.
        float len2 = share.LengthSquared();
        float k = len2 > 1e-8f ? Math.Clamp(Vector3.Dot(Skip, share) / len2, 0f, 1f) : 0f;
        if (_kept > 0f && len2 > 1e-8f)
            k = MathF.Max(k, MathF.Min(_kept, MathF.Sqrt(len2) - MaxLean) / MathF.Sqrt(len2));
        Skip = share * k;
        // Kept from the turn and its walk; gone once the head is back within MaxLean of the body. A turn's walk keeps all
        // past MaxLean: its settle springs back with nothing skipped (run 21: vr.45b 8.5 degrees, vr.300 15.1).
        _kept =
            len2 <= MaxLean * MaxLean ? 0f
            : _turnWalk ? MathF.Sqrt(len2) - MaxLean
            : _since <= SwingSeconds || walking || creeping ? MathF.Max(0f, Skip.Length() - MaxLean)
            : _kept;
        _last = share;
        _lastYaw = yaw;
        _lastLook = look ?? yaw;
        _lastBack = back;
        return Skip;
    }

    public void Reset()
    {
        Skip = Vector3.Zero;
        _kept = 0f;
        _have = false;
        _walked = false;
        _turnWalk = false;
        _byTurn = false;
        _since = float.MaxValue;
    }
}
