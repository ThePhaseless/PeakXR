using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Crouch from the real head: in when the head drops more than <see cref="EnterFraction"/> of the standing
/// height below the calibrated eye, out above <see cref="ExitFraction"/>, so standing at the edge never flickers.
/// </summary>
public sealed class PhysicalCrouch
{
    /// <summary>A fifth of standing height: a 0.47 m stoop to the ground (29 %, vr.87) crouches the avatar, so its
    /// hands reach the controllers there, while a nod or a glance at the floor (well under 15 %) does not.</summary>
    public float EnterFraction = DefaultDepth;
    public float ExitFraction = DefaultDepth * ExitShare;

    public const float DefaultDepth = 0.20f;

    /// <summary>Exit sits at this share of the entry depth: standing at the edge never flickers.</summary>
    public const float ExitShare = 0.7f;

    /// <summary>Sets the entry depth (fraction of standing height) and its exit.</summary>
    public void SetDepth(float depth)
    {
        EnterFraction = depth;
        ExitFraction = depth * ExitShare;
    }

    public bool Crouched { get; private set; }

    /// <param name="enabled">Setting on, head tracked, not seated.</param>
    /// <param name="headY">Tracking-space head height now.</param>
    /// <param name="calibratedEyeY">Tracking-space eye height captured at the last recenter.</param>
    /// <param name="standingHeight">Real standing eye height above the floor, metres (reference height when the
    /// tracking space has no floor).</param>
    public bool Update(bool enabled, float headY, float calibratedEyeY, float standingHeight)
    {
        if (!enabled || !(standingHeight >= HeightCalibration.MinPlausibleEyeHeight))
            return Crouched = false;
        float drop = (calibratedEyeY - headY) / standingHeight;
        if (float.IsNaN(drop))
            return Crouched = false;
        Crouched = Crouched ? drop > ExitFraction : drop > EnterFraction;
        return Crouched;
    }
}

/// <summary>
/// A player who sat down mid-run stays in the physical crouch and crouch-walks at half speed without knowing
/// why. After <see cref="Seconds"/> of moving while physically crouched, this fires once; standing up starts a
/// new episode. Crouching still at the ground (a fuse, a fire) does not count.
/// </summary>
public sealed class LongCrouchHint
{
    public float Seconds = 10f;

    private float movingFor;
    private bool shown;

    /// <returns>True on the one frame the hint should show.</returns>
    public bool Update(bool crouched, bool moving, float dt)
    {
        if (!crouched)
        {
            movingFor = 0f;
            shown = false;
            return false;
        }
        if (moving)
            movingFor += dt;
        if (shown || movingFor < Seconds)
            return false;
        shown = true;
        return true;
    }
}

/// <summary>
/// Arm-swing sprint: alternating hand swings (hands moving in opposite directions, positions relative to the
/// head) above <see cref="EnterSpeed"/> average hand speed hold sprint; it lets go below <see cref="ExitSpeed"/>.
/// The average decays over <see cref="TimeConstant"/>, so the slow points of each swing do not drop it.
/// Only a running swing counts: fore-aft and up-down motion (sideways is clapping or waving), hands below
/// <see cref="MaxHandHeight"/> (not gestures at the face), and only while the player walks; standing still
/// clears it, so gestures made in place never sprint, not even on the first steps after.
/// </summary>
public sealed class ArmSwing
{
    public float EnterSpeed = 1.2f;
    public float ExitSpeed = 0.8f;
    public float TimeConstant = 0.35f;

    /// <summary>Both hands must be this far below the head (m, so negative): a running swing is at the hips
    /// and chest.</summary>
    public float MaxHandHeight = -0.15f;

    bool _have;
    Vector3 _left,
        _right;
    float _average;

    public bool Active { get; private set; }

    /// <param name="left">Left hand position relative to the head, tracking space.</param>
    /// <param name="right">Right hand position relative to the head, tracking space.</param>
    /// <param name="side">The head's flat right axis, tracking space (unit length): motion along it is not a
    /// running swing.</param>
    /// <param name="moving">The player walks forward (stick pushed, on the ground).</param>
    public bool Update(
        bool leftTracked,
        Vector3 left,
        bool rightTracked,
        Vector3 right,
        Vector3 side,
        bool moving,
        float dt
    )
    {
        if (!leftTracked || !rightTracked || !moving || dt <= 0f)
        {
            Reset();
            return false;
        }
        float sample = 0f;
        if (_have && left.Y < MaxHandHeight && right.Y < MaxHandHeight)
        {
            var vl = (left - _left) / dt;
            var vr = (right - _right) / dt;
            vl -= side * Vector3.Dot(vl, side);
            vr -= side * Vector3.Dot(vr, side);
            if (Vector3.Dot(vl, vr) < 0f)
                sample = (vl.Length() + vr.Length()) * 0.5f;
        }
        _have = true;
        _left = left;
        _right = right;
        _average += (sample - _average) * ExpSmooth.Factor(1f / TimeConstant, dt);
        Active = Active ? _average > ExitSpeed : _average > EnterSpeed;
        return Active;
    }

    public void Reset()
    {
        _have = false;
        _average = 0f;
        Active = false;
    }
}
