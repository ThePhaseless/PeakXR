using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Climbing by pulling on grabbed holds. Feed hand positions in tracking space each frame; the result is
/// the body displacement, opposite to the motion of the newest grip (the hand that most recently took a hold and
/// still holds it). Averaging both hands halved every hand-over-hand pull, since the lower hand still gripping
/// stays put while the new one pulls. A hand contributes nothing on the frame it starts grabbing (the other grip
/// carries that frame), so grab start and end never cause a jump.
/// </summary>
public sealed class ArmLocomotion
{
    /// <summary>Maximum body speed, m/s.</summary>
    public float MaxSpeed = 6f;

    bool _leftWas,
        _rightWas,
        _leftLeads;
    Vector3 _leftLast,
        _rightLast;

    public Vector3 Update(
        bool leftGrab,
        Vector3 leftPos,
        bool rightGrab,
        Vector3 rightPos,
        float dt
    )
    {
        if (leftGrab && !_leftWas)
            _leftLeads = true;
        else if (rightGrab && !_rightWas)
            _leftLeads = false;
        bool leftMoves = leftGrab && _leftWas;
        bool rightMoves = rightGrab && _rightWas;
        Vector3 moved =
            leftMoves && (_leftLeads || !rightMoves) ? leftPos - _leftLast
            : rightMoves ? rightPos - _rightLast
            : Vector3.Zero;
        _leftWas = leftGrab;
        _rightWas = rightGrab;
        _leftLast = leftPos;
        _rightLast = rightPos;

        if (!(leftMoves || rightMoves) || dt <= 0f)
            return Vector3.Zero;

        Vector3 d = -moved;
        float max = MaxSpeed * dt;
        float len = d.Length();
        if (len > max && len > 0f)
            d *= max / len;
        return d;
    }

    public void Reset()
    {
        _leftWas = _rightWas = false;
    }
}
