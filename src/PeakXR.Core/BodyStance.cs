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

    /// <summary>
    /// The level part of what the lean would bend for (<paramref name="share"/>) past <paramref name="limit"/>, which
    /// it leaves out: past the lean limit the stance walk steps under the head (<see cref="StanceWalk.AtOnce"/>), so a
    /// head walking away in the room bends the spine no further than the limit while the body catches up (run 12,
    /// vr.396: 0.41 m owed, 20.7 degrees, 24 at most).
    /// </summary>
    public static Vector3 LeanBeyond(Vector3 share, float limit)
    {
        var flat = new Vector3(share.X, 0f, share.Z);
        float len = flat.Length();
        return len > limit ? flat * (1f - limit / len) : Vector3.Zero;
    }
}

/// <summary>What a <see cref="StanceWalk"/> is doing.</summary>
public enum StanceWalkPhase
{
    Idle,
    Push,
    Settle,
}

/// <summary>
/// Walks the body off a <see cref="BodyStance.Debt"/> in steady pushes, the way a player nudges the stick: past
/// <see cref="Start"/>, once the debt has held still for <see cref="Delay"/> (a glance returns before the body moves)
/// or at once past <see cref="AtOnce"/>, it pushes toward where the body would come to rest (the debt less its coast:
/// <see cref="CoastSeconds"/> of drag and the game's eased input running down at <see cref="InputSlew"/>), ramping
/// down to <see cref="MinInput"/> as it closes. A push holds for at least <see cref="MinPush"/> so the game's gait
/// steps the feet, and ends once the body would come to rest under the view (or <see cref="Start"/> past it, sooner).
/// The request goes out as it is: the game eases it into the force at <see cref="InputSlew"/>, within a frame.
/// Then the walk settles until the body has been still for <see cref="StillSeconds"/> (at most <see cref="SettleMax"/>)
/// and judges the debt: under <see cref="Again"/> the walk is done and the lean bends for the rest (<see cref="Rested"/>
/// until the debt moves), otherwise the same walk pushes again without another delay. A head
/// that moves on during the settle ends the walk (past <see cref="AtOnce"/> it pushes at once). A walk whose body
/// makes no headway of its own (a wall, a friend, a body that keeps springing back), or that has pushed
/// <see cref="MaxPushes"/> times, gives up until the debt changes.
/// </summary>
public sealed class StanceWalk
{
    /// <summary>Debt (m) that starts a walk, and under which a settled walk is done: above the standing body's idle
    /// sway; the lean bends for the rest.</summary>
    public float Start = 0.05f;

    /// <summary>A push aims to bring the body to rest this close (m) to the view.</summary>
    public float Stop = 0.03f;

    /// <summary>Seconds a debt must hold still (within <see cref="Start"/>) before it is walked.</summary>
    public float Delay = 1.5f;

    /// <summary>A debt past this (m, the lean limit) is walked at once.</summary>
    public float AtOnce = 0.35f;

    /// <summary>Distance past <see cref="Stop"/> (m) over which the request ramps to full.</summary>
    public float Ramp = 0.2f;

    /// <summary>Least request while pushing: a standing body does not move for less (run 9: 0.1 of the stick for
    /// 1.6 s made no headway). That 0.2 is enough to step the feet is an unmeasured assumption (#10): the stance
    /// trace's push length, headway and spring-back measure it.</summary>
    public float MinInput = 0.2f;

    /// <summary>Least seconds a push holds its request. The game's gait reads the move input damped over 0.125 s and
    /// snapped to 0 under 0.125 (CharacterAnimations.cs:185-196), so a shorter push slides the body over planted
    /// feet and it springs back (run 10, vr.45 t840.54-841.59: 0.154 m walked in a few frames, 0.128 m back). How long
    /// the gait needs is not measured (the Core tests assume 0.25 s, #10).</summary>
    public float MinPush = 0.3f;

    /// <summary>Seconds of travel the body coasts at its speed once its move input is gone: PEAK's drag of 0.85 a 50 Hz
    /// step (CharacterMovement.cs:48, Bodypart.cs:214-225) leaves v x 0.11 s. cf6aa3e vr.45: walks stopped on the debt
    /// alone reached 2 m/s and coasted 0.1-0.16 m past the view.</summary>
    public float CoastSeconds = 0.11f;

