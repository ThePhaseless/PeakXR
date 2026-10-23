using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// When the held item may ride the ragdoll hand it is welded to, and when it must leave it. The avatar's arm stops
/// at its own reach from a shoulder that stays at standing height, so a hand stooped toward the ground (a fuse, a
/// fire, a build spot) drags the item away from the controller. The rule reads the arm, not the item: the weld lets
/// go only when the ragdoll hand itself is more than <see cref="Release"/> from its controller for <see cref="Hold"/>
/// seconds (an item's grip offset never counts, vr53 vr.230), and rejoins once the hand is back within
/// <see cref="Rejoin"/> of its controller and of the item's grip.
/// </summary>
public sealed class WeldGate
{
    /// <summary>Ragdoll hand to its controller that releases the weld, metres.</summary>
    public float Release = 0.15f;

    /// <summary>Ragdoll hand to its controller, and to the free item's grip, that rejoins it, metres.</summary>
    public float Rejoin = 0.05f;

    /// <summary>Each change waits this long, seconds, so an arm trailing a fast swing does not flip it.</summary>
    public float Hold = 0.08f;

    private float timer;

    public bool Released { get; private set; }

    /// <param name="handToController">Ragdoll hand to where its controller puts the hand bone, metres.</param>
    /// <param name="handToItem">Ragdoll hand to where the item's grip puts the hand bone, metres.</param>
    public bool Update(float handToController, float handToItem, float dt)
    {
        bool toward = Released
            ? handToController < Rejoin && handToItem < Rejoin
            : handToController > Release;
        timer = toward ? timer + dt : 0f;
        if (timer >= Hold)
        {
            Released = !Released;
            timer = 0f;
        }
        return Released;
    }

    public void Reset()
    {
        Released = false;
        timer = 0f;
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
