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
/// input toward where the body would come to rest (the debt less its coast: <see cref="CoastSeconds"/> of drag and the
/// game's eased input running down at <see cref="InputSlew"/>) until that is inside <see cref="Stop"/>, ramping down to
/// <see cref="MinInput"/> as it closes, and ends once the body is also nearly still, so a stop short of the view carries
/// on. A body that still coasts past the view gets no input back against its run; once it has slowed, the same walk
/// turns and walks back. A walk that makes no headway (a wall, a friend, a body that keeps swinging past) gives up until
/// the debt changes.
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

    /// <summary>Seconds of travel the body coasts at its speed once its move input is gone: PEAK's drag of 0.85 a 50 Hz
    /// step (CharacterMovement.cs:48, Bodypart.cs:214-225) leaves v x 0.11 s. cf6aa3e vr.45: walks stopped on the debt
    /// alone reached 2 m/s and coasted 0.1-0.16 m past the view.</summary>
    public float CoastSeconds = 0.11f;

    /// <summary>Level speed (m/s) under which a walk whose coast has closed the debt ends.</summary>
    public float StillSpeed = 0.1f;

    /// <summary>How fast (a second) the game eases its move input toward the stick: movementTurnSpeed
    /// (CharacterMovement.cs:50, Character.cs:1336-1341; set live). An eased input u runs down for u / slew seconds after
    /// the request ends, still pushing, which adds v x u / (2 slew) to the coast: 0.25 s at full input.</summary>
    public float InputSlew = 2f;

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
    Vector2 _heading;
    bool _turned;

    /// <summary>The last walk stalled; it waits for the debt to change.</summary>
    public bool Blocked { get; private set; }

    /// <summary>The body's travel is the walk's: walking, or settling from it.</summary>
    public bool Walking { get; private set; }

    /// <param name="debt">What is owed, tracking x, z (m).</param>
    /// <param name="velocity">The body's level velocity, tracking x, z (m/s).</param>
    /// <param name="eased">The game's eased move input, length 0..1 (worldMovementInput_Lerp).</param>
    /// <param name="canWalk">The body may be walked: room-scale walking on, grounded, conscious, the stick idle.</param>
    /// <returns>Requested move input in tracking space, length 0..1.</returns>
    public Vector2 Update(Vector2 debt, Vector2 velocity, float eased, bool canWalk, float dt)
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
        // Where the body stops if the request ends now; the debt is a frame old (the rig measures it in LateUpdate).
        var rest =
            debt - velocity * (CoastSeconds + eased / (2f * MathF.Max(InputSlew, 0.1f)) + dt);
        float restDist = rest.Length();
        float speed = velocity.Length();
        _walking = !Blocked && (_walking ? restDist > Stop || speed > StillSpeed : start);
        if (start)
        {
            _heading = debt / dist;
            _turned = false;
        }
        // Coasting past the debt: no input back against the body's run (peers would see it brake on the spot) until it
        // has slowed, then the walk turns back.
        bool back = Vector2.Dot(rest, _heading) < 0f;
        bool ask = _walking && restDist > Stop && (!back || speed <= StillSpeed);
        // The walk back's headway counts from where it turned. Only the first turn: a body that keeps swinging past
        // the view stalls and gives up instead of shuffling back and forth.
        bool turn = ask && back && !_turned;
        if (start || turn || (_walking && dist < _best - StallProgress))
        {
            _best = dist;
            _stallFor = 0f;
            _turned |= turn;
        }
        else if (_walking && (_stallFor += dt) >= StallSeconds)
        {
            Blocked = true;
            _blockedAt = debt;
            _walking = false;
            _sinceWalk = float.MaxValue;
            ask = false;
        }
        if (_walking)
            _sinceWalk = 0f;
        else if (_sinceWalk < float.MaxValue)
            _sinceWalk += dt;
        Walking = _walking || _sinceWalk < SettleSeconds;
        if (!ask)
            return Vector2.Zero;
        _heading = rest / restDist;
        float amount = Ramp > 0f ? (restDist - Stop) / Ramp : 1f;
        return _heading * Math.Clamp(amount, MinInput, 1f);
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

/// <summary>
/// Which of a free body's level travel PeakXR itself caused, so the view keeps it out (the room anchor takes it): the
/// stance walk, its coast until the body is nearly still, and the body's shift under the head's own turn or look down.
/// Outside forces (wind, a push, a knockback, a slide, a moving ground) are never kept out: they move body and view
/// as in vanilla, so the walk never walks them back and the player is never immune to them.
/// </summary>
public sealed class OwnMotion
{
    /// <summary>Level body speed (m/s) under which a walk's coast has ended.</summary>
    public float StillSpeed = 0.05f;

    /// <summary>Look rate (degrees per second) that counts as the head turning or pitching.</summary>
    public float LookRate = 10f;

    /// <summary>Seconds after the look stops moving during which the body still settles into the new pose.</summary>
    public float PoseSeconds = 0.6f;

    bool _coasting;
    float _poseFor;
    Vector2 _look;
    bool _haveLook;

    /// <param name="walking">The stance walk is walking or settling.</param>
    /// <param name="levelSpeed">The body's level speed (m/s).</param>
    /// <param name="look">The character's look (yaw, pitch) in degrees, as the head drives it.</param>
    /// <param name="outside">An outside force acts this update: wind, an impulse, a slide, a moving ground.</param>
    /// <returns>This update's body travel is PeakXR's own.</returns>
    public bool Update(bool walking, float levelSpeed, Vector2 look, bool outside, float dt)
    {
        if (walking)
            _coasting = true;
        else if (levelSpeed < StillSpeed)
            _coasting = false;
        if (_haveLook && dt > 0f)
        {
            float yaw = (look.X - _look.X) % 360f;
            yaw =
                yaw > 180f ? yaw - 360f
                : yaw < -180f ? yaw + 360f
                : yaw;
            float rate = new Vector2(yaw, look.Y - _look.Y).Length() / dt;
            _poseFor = rate > LookRate ? PoseSeconds : MathF.Max(0f, _poseFor - dt);
        }
        _look = look;
        _haveLook = true;
        return !outside && (walking || _coasting || _poseFor > 0f);
    }

    public void Reset()
    {
        _coasting = false;
        _poseFor = 0f;
        _haveLook = false;
    }
}