    /// <summary>Level speed (m/s) under which a settling body counts as still.</summary>
    public float StillSpeed = 0.05f;

    /// <summary>Seconds a settling body must stay under <see cref="StillSpeed"/> before the push is judged.</summary>
    public float StillSeconds = 0.3f;

    /// <summary>Longest settle (s): a body whose speed keeps swaying (run 10 t289.4-291.1: 0.06-0.26 m/s standing,
    /// a walk that never ended) is judged anyway.</summary>
    public float SettleMax = 1.5f;

    /// <summary>How fast (a second) the game eases its move input toward the stick: movementTurnSpeed
    /// (Character.cs:1336-1341; set live, 20/s in run 13's stance trace, not the decompiled field default of 2). An eased
    /// input u runs down for u / slew seconds after the request ends, still pushing, which adds v x u / (2 slew) to the
    /// coast: 0.025 s at full input. At that rate the game's eased input and the raw request the gait reads are a frame
    /// apart, so the walk sends its request as it is.</summary>
    public float InputSlew = 20f;

    /// <summary>A push that closes less than <see cref="StallProgress"/> m in this many seconds is blocked.</summary>
    public float StallSeconds = 1.5f;

    public float StallProgress = 0.03f;

    /// <summary>A settled walk owing more than this (m) pushes again; less is left to the lean (inside its 0.35 m
    /// limit, a few degrees). Run 11: a push from planted feet on what a walk left (0.07-0.2 m, median 0.13) moved the
    /// body about 0.1 m and let it straight back in 16 of 23, a twitch for peers that closed nothing.</summary>
    public float Again = 0.15f;

    /// <summary>Settled pushes in a row whose body walked less than <see cref="StallProgress"/> along the push before
    /// the walk gives up: a body that springs all the way back stops shuffling for peers. The body's own travel, not
    /// the debt, which a head moving meanwhile also changes.</summary>
    public int MaxFruitless = 2;

    /// <summary>Most pushes in one walk: a body that keeps swinging past the view, or a head that keeps drifting,
    /// stops shuffling for peers.</summary>
    public int MaxPushes = 6;

    /// <summary>How long the last push held its request (s), for the trace.</summary>
    public float PushSeconds { get; private set; }

    /// <summary>The body's own travel (m) along the last settled push, from its start to the end of its settle (or
    /// now, while settling), for the trace and the fruitless count.</summary>
    public float Headway { get; private set; }

    /// <summary>How far (m) the body came back over that settle from the most headway of its push.</summary>
    public float SpringBack { get; private set; }

    Vector2 _blockedAt;
    Vector2 _heldAt;
    float _heldFor;
    Vector2 _heading;
    float _best;
    float _stallFor;
    int _fruitless;
    Vector2 _travel;
    Vector2 _step;
    float _headway;
    float _mostHeadway;
    Vector2 _settleDebt;
    Vector2 _settleTravel;

    /// <summary>The walk's phase this update.</summary>
    public StanceWalkPhase State { get; private set; }

    /// <summary>The last walk stalled; it waits for the debt to change.</summary>
    public bool Blocked { get; private set; }

    /// <summary>The last walk ended with what is left to the lean; the next waits for the debt to move.</summary>
    public bool Rested { get; private set; }

    /// <summary>The body's travel is the walk's: pushing, or settling from a push.</summary>
    public bool Walking => State != StanceWalkPhase.Idle;

    /// <summary>Pushes in this walk (the last one's, once idle).</summary>
    public int Pushes { get; private set; }

    /// <summary>Seconds in the current phase.</summary>
    public float PhaseSeconds { get; private set; }

    /// <summary>Debt (m) when the current or last push began.</summary>
    public float PushFrom { get; private set; }

    /// <summary>Closest the debt came (m) during the current or last push and its settle.</summary>
    public float Closest { get; private set; }

