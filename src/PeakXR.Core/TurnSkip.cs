using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// The part of the lean's share of the stance debt that a turn made, which the lean leaves to the stance walk. The game
/// turns the body to the look about its mass centre with its eyes 0.24 m ahead, a real head turns about its neck, and a
/// snap or smooth turn pivots on the eye, so every turn moves the debt without the head leaving the body. Run 13 bent
/// the spine 9.5 degrees for a glance (vr.302) and 15-19 under a held turned head (vr.45b). The debt's change while the
/// look turns (and for <see cref="SwingSeconds"/> after, while the ragdoll torso swings onto the new heading) goes
/// into the skip; the skip never exceeds the share along it, so a walk that closes the debt closes the skip with it.
/// A stale skip once bent the lean away from the view (vr.300, run 13). Once the walk has been and gone, or none
/// came for <see cref="ReleaseAfter"/>, the skip lets go and the lean takes what is left: a held offset.
/// </summary>
public sealed class TurnSkip
{
    /// <summary>The look turns faster than this (degrees/s): a glance or head turn is 100-250, idle sway a few.</summary>
    public float TurningSpeed = 30f;

    /// <summary>Seconds after the look stops turning that the debt's change still counts as the turn's.</summary>
    public float SwingSeconds = 0.5f;

    /// <summary>Seconds after a turn with no stance walk before the lean takes the skip (past the walk's 1.5 s delay).</summary>
    public float ReleaseAfter = 3f;

    /// <summary>Rate (1/s) at which a released skip goes to the lean.</summary>
    public float ReleaseRate = 2f;

    /// <summary>Most of a turn's leftover the lean takes (m, about 6 degrees): the rest stays skipped, so a walk that
    /// left 0.10-0.14 m under a turned head no longer bends the body 7-12 degrees (run 15, vr.45b; want 8).</summary>
    public float MaxLean = 0.09f;

    Vector3 _last;
    float _lastYaw;
    bool _have;
    bool _walked;
    float _since = float.MaxValue;
    float _kept;

    /// <summary>The level offset the lean leaves out (world, m).</summary>
    public Vector3 Skip { get; private set; }

    /// <param name="share">The lean's share of the debt before this skip (world, m).</param>
    /// <param name="yaw">The body's heading, which the game keeps on the look (degrees).</param>
    /// <param name="walking">The stance walk is stepping the body.</param>
    /// <returns><see cref="Skip"/>.</returns>
    public Vector3 Update(Vector3 share, float yaw, bool walking, float dt)
    {
        share.Y = 0f;
        if (!_have)
        {
            _have = true;
            _last = share;
            _lastYaw = yaw;
            return Skip;
        }
        if (dt <= 0f)
            return Skip;
        if (MathF.Abs(BodySlotLayout.DeltaAngle(_lastYaw, yaw)) > TurningSpeed * dt)
        {
            _since = 0f;
            _walked = false;
        }
        else if (_since < float.MaxValue)
            _since += dt;
        if (walking)
            _walked = true;
        else if (_since <= SwingSeconds)
            Skip += share - _last;
        else if (_walked || _since > ReleaseAfter)
            Skip *= MathF.Max(0f, 1f - ReleaseRate * dt);
        // Along the share and no longer than it: the lean then bends toward the view by at most the debt. Released,
        // never shorter than the skip's own part of the share past MaxLean.
        float len2 = share.LengthSquared();
        float k = len2 > 1e-8f ? Math.Clamp(Vector3.Dot(Skip, share) / len2, 0f, 1f) : 0f;
        if (_kept > 0f && len2 > 1e-8f)
            k = MathF.Max(k, MathF.Min(_kept, MathF.Sqrt(len2) - MaxLean) / MathF.Sqrt(len2));
        Skip = share * k;
        // Kept from the turn and its walk; gone once the head is back within MaxLean of the body.
        _kept =
            len2 <= MaxLean * MaxLean ? 0f
            : _since <= SwingSeconds || walking ? MathF.Max(0f, Skip.Length() - MaxLean)
            : _kept;
        _last = share;
        _lastYaw = yaw;
        return Skip;
    }

    public void Reset()
    {
        Skip = Vector3.Zero;
        _kept = 0f;
        _have = false;
        _walked = false;
        _since = float.MaxValue;
    }
}
