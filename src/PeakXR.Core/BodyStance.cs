using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Where the local body should stand under the first-person view. The view moves only with the head, and many things
/// move the head off the body: a head turned in the room (the game turns the body about its mass centre,
/// Character.SetRotation, Character.cs:1539-1542, its eyes about 0.24 m ahead, while a real head turns about the neck),
/// a snap or smooth turn pivoting on the eye, a step or a lean in the room. <see cref="Debt"/> measures the one offset
/// they all leave, from where the view is, so no source is missed; looking down, the body stands back so the player
/// sees the ground in front of its feet. The body closes it by walking (<see cref="StanceWalk"/>, vanilla movement
/// input), and the view keeps that walk out of itself, so it never moves the view.
/// </summary>
public static class BodyStance
{
    /// <summary>Look pitch (degrees, up positive) below which the body starts to step back.</summary>
    public const float StepBackFromPitch = -35f;

    /// <summary>Look pitch at which the step back is whole.</summary>
    public const float StepBackFullPitch = -65f;

    /// <summary>The whole step back (m, world): the feet out of the way of the ground just ahead of them.</summary>
    public const float MaxStepBack = 0.3f;

    /// <summary>How far (m) the body stands back behind the view for a look pitch: eased in and out over
    /// <see cref="StepBackFromPitch"/> to <see cref="StepBackFullPitch"/>.</summary>
    public static float StepBack(float pitchDeg)
    {
        float t = Math.Clamp(
            (pitchDeg - StepBackFromPitch) / (StepBackFullPitch - StepBackFromPitch),
            0f,
            1f
        );
        return MaxStepBack * t * t * (3f - 2f * t);
    }

    /// <summary>
    /// The level walk (world) that puts the body where it belongs: the <paramref name="view"/> eye less the body's
    /// unbent eyes at its own heading (<paramref name="bodyEye"/>), stood <paramref name="back"/> m behind it along
    /// <paramref name="bodyForward"/>.
    /// </summary>
    public static Vector3 Debt(Vector3 view, Vector3 bodyEye, Vector3 bodyForward, float back)
    {
        var d = view - bodyEye - bodyForward * back;
        return new Vector3(d.X, 0f, d.Z);
    }

    /// <summary>
    /// The part of the head's offset the lean leaves out, so the body never bends for its step back: the step back
    /// (<paramref name="back"/> along <paramref name="forward"/>) less what of it the <paramref name="debt"/> still owes.
    /// The lean bends for the rest of the debt, which keeps the eyes at the view while the body is on its way.
    /// </summary>
    public static Vector3 LeanSkip(Vector3 debt, Vector3 forward, float back) =>
        forward * (back - Math.Clamp(-Vector3.Dot(debt, forward), 0f, back));
}

/// <summary>
/// Walks the body off a <see cref="BodyStance.Debt"/>: past <see cref="Start"/>, once the debt has held still for
/// <see cref="Delay"/> (a glance returns before the body moves) or at once past <see cref="AtOnce"/>, it asks for move
/// input along the debt until it is inside <see cref="Stop"/>, ramping down to <see cref="MinInput"/> as it closes so
/// the body does not overshoot. A walk that makes no headway (a wall, a friend) gives up until the debt changes.
/// </summary>
public sealed class StanceWalk
{
    /// <summary>Debt (m) that starts a walk: above the standing body's idle sway.</summary>
    public float Start = 0.05f;

    /// <summary>A started walk ends this close (m): all the way under the head, above the idle sway's noise.</summary>
    public float Stop = 0.03f;

    /// <summary>Seconds a debt must hold still (within <see cref="Start"/>) before it is walked.</summary>
    public float Delay = 1.5f;

    /// <summary>A debt past this (m, the lean limit) is walked at once.</summary>
    public float AtOnce = 0.35f;

    /// <summary>Distance past <see cref="Stop"/> (m) over which the request ramps to full.</summary>
    public float Ramp = 0.2f;

    /// <summary>Least request while walking: a standing body does not move for less (run 9: 0.1 of the stick for
    /// 1.6 s made no headway), and its coast at this pace stays inside the stop distance.</summary>
    public float MinInput = 0.35f;

    /// <summary>Seconds after a walk during which the body's coast still belongs to it.</summary>
    public float SettleSeconds = 0.5f;

    /// <summary>A walk that closes less than <see cref="StallProgress"/> m in this many seconds is blocked.</summary>
    public float StallSeconds = 1.5f;

    public float StallProgress = 0.03f;

    bool _walking;
    float _sinceWalk = float.MaxValue;
    float _best;
    float _stallFor;
    Vector2 _blockedAt;
    Vector2 _heldAt;
    float _heldFor;

    /// <summary>The last walk stalled; it waits for the debt to change.</summary>
    public bool Blocked { get; private set; }

    /// <summary>The body's travel is the walk's: walking, or settling from it.</summary>
    public bool Walking { get; private set; }

    /// <param name="debt">What is owed, tracking x, z (m).</param>
    /// <param name="canWalk">The body may be walked: room-scale walking on, grounded, conscious, the stick idle.</param>
    /// <returns>Requested move input in tracking space, length 0..1.</returns>
    public Vector2 Update(Vector2 debt, bool canWalk, float dt)
    {
        float dist = debt.Length();
        if (!canWalk)
        {
            Reset();
            return Vector2.Zero;
        }
        if (Vector2.Distance(debt, _heldAt) > Start)
        {
            _heldAt = debt;
            _heldFor = 0f;
        }
        else
            _heldFor += dt;
        if (Blocked && Vector2.Distance(debt, _blockedAt) > Start)
            Blocked = false;
        bool start = !_walking && !Blocked && dist > Start && (dist > AtOnce || _heldFor >= Delay);
        _walking = !Blocked && (_walking ? dist > Stop : start);
        if (start || (_walking && dist < _best - StallProgress))
        {
            _best = dist;
            _stallFor = 0f;
        }
        else if (_walking && (_stallFor += dt) >= StallSeconds)
        {
            Blocked = true;
            _blockedAt = debt;
            _walking = false;
            _sinceWalk = float.MaxValue;
        }
        if (_walking)
            _sinceWalk = 0f;
        else if (_sinceWalk < float.MaxValue)
            _sinceWalk += dt;
        Walking = _walking || _sinceWalk < SettleSeconds;
        if (!_walking)
            return Vector2.Zero;
        float amount = Ramp > 0f ? (dist - Stop) / Ramp : 1f;
        return debt / dist * Math.Clamp(amount, MinInput, 1f);
    }

    /// <summary>Forget the walk (the body cannot be walked, or the view was placed again).</summary>
    public void Reset()
    {
        _walking = false;
        _sinceWalk = float.MaxValue;
        Walking = false;
        Blocked = false;
        _heldFor = 0f;
    }
}
