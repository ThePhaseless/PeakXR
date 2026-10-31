using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Closes what the lean leaves between the drawn eyes and their goal once the head holds still. The bend is open loop
/// and falls short (run 12): restoring the head's rotation after the swing moves the animated eye only 0.55 of its
/// want (the head joint's lever, AvatarPoseDriver.ApplyLean), the ragdoll follows part of that (vr.45: animated
/// shoulders 0.135 m, ragdoll 0.073 m), and the eye model sits 0.02-0.045 m off the renderers. So a walk's
/// leftover of 0.1-0.15 m left the drawn eyes 0.07-0.11 m off the view. This integrates the drawn eyes' miss into
/// an extra head offset for the lean, slowly and level only, while the lean's want holds still: a head walking away
/// is the stance walk's to close, and a moving body would wind it up.
/// </summary>
public sealed class LeanTrim
{
    /// <summary>Integral rate (1/s): a 0.1 m miss closes in about 1 s at the bend's 0.4 reach, and the loop stays calm
    /// against the ragdoll's own lag (about 0.15 s) even if the bend reached all of it.</summary>
    public float Rate = 5f;

    /// <summary>Miss (m) left alone, taken off continuously: the hip's idle sway (<see cref="BodyLean.WantDeadband"/>).</summary>
    public float Deadband = BodyLean.WantDeadband;

    /// <summary>Largest trim (m): with the bend reaching 0.4 of its want, a walk's 0.15 m leftover (StanceWalk.Again)
    /// and the eye model's 0.045 m error need about 0.29 m more. The bend's own limits still apply.</summary>
    public float Max = 0.3f;

    /// <summary>Rate (1/s) at which the trim lets go while it may not run.</summary>
    public float Decay = 2f;

    /// <summary>The want counts as held while it moves slower than this (m/s)...</summary>
    public float HeldSpeed = 0.15f;

    /// <summary>...for this long (s).</summary>
    public float HeldSeconds = 0.3f;

    Vector3 _lastWant;
    bool _have;
    float _heldFor;

    /// <summary>The level offset to add to the head the lean aims for (world, m).</summary>
    public Vector3 Offset { get; private set; }

    /// <param name="want">The lean's goal for the eyes, from the eye model (world, m).</param>
    /// <param name="miss">That goal less the drawn eyes (world, m).</param>
    /// <param name="open">The lean may run and the drawn eyes are a fair measure (gate open, upright, look level).</param>
    /// <returns><see cref="Offset"/>.</returns>
    public Vector3 Update(Vector3 want, Vector3 miss, bool open, float dt)
    {
        if (!open)
        {
            // A closed gate measures nothing: the goal must hold still again once it reopens.
            _have = false;
            _heldFor = 0f;
            Offset *= MathF.Max(0f, 1f - Decay * dt);
            return Offset;
        }
        want.Y = 0f;
        bool still = _have && dt > 0f && Vector3.Distance(want, _lastWant) <= HeldSpeed * dt;
        _heldFor = still ? _heldFor + dt : 0f;
        _lastWant = want;
        _have = true;
        if (_heldFor < HeldSeconds)
        {
            Offset *= MathF.Max(0f, 1f - Decay * dt);
            return Offset;
        }
        miss.Y = 0f;
        float len = miss.Length();
        if (len > Deadband)
        {
            var next = Offset + miss * ((len - Deadband) / len * Rate * dt);
            float n = next.Length();
            Offset = n > Max ? next * (Max / n) : next;
        }
        return Offset;
    }

    public void Reset()
    {
        Offset = Vector3.Zero;
        _have = false;
        _heldFor = 0f;
    }
}