    /// <param name="debt">What is owed, world-heading x, z (m): a fixed frame, so a snap or smooth turn mid-walk leaves the
    /// walk's heading valid.</param>
    /// <param name="velocity">The body's level velocity, in the frame of <paramref name="debt"/> (m/s).</param>
    /// <param name="eased">The game's eased move input, length 0..1 (worldMovementInput_Lerp).</param>
    /// <param name="canWalk">The body may be walked: room-scale walking on, grounded, conscious, the stick idle.</param>
    /// <returns>Requested move input in the frame of <paramref name="debt"/>, length 0..1.</returns>
    public Vector2 Update(Vector2 debt, Vector2 velocity, float eased, bool canWalk, float dt)
    {
        float dist = debt.Length();
        if (!canWalk)
        {
            // A moment the body can't be walked (a push, the stick, a mantle) keeps a rested leftover the lean's:
            // only a debt that moves past Start, or Reset, gives it back to the walk.
            Halt();
            return Vector2.Zero;
        }
        // The delay runs from when the debt is owed: time under Start does not count, or the first few cm of a new
        // lean or turn read as held (run 11, vr.45: a 0.06 m debt walked 0.36 s before the lean's hold began).
        if (dist <= Start || Vector2.Distance(debt, _heldAt) > Start)
        {
            _heldAt = debt;
            _heldFor = 0f;
        }
        else
            _heldFor += dt;
        if (Vector2.Distance(debt, _blockedAt) > Start)
            Blocked = Rested = false;
        PhaseSeconds += dt;
        Closest = MathF.Min(Closest, dist);
        // The body's travel up to the debt's frame: the debt is a frame old (the rig measures it in LateUpdate), the
        // velocity this frame's, so its step counts from the next update on.
        if (State != StanceWalkPhase.Idle)
        {
            _travel += _step;
            _headway = Vector2.Dot(_travel, _heading);
            _mostHeadway = MathF.Max(_mostHeadway, _headway);
        }
        _step = velocity * dt;
        switch (State)
        {
            case StanceWalkPhase.Idle:
                if (
                    !Blocked
                    && (!Rested || dist > AtOnce)
                    && dist > Start
                    && (dist > AtOnce || _heldFor >= Delay)
                )
                {
                    _fruitless = 0;
                    Pushes = 0;
                    return BeginPush(debt, velocity, eased, dt);
                }
                return Vector2.Zero;
            case StanceWalkPhase.Settle:
                _stallFor = velocity.Length() < StillSpeed ? _stallFor + dt : 0f;
                Headway = _headway;
                SpringBack = _mostHeadway - _headway;
                if (dist <= AtOnce)
                {
                    // The view's own travel since the push ended: the debt's change plus the body's.
                    if ((debt - _settleDebt + _travel - _settleTravel).Length() > Start)
                    {
                        // The head moved on, not the body back: a new debt, which waits its own delay.
                        Enter(StanceWalkPhase.Idle);
                        _heldAt = debt;
                        _heldFor = 0f;
                        return Vector2.Zero;
                    }
                    if (_stallFor < StillSeconds && PhaseSeconds < SettleMax)
                        return Vector2.Zero;
                    if (dist <= Again)
                    {
                        // Done; a body still creeping back over its feet is not a new debt to walk.
                        Rested = true;
                        _blockedAt = debt;
                        Enter(StanceWalkPhase.Idle);
                        return Vector2.Zero;
                    }
                }
                _fruitless = _headway < StallProgress ? _fruitless + 1 : 0;
                if (_fruitless >= MaxFruitless || Pushes >= MaxPushes)
                {
                    Block(debt);
                    return Vector2.Zero;
                }
                return BeginPush(debt, velocity, eased, dt);
            default:
                return Pushing(debt, velocity, eased, dt);
        }
    }

    Vector2 BeginPush(Vector2 debt, Vector2 velocity, float eased, float dt)
    {
        float dist = debt.Length();
        Enter(StanceWalkPhase.Push);
        Pushes++;
        PushFrom = Closest = _best = dist;
        _stallFor = 0f;
        _heading = debt / dist;
        _travel = Vector2.Zero;
        _headway = _mostHeadway = 0f;
        return Pushing(debt, velocity, eased, dt);
    }

