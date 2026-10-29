using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Where the local body should stand under the first-person view, apart from a lean. The game turns the body to the
/// head's heading about its mass centre (Character.SetRotation, Character.cs:1539-1542), its eyes about 0.24 m ahead
/// of it, while a real head turns about the neck with the eyes about <see cref="NeckToEyes"/> ahead: a head turned
/// 180 degrees in the room left the body's eyes 0.27 m off the view. Looking down, the body stands back so the player
/// sees the ground in front of its feet. The body closes the difference by walking (<see cref="StanceWalk"/>, vanilla
/// movement input), and the view keeps the walk as a shift, so it never moves the view.
/// </summary>
public static class BodyStance
{
    /// <summary>Real eyes ahead of the neck's turning axis (m, real): a head turned in place swings them round it.</summary>
    public const float NeckToEyes = 0.1f;

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
    /// The level walk (world) that puts the body where it belongs under the view: the body's eyes at the rig heading
    /// (<paramref name="rigEye"/>, the point the view anchor rides) less those at the body's own heading
    /// (<paramref name="bodyEye"/>), plus the view's kept <paramref name="shift"/>, the real eyes' swing round the
    /// <paramref name="neck"/> (world m) and the step back of <paramref name="back"/> away from the look.
    /// </summary>
    public static Vector3 Debt(
        Vector3 rigEye,
        Vector3 bodyEye,
        Vector3 shift,
        Vector3 rigForward,
        Vector3 bodyForward,
        float neck,
        float back
    )
    {
        var d = rigEye - bodyEye + shift + (bodyForward - rigForward) * neck - bodyForward * back;
        return new Vector3(d.X, 0f, d.Z);
    }

    /// <summary>The real eyes' swing (tracking x, z) for a head turned <paramref name="headYawDeg"/> in the room from
    /// the rig's forward: room-scale reads a head offset less this as the lean.</summary>
    public static Vector2 NeckSwing(float headYawDeg) =>
        YawMath.Rotate(new Vector2(0f, NeckToEyes), headYawDeg) - new Vector2(0f, NeckToEyes);
}

/// <summary>
/// Walks the body off a <see cref="BodyStance.Debt"/>: past <see cref="Start"/> it asks for move input along the
/// debt until it is inside <see cref="Stop"/>, ramping down as it closes so the body does not overshoot. The caller
/// pays the debt with the body's travel while <see cref="Walking"/> (the walk and a short settle for its coast). A
/// walk that makes no headway (a wall, a friend) gives up quietly until the debt changes.
/// </summary>
public sealed class StanceWalk
{
    /// <summary>Debt (m) that starts a walk: above the standing body's idle sway.</summary>
    public float Start = 0.05f;

    /// <summary>A started walk ends this close (m).</summary>
    public float Stop = 0.02f;

    /// <summary>Distance past <see cref="Stop"/> (m) over which the request ramps to full.</summary>
    public float Ramp = 0.2f;

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

    /// <summary>The last walk stalled; it waits for the debt to change.</summary>
    public bool Blocked { get; private set; }

    /// <summary>The body's travel is the walk's: walking, or settling from it.</summary>
    public bool Walking { get; private set; }

    /// <param name="debt">What is owed, tracking x, z (m).</param>
    /// <param name="canWalk">The body may be walked: grounded, free, the stick idle and no room-scale follow.</param>
    /// <returns>Requested move input in tracking space, length 0..1.</returns>
    public Vector2 Update(Vector2 debt, bool canWalk, float dt)
    {
        float dist = debt.Length();
        if (!canWalk)
        {
            Reset();
            return Vector2.Zero;
        }
        if (Blocked && Vector2.Distance(debt, _blockedAt) > Start)
            Blocked = false;
        bool start = !_walking && !Blocked && dist > Start;
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
        return debt / dist * Math.Clamp(amount, 0f, 1f);
    }

    /// <summary>Forget the walk (the body cannot be walked, or the view was placed again).</summary>
    public void Reset()
    {
        _walking = false;
        _sinceWalk = float.MaxValue;
        Walking = false;
        Blocked = false;
    }
}
