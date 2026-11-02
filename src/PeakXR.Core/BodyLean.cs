using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Upper-body lean toward the tracked head. The avatar's spine swings about a pivot low in the back so the eyes
/// move onto the line from the pivot to the head, as far as a natural bend allows. Leaning over a table then bends
/// the avatar and carries its shoulders (and the arms' reach) with the head, instead of leaving the arms short of
/// the controllers. The lean covers the head's offset while the stance walk (<see cref="StanceWalk"/>) waits out
/// its delay or is on its way; a lean held longer, or past the lean limit, is stepped under.
/// </summary>
public static class BodyLean
{
    /// <summary>Limits of the bend away from the animated pose, in degrees.</summary>
    public const float MaxForwardDeg = 40f;
    public const float MaxBackDeg = 15f;
    public const float MaxSideDeg = 25f;

    const float Rad2Deg = 180f / MathF.PI;
    const float Deg2Rad = MathF.PI / 180f;

    /// <summary>
    /// World rotation about <paramref name="pivot"/> that swings <paramref name="eye"/> toward
    /// <paramref name="head"/>: forward/back pitch and sideways roll in the body frame
    /// (<paramref name="forward"/>, <paramref name="up"/>), each clamped to its limit. Distance along the line
    /// (ducking) is left to the crouch. Identity when the head is on the eye's line.
    /// </summary>
    public static Quaternion Swing(
        Vector3 pivot,
        Vector3 eye,
        Vector3 head,
        Vector3 forward,
        Vector3 up
    )
    {
        TrySwing(pivot, eye, head, forward, up, out var q);
        return q;
    }

    /// <summary>
    /// <see cref="Swing"/>, false (with identity) when the eye or the head is not above the pivot: lying down or a
    /// deep duck has no lean, and the caller fades out the last one.
    /// </summary>
    public static bool TrySwing(
        Vector3 pivot,
        Vector3 eye,
        Vector3 head,
        Vector3 forward,
        Vector3 up,
        out Quaternion swing
    )
    {
        swing = Quaternion.Identity;
        var right = Vector3.Cross(up, forward);
        var from = eye - pivot;
        var to = head - pivot;
        float fu = Vector3.Dot(from, up);
        float tu = Vector3.Dot(to, up);
        if (fu <= 1e-4f || tu <= 1e-4f)
            return false;
        float pitch0 = MathF.Atan2(Vector3.Dot(from, forward), fu) * Rad2Deg;
        float roll0 = MathF.Atan2(Vector3.Dot(from, right), fu) * Rad2Deg;
        float dp = MathF.Atan2(Vector3.Dot(to, forward), tu) * Rad2Deg - pitch0;
        float dr = MathF.Atan2(Vector3.Dot(to, right), tu) * Rad2Deg - roll0;
        // Each direction's limit bounds the whole bend toward it (an elliptic cone): clamped apart, a sideways lean
        // with the eyes ahead of the head bent 25 sideways and 9 back at once, 26.7 degrees (run 15, vr.45).
        float e = MathF.Sqrt(
            MathF.Pow(dp / (dp >= 0f ? MaxForwardDeg : MaxBackDeg), 2f)
                + MathF.Pow(dr / MaxSideDeg, 2f)
        );
        float pitch = pitch0 + (e > 1f ? dp / e : dp);
        float roll = roll0 + (e > 1f ? dr / e : dr);
        var dir = up + MathF.Tan(pitch * Deg2Rad) * forward + MathF.Tan(roll * Deg2Rad) * right;
        swing = GripMath.FromTo(from, dir);
        return true;
    }

    /// <summary>
    /// <see cref="TrySwing"/> for a head that keeps its world rotation through the bend (the neck takes it), so its
    /// <paramref name="eye"/> moves only as the head <paramref name="joint"/> does. Aimed at the eye itself, the swing
    /// moved the scout's eyes (high in a big head, about twice the joint's distance from the pivot) half of the way
    /// (run 14: 0.47-0.52). Solved instead so the joint carries the eye level onto <paramref name="head"/>, each axis
    /// within its limit; the height is left to the tracked head (ducking is the crouch's).
    /// </summary>
    public static bool TrySwingHeld(
        Vector3 pivot,
        Vector3 joint,
        Vector3 eye,
        Vector3 head,
        Vector3 forward,
        Vector3 up,
        out Quaternion swing
    )
    {
        var from = joint - pivot;
        float reach = from.Length();
        // Where the joint goes for the eye to land on the head, level only, kept on the joint's own circle.
        var want = head - (eye - joint) - pivot;
        var level = want - up * Vector3.Dot(want, up);
        float len = level.Length();
        if (len > 0.99f * reach)
            level *= 0.99f * reach / len;
        var to = level + up * MathF.Sqrt(MathF.Max(reach * reach - level.LengthSquared(), 0f));
        return TrySwing(pivot, joint, pivot + to, forward, up, out swing)
            && Vector3.Dot(head - pivot, up) > 1e-4f;
    }