    Vector2 Pushing(Vector2 debt, Vector2 velocity, float eased, float dt)
    {
        float dist = debt.Length();
        // Where the body stops if the request ends now; the debt is a frame old (the rig measures it in LateUpdate).
        var rest =
            debt - velocity * (CoastSeconds + eased / (2f * MathF.Max(InputSlew, 0.1f)) + dt);
        float restDist = rest.Length();
        float along = Vector2.Dot(rest, _heading);
        // Done once the body would come to rest under the view, but not before the gait has stepped; before that only
        // once it would rest Start past the view (ending at Stop past gave small debts 0.25 s pushes that never stepped).
        if (along < -Start || PhaseSeconds >= MinPush && (along <= 0f || restDist <= Stop))
        {
            PushSeconds = PhaseSeconds;
            Enter(StanceWalkPhase.Settle);
            _settleDebt = debt;
            _settleTravel = _travel;
            return Vector2.Zero;
        }
        if (dist < _best - StallProgress)
        {
            _best = dist;
            _stallFor = 0f;
        }
        else if ((_stallFor += dt) >= StallSeconds)
        {
            Block(debt);
            return Vector2.Zero;
        }
        var dir = along > 0f && restDist > Stop ? rest / restDist : _heading;
        float amount = Ramp > 0f ? (restDist - Stop) / Ramp : 1f;
        return dir * Math.Clamp(amount, MinInput, 1f);
    }

    void Enter(StanceWalkPhase phase)
    {
        State = phase;
        PhaseSeconds = 0f;
        _stallFor = 0f;
    }

    void Block(Vector2 debt)
    {
        Blocked = true;
        _blockedAt = debt;
        Enter(StanceWalkPhase.Idle);
    }

    /// <summary>Forget the walk (the body cannot be walked, or the view was placed again).</summary>
    public void Reset()
    {
        Halt();
        Blocked = Rested = false;
    }

    void Halt()
    {
        Enter(StanceWalkPhase.Idle);
        _heldFor = 0f;
    }
}

/// <summary>
/// Which of a free body's level travel PeakXR itself caused, so the view keeps it out (the room anchor takes it): the
/// stance walk, its coast until the body is nearly still (at most <see cref="MaxCoastSeconds"/>, and never through an
/// outside force), and the body's shift under the head's own turn or look down.
/// Outside forces (wind, a push, a knockback, a slide, a moving ground) are never kept out: they move body and view
/// as in vanilla, so the walk never walks them back and the player is never immune to them.
/// </summary>
public sealed class OwnMotion
{
    /// <summary>Level body speed (m/s) under which a walk's coast has ended.</summary>
    public float StillSpeed = 0.05f;

    /// <summary>Seconds the body must stay under <see cref="StillSpeed"/> before the coast has ended.</summary>
    public float StillSeconds = 0.3f;

    /// <summary>Look rate (degrees per second) that counts as the head turning or pitching.</summary>
    public float LookRate = 10f;

    /// <summary>Seconds after the look stops moving during which the body still settles into the new pose.</summary>
    public float PoseSeconds = 0.6f;

    /// <summary>Longest coast (s) after a walk ends: a walked body is still within a second (drag 0.11 s, the eased
    /// input's run-down 0.5 s at most), so travel after that is something else's.</summary>
    public float MaxCoastSeconds = 1f;

    bool _coasting;
    float _coastFor;
    float _stillFor;
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
        // Held still a while, not one slow update: a body springing back toward its planted feet passes through
        // zero at the turn and creeps at the end (run 10, vr.45 t837.25-837.85: the view followed that tail).
        _stillFor = walking || levelSpeed >= StillSpeed ? 0f : _stillFor + dt;
        _coastFor = walking ? 0f : _coastFor + dt;
        // An outside force takes the coast over: its tail is the force's, after OutsideSeconds too.
        if (outside)
            _coasting = false;
        else if (walking)
            _coasting = true;
        else if (_stillFor >= StillSeconds || _coastFor >= MaxCoastSeconds)
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
        _coastFor = 0f;
        _stillFor = 0f;
        _poseFor = 0f;
        _haveLook = false;
    }
}
