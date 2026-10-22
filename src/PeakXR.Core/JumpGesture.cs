using System;

namespace PeakXR.Core;

/// <summary>
/// Hop to jump: both arms swing up fast while the real head rises above its calibrated standing height, the way a
/// person jumps on the spot. Standing up out of a crouch, looking down and back up, a head bob, reaching overhead
/// and an arm-swing sprint all stay below at least one of the bars, so a false jump (a fall off a cliff) is rare.
/// One jump per <see cref="Cooldown"/>.
/// </summary>
public sealed class JumpGesture
{
    /// <summary>The head must be this far (m) above the calibrated standing eye: standing up only gets back to it.</summary>
    public float RiseAbove = 0.04f;

    /// <summary>Upward head speed (m/s) at the moment of the hop.</summary>
    public float HeadSpeed = 0.35f;

    /// <summary>Upward speed (m/s) both hands reach together during the swing.</summary>
    public float HandSpeed = 1.2f;

    /// <summary>Seconds the arm swing may lead the head.</summary>
    public float HandWindow = 0.3f;

    /// <summary>Seconds after a crouch ends during which a rise is the stand-up, not a hop.</summary>
    public float CrouchGrace = 0.5f;

    /// <summary>Head pitch (degrees from level) past which the head is looking down or up, not hopping.</summary>
    public float MaxPitch = 30f;

    /// <summary>Seconds after the head was past <see cref="MaxPitch"/> during which a rise is the head coming back
    /// level (the headset swings up with it), not a hop.</summary>
    public float PitchGrace = 0.4f;

    public float Cooldown = 0.6f;

    /// <summary>Head speed smoothing (s): tracking jitter must not read as a rise.</summary>
    public float Smoothing = 0.03f;

    bool _have;
    float _head,
        _left,
        _right,
        _headSpeed;
    float _sinceHands = float.PositiveInfinity;
    float _sinceCrouch = float.PositiveInfinity;
    float _sinceHop = float.PositiveInfinity;
    float _sinceTilted = float.PositiveInfinity;

    /// <summary>Feeds one frame; true on the frame a hop is recognised.</summary>
    /// <param name="enabled">Setting on, head and both hands tracked, standing play.</param>
    /// <param name="headY">Tracking-space head height.</param>
    /// <param name="standingEyeY">Tracking-space eye height calibrated standing.</param>
    /// <param name="pitchDegrees">Head pitch from level, either sign.</param>
    /// <param name="leftY">Tracking-space left hand height.</param>
    /// <param name="rightY">Tracking-space right hand height.</param>
    /// <param name="crouched">The physical crouch holds this frame.</param>
    /// <param name="swinging">The arm-swing sprint holds this frame: alternating arms are running, not hopping.</param>
    public bool Update(
        bool enabled,
        float headY,
        float standingEyeY,
        float pitchDegrees,
        float leftY,
        float rightY,
        bool crouched,
        bool swinging,
        float dt
    )
    {
        if (!enabled || dt <= 0f || float.IsNaN(headY + leftY + rightY))
        {
            Reset();
            return false;
        }
        _sinceHop += dt;
        _sinceCrouch = crouched ? 0f : _sinceCrouch + dt;
        _sinceHands += dt;
        _sinceTilted = MathF.Abs(pitchDegrees) > MaxPitch ? 0f : _sinceTilted + dt;
        if (_have)
        {
            float k = 1f - MathF.Exp(-dt / Smoothing);
            _headSpeed += ((headY - _head) / dt - _headSpeed) * k;
            if ((leftY - _left) / dt > HandSpeed && (rightY - _right) / dt > HandSpeed)
                _sinceHands = 0f;
        }
        _have = true;
        _head = headY;
        _left = leftY;
        _right = rightY;

        bool hop =
            _sinceHop > Cooldown
            && !swinging
            && _sinceCrouch > CrouchGrace
            && _sinceTilted > PitchGrace
            && _headSpeed > HeadSpeed
            && headY - standingEyeY > RiseAbove
            && _sinceHands <= HandWindow;
        if (hop)
            _sinceHop = 0f;
        return hop;
    }

    public void Reset()
    {
        _have = false;
        _headSpeed = 0f;
        _sinceHands = float.PositiveInfinity;
        _sinceCrouch = float.PositiveInfinity;
        _sinceHop = float.PositiveInfinity;
        _sinceTilted = float.PositiveInfinity;
    }
}