    /// <summary>
    /// Horizontal shift of the legs' animated pose (relative to the hip) that keeps the mass centre over the
    /// planted feet while the upper body bends. The ragdoll pulls every part toward hip + its animated offset
    /// (Bodypart.cs:193-207) and the feet grip, so a bend alone leaves the leg targets behind the feet: friction
    /// then creeps the whole body toward the lean until the hip is back over them (run 28: the mass centre 0.06 m
    /// forward, back again on standing up). With the legs shifted, the hip settles back by this much and the
    /// mass centre stays, as a person's hips go back when they bend. <paramref name="upperMoment"/> is the sum of
    /// mass times animated displacement of the bent parts; <paramref name="legMass"/> the mass that stays planted.
    /// </summary>
    public static Vector3 LegShift(Vector3 upperMoment, float totalMass, float legMass)
    {
        float carried = totalMass - legMass;
        if (carried <= 1e-4f)
            return Vector3.Zero;
        return new Vector3(upperMoment.X, 0f, upperMoment.Z) / carried;
    }

    /// <summary>A look change in one physics step (degrees) from which it can be a snap: the smallest
    /// (<c>Comfort.SnapTurnAngle</c>) is 15.</summary>
    public const float SwingTrail = 15f;

    /// <summary>How far a head swing's eye trails the look (s of its rate), as far as <see cref="MaxSwingLag"/> degrees.
    /// A frame's worth at 30 fps, where it was measured: the 2-3 degrees of bend that trail leaves against the swing take
    /// load off the feet, and after a glance the body slid 0.082-0.122 m with the hold on and none, 0.020-0.029 m with
    /// (vr.302). It is set by the rate, so the frame rate doesn't shrink it: the step's own lag was 3-4x less at 90 Hz.</summary>
    public const float SwingLagSeconds = 0.035f;

    public const float MaxSwingLag = 8f;

    /// <summary>Seconds the look's rate is smoothed over: at 25-40 fps one physics step carries a whole frame, reading
    /// 1.25-2x the rate and the next step 0.</summary>
    public const float SwingRateSeconds = 0.05f;

    /// <summary>A step more than this (s) of the swing's rate is a jump of the look, not its turning: a frame is at most
    /// 0.04 s at the frame rates played, so a fast swing's step stays well under it.</summary>
    public const float SnapOverSeconds = 0.1f;

    /// <summary>A look change of <paramref name="step"/> degrees in one physics step, against the swing's smoothed
    /// <paramref name="rate"/> before it, is a snap.</summary>
    public static bool IsSnap(float step, float rate) =>
        MathF.Abs(step) >= SwingTrail && MathF.Abs(step) > MathF.Abs(rate) * SnapOverSeconds;

    /// <summary>The look's smoothed rate (deg/s) after a step of <paramref name="step"/> degrees. A snap has landed by the
    /// next frame and leaves no swing: smoothed, a 45 degree step read 740 deg/s, a 0.1 s trail under a still head.</summary>
    public static float SwingRate(float rate, float step, float dt)
    {
        if (dt <= 0f)
            return rate;
        return IsSnap(step, rate)
            ? 0f
            : rate + (step / dt - rate) * ExpSmooth.Factor(1f / SwingRateSeconds, dt);
    }

    /// <summary>
    /// What to add to the tracked head so the lean starts from the calibrated eyes: the unbent animated eyes
    /// (<paramref name="eyeFromHip"/>, relative to the animated hip, taken on a physics step) carried onto the
    /// rendered frame's physical <paramref name="hip"/>, minus that frame's <paramref name="calibratedEye"/>.
    /// Both ends on the same frame, so a moving body adds no bias. The next step stands the pose on the look this frame
    /// wrote (<c>CharacterRagdoll.RotateCharacter</c>), so the eye turns by <paramref name="turned"/> (degrees), the look's
    /// change since that step. Left on the old look, a 45 degree snap read as a lean by the turn's chord for one step:
    /// want 0.197-0.232 m, 16-22 degrees of target under a still head (vr.300, #123). A swing's eye still trails it, by
    /// <see cref="SwingLagSeconds"/> of its smoothed <paramref name="lookRate"/>, which a snap resets
    /// (<see cref="SwingRate"/>).
    /// </summary>
    public static Vector3 HeadBias(
        Vector3 eyeFromHip,
        Vector3 hip,
        Vector3 calibratedEye,
        float turned = 0f,
        float lookRate = 0f
    )
    {
        float lag = Math.Clamp(lookRate * SwingLagSeconds, -MaxSwingLag, MaxSwingLag);
        var level = YawMath.Rotate(new Vector2(eyeFromHip.X, eyeFromHip.Z), turned - lag);
        return hip + new Vector3(level.X, eyeFromHip.Y, level.Y) - calibratedEye;
    }

    /// <summary>Horizontal head offset (m) the lean ignores: the hip's own sway and frame timing read as about
    /// 0.01 m (MP run 9), and bending for it pushed the sway on (a still head, a body rocking at 0.04-0.15 m/s).</summary>
    public const float WantDeadband = 0.02f;

    /// <summary>
    /// <paramref name="want"/> (head minus the unbent eyes) with <see cref="WantDeadband"/> taken off its
    /// horizontal length, continuously: a still head bends nothing, a real lean keeps its direction and loses 2 cm.
    /// The vertical part (ducking, the crouch's) is kept.
    /// </summary>
    public static Vector3 Deadband(Vector3 want, Vector3 up)
    {
        var vertical = up * Vector3.Dot(want, up);
        var flat = want - vertical;
        float len = flat.Length();
        return len <= WantDeadband ? vertical : vertical + flat * ((len - WantDeadband) / len);
    }
}
