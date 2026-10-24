using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// When the held item may ride the ragdoll hand it is welded to, and when it must leave it. The avatar's arm stops
/// at its own reach from a shoulder at standing height, so a hand stooped toward the ground (a fuse, a fire, a build
/// spot) drags the item away from the controller. The rule reads the arm, not the item: the weld lets go only when
/// the ragdoll hand itself stays more than <see cref="Release"/> from its controller for <see cref="ReleaseHold"/>
/// (long enough for a physical crouch to lower the shoulder, vr61 vr.230; an item's grip offset never counts, vr53).
/// It rejoins once the hand is within <see cref="Rejoin"/> of its controller and the free item is within
/// <see cref="Release"/> of the hand (the rejoin puts the hand on the item's grip), and a fresh weld stays for
/// <see cref="Settle"/> before it may let go again, so the rejoin's own jolt never flips it back.
/// </summary>
public sealed class WeldGate
{
    /// <summary>Ragdoll hand to its controller that releases the weld, metres.</summary>
    public float Release = 0.15f;

    /// <summary>Ragdoll hand to its controller that rejoins it, metres.</summary>
    public float Rejoin = 0.05f;

    /// <summary>How long the arm must stay out of reach before the weld lets go, seconds.</summary>
    public float ReleaseHold = 0.2f;

    /// <summary>How long the arm must be back before the item rejoins, seconds.</summary>
    public float RejoinHold = 0.08f;

    /// <summary>A fresh weld holds at least this long, seconds.</summary>
    public float Settle = 0.5f;

    private float timer;
    private float settle;

    public bool Released { get; private set; }

    /// <param name="handToController">Ragdoll hand to where its controller puts the hand bone, metres.</param>
    /// <param name="handToItem">Ragdoll hand to where the item's grip puts the hand bone, metres.</param>
    public bool Update(float handToController, float handToItem, float dt)
    {
        if (settle > 0f)
            settle -= dt;
        bool toward = Released
            ? handToController < Rejoin && handToItem < Release
            : settle <= 0f && handToController > Release;
        timer = toward ? timer + dt : 0f;
        if (timer >= (Released ? RejoinHold : ReleaseHold))
        {
            Released = !Released;
            timer = 0f;
            if (!Released)
                settle = Settle;
        }
        return Released;
    }

    public void Reset()
    {
        Released = false;
        timer = 0f;
        settle = 0f;
    }
}

/// <summary>A held item carried by its rigidbody's velocity onto the controller-held pose: the hand's own motion plus a
/// fast pull onto the target, so it neither lags a moving hand nor springs past it.</summary>
public static class ItemFollow
{
    /// <summary>Time constant of the pull onto the target, seconds.</summary>
    public const float Tau = 0.06f;

    /// <summary>Fastest the item is carried, m/s (a hand swing; a thrown item leaves the hand first).</summary>
    public const float MaxSpeed = 15f;

    /// <summary>Fastest spin, rad/s.</summary>
    public const float MaxSpin = 30f;

    /// <summary>A free item farther than this from its place is put there at once, metres: a chase at
    /// <see cref="MaxSpeed"/> falls behind a target that jumps (a recentre, a warp) or something knocks it off.</summary>
    public const float SnapGap = 0.3f;

    public static bool Snaps(float gap) => gap > SnapGap;

    public static Vector3 Velocity(Vector3 position, Vector3 target, Vector3 handVelocity, float dt)
    {
        var v = handVelocity + (target - position) * Gain(dt);
        float speed = v.Length();
        return speed > MaxSpeed ? v * (MaxSpeed / speed) : v;
    }

    /// <summary>Angular velocity (rad/s, world axis) that turns <paramref name="rotation"/> onto
    /// <paramref name="target"/>.</summary>
    public static Vector3 Spin(Quaternion rotation, Quaternion target, float dt)
    {
        var delta = Quaternion.Normalize(target * Quaternion.Inverse(rotation));
        if (delta.W < 0f)
            delta = new Quaternion(-delta.X, -delta.Y, -delta.Z, -delta.W);
        float sin = MathF.Sqrt(delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z);
        if (sin < 1e-6f)
            return Vector3.Zero;
        float angle = 2f * MathF.Atan2(sin, delta.W);
        var axis = new Vector3(delta.X, delta.Y, delta.Z) / sin;
        return axis * MathF.Min(angle * Gain(dt), MaxSpin);
    }

    /// <summary>Never more than the whole gap in one tick.</summary>
    private static float Gain(float dt) => dt > 0f ? MathF.Min(1f / Tau, 1f / dt) : 1f / Tau;
}
