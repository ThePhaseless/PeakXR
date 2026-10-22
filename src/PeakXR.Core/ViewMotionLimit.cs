using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Motion of the view the player does not make (the detached chase after a tumbling, carried or spectated body) is
/// artificial locomotion: it eases in and out at no more than <see cref="MaxAcceleration"/> and never passes
/// <see cref="MaxSpeed"/>. The caller's own follow picks the goal; this only shapes how the view gets there. A body
/// that gets away re-places the view behind a fade (the caller's job), never a fast glide.
/// </summary>
public sealed class ViewMotionLimit
{
    /// <summary>m/s²: about a gentle car's pull, the felt "push" stays low.</summary>
    public float MaxAcceleration = 3f;

    /// <summary>m/s: a run; faster bodies leave the view behind until the fade re-places it.</summary>
    public float MaxSpeed = 6f;

    /// <summary>A view move over this (m) in one frame is a snap or a re-place behind a fade, not motion.</summary>
    public const float JumpDistance = 1.5f;

    public Vector3 Velocity { get; private set; }

    /// <summary>The next view position from <paramref name="position"/> toward <paramref name="goal"/> (this frame's
    /// follow result).</summary>
    public Vector3 Step(Vector3 position, Vector3 goal, float dt)
    {
        if (!(dt > 0f))
            return position;
        var want = (goal - position) / dt;
        var dv = want - Velocity;
        float maxDv = MaxAcceleration * dt;
        if (dv.Length() > maxDv)
            dv = Vector3.Normalize(dv) * maxDv;
        var v = Velocity + dv;
        if (v.Length() > MaxSpeed)
            v = Vector3.Normalize(v) * MaxSpeed;
        Velocity = v;
        return position + v * dt;
    }

    public void Reset() => Velocity = Vector3.Zero;

    /// <summary>Speed (m/s) of a view move of <paramref name="delta"/> in <paramref name="dt"/>, for the comfort
    /// vignette; 0 for a snap or re-place (over <see cref="JumpDistance"/>), which a fade already covers.</summary>
    public static float ArtificialSpeed(Vector3 delta, float dt)
    {
        float d = delta.Length();
        return dt > 0f && d <= JumpDistance ? d / dt : 0f;
    }
}
