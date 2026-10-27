using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// A wrist twist used as a knob: the game's scroll input (rope payout, cannon angle, page flips) without
/// a thumbstick. Feed the signed twist per frame; <see cref="Update"/> returns whole steps for the scroll
/// buttons and <see cref="Direction"/> holds while the wrist keeps turning, for scroll-held consumers.
/// </summary>
public sealed class TwistKnob
{
    /// <summary>Degrees of twist per scroll step.</summary>
    public float StepDegrees = 30f;

    /// <summary>Smoothed twist rate (deg/s) above which <see cref="Direction"/> is non-zero.</summary>
    public float HoldRate = 90f;

    /// <summary>Smoothing rate of the twist speed, 1/s.</summary>
    public float RateSmoothing = 10f;

    float _accum;
    float _rate;

    /// <summary>-1, 0 or 1: the way the wrist is turning steadily right now.</summary>
    public int Direction { get; private set; }

    /// <summary>Adds this frame's twist; returns the number of whole steps crossed (signed).</summary>
    public int Update(float deltaDegrees, float dt)
    {
        if (dt > 0f)
        {
            float a = ExpSmooth.Factor(RateSmoothing, dt);
            _rate += (deltaDegrees / dt - _rate) * a;
        }
        Direction =
            _rate > HoldRate ? 1
            : _rate < -HoldRate ? -1
            : 0;

        _accum += deltaDegrees;
        if (StepDegrees <= 0f)
            return 0;
        int steps = (int)(_accum / StepDegrees);
        _accum -= steps * StepDegrees;
        return steps;
    }

    public void Reset()
    {
        _accum = 0f;
        _rate = 0f;
        Direction = 0;
    }

    /// <summary>
    /// Signed twist (degrees) of the rotation from <paramref name="from"/> to <paramref name="to"/> about
    /// <paramref name="localAxis"/>, an axis in <paramref name="from"/>'s own frame (swing-twist split, so
    /// swinging the hand about other axes does not count).
    /// </summary>
    public static float TwistAngle(Quaternion from, Quaternion to, Vector3 localAxis)
    {
        var rel = UnityQuat.Mul(Quaternion.Inverse(from), to);
        return ArmTwist.TwistAngle(rel, Vector3.Normalize(localAxis)) * UnityQuat.Rad2Deg;
    }
}
