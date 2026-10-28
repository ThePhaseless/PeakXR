using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Pumping a rope swing (decision D7): leaning the head along the swing pushes the held rope segment the same way,
/// as shifting your weight does on a real rope. Only the level part of the head's motion counts, and nothing
/// happens below the deadzone, so a still player swings exactly as vanilla.
/// </summary>
public static class RopePump
{
    /// <summary>The eyes' offset from the neck's pivot in head space (up, forward), metres: a typical neck model.
    /// Turning or nodding the head swings the tracked eyes about this pivot without shifting any weight.</summary>
    public static readonly Vector3 NeckToEye = new(0f, 0.075f, 0.08f);

    private const float Gravity = 9.81f;

    /// <summary>The neck's pivot under the tracked eyes. A lean moves it; a look round or a nod does not, so the pump
    /// reads this instead of the eyes: a quick look round swings the eyes about 0.1 m at 0.4 m/s and more.</summary>
    public static Vector3 Neck(Vector3 eye, Quaternion rotation) =>
        eye - Vector3.Transform(NeckToEye, rotation);

    /// <param name="headVelocity">The tracked head's velocity relative to the body, world axes, m/s.</param>
    /// <param name="deadzone">Level head speed (m/s) below which nothing happens.</param>
    /// <param name="gain">Acceleration (m/s²) per m/s of head speed.</param>
    /// <param name="maxAcceleration">Cap on the result (m/s²).</param>
    /// <returns>Level acceleration to add to the held segment.</returns>
    public static Vector3 Push(
        Vector3 headVelocity,
        float deadzone,
        float gain,
        float maxAcceleration
    )
    {
        var level = new Vector3(headVelocity.X, 0f, headVelocity.Z);
        float speed = level.Length();
        if (speed < deadzone || speed < 1e-6f)
            return Vector3.Zero;
        float accel = speed * gain;
        if (accel > maxAcceleration)
            accel = maxAcceleration;
        return level * (accel / speed);
    }

    /// <summary>
    /// How far the climb point would swing out from under the rope's anchor (metres, level) if nothing pushed it: the
    /// pendulum's amplitude from its offset and its speed, sqrt(x² + v²·ℓ/g), with ℓ the drop from the anchor. A speed
    /// ceiling alone never bounded the swing: pushes below it near the ends of each swing kept adding energy.
    /// </summary>
    /// <param name="point">The climb point, world, metres.</param>
    /// <param name="anchor">The rope's anchored end, world, metres.</param>
    /// <param name="swingVelocity">The climb point's velocity, world axes, m/s.</param>
    public static float Amplitude(Vector3 point, Vector3 anchor, Vector3 swingVelocity)
    {
        var x = new Vector2(point.X - anchor.X, point.Z - anchor.Z);
        var v = new Vector2(swingVelocity.X, swingVelocity.Z);
        float length = MathF.Max(anchor.Y - point.Y, 0.5f);
        return MathF.Sqrt(x.LengthSquared() + v.LengthSquared() * length / Gravity);
    }

    /// <summary>
    /// The comfort ceiling on a pumped swing: once its <see cref="Amplitude"/> reaches <paramref name="maxAmplitude"/>
    /// (metres), a push that would add energy (one along the swing's level velocity, or any push while it stands at
    /// the end of a swing) is dropped, so the view's sway stays bounded however hard the player pumps. A push against
    /// the swing still brakes it.
    /// </summary>
    /// <param name="push">Acceleration from <see cref="Push"/>.</param>
    /// <param name="swingVelocity">The climb point's velocity, world axes, m/s.</param>
    /// <param name="amplitude">The swing's <see cref="Amplitude"/>.</param>
    public static Vector3 Limit(
        Vector3 push,
        Vector3 swingVelocity,
        float amplitude,
        float maxAmplitude
    )
    {
        var swing = new Vector3(swingVelocity.X, 0f, swingVelocity.Z);
        return amplitude >= maxAmplitude && Vector3.Dot(push, swing) >= 0f ? Vector3.Zero : push;
    }

    /// <summary>
    /// A pump (<paramref name="push"/> above 0, from <see cref="Push"/>) on a rope this player cannot push, since
    /// only its owner simulates it: true once per hold, so the lean gets one refusal instead of silence or a stream.
    /// </summary>
    public static bool RefuseOnce(bool canPush, float push, ref bool refusedThisHold)
    {
        if (canPush || push <= 0f || refusedThisHold)
            return false;
        refusedThisHold = true;
        return true;
    }
}

/// <summary>
/// Whether the player is clearly pumping a rope, not just moving: a rhythm of level head leans back and forth, each
/// at least <see cref="MinHeadSpeed"/> for <see cref="MinStroke"/>, <see cref="Strokes"/> of them with at most
/// <see cref="MaxGap"/> between, and in phase with the swing whenever the rope swings. Looking around, turning the head
/// or one lean to grab never gets there. Gates the push itself (a lean in to catch the rope swung it about 1 m) and
/// the refusal for pumping a rope someone else owns. Allocation-free.
/// </summary>
public sealed class PumpIntent
{
    public const float MinHeadSpeed = 0.4f;
    public const float MinSwingSpeed = 0.1f;
    public const float MinStroke = 0.15f;
    public const int Strokes = 3;
    public const float MaxGap = 2f;

    private Vector3 strokeDir;
    private float strokeTime;
    private bool counted;
    private Vector3 lastDir;
    private float lastAt = float.NegativeInfinity;
    private int count;

    /// <summary>Strokes in the current rhythm (self-test vr.42e checks a lean reaches <see cref="Strokes"/> - 1).</summary>
    public int Count => count;

    public void Reset()
    {
        strokeDir = lastDir = Vector3.Zero;
        strokeTime = 0f;
        counted = false;
        lastAt = float.NegativeInfinity;
        count = 0;
    }

    /// <param name="headVelocity">The head's (<see cref="RopePump.Neck"/>) velocity relative to the body, world axes,
    /// m/s.</param>
    /// <param name="swingVelocity">The climb point's velocity on the rope, world axes, m/s.</param>
    /// <returns>True while the pumping rhythm holds.</returns>
    public bool Update(Vector3 headVelocity, Vector3 swingVelocity, float now, float dt)
    {
        var head = new Vector3(headVelocity.X, 0f, headVelocity.Z);
        var swing = new Vector3(swingVelocity.X, 0f, swingVelocity.Z);
        float speed = head.Length();
        bool stroking = speed >= MinHeadSpeed;
        // A swinging rope: only a lean the way it is moving pumps it.
        if (stroking && swing.Length() >= MinSwingSpeed)
            stroking = Vector3.Dot(head, swing) > 0f;
        if (!stroking)
        {
            strokeTime = 0f;
            strokeDir = Vector3.Zero;
        }
        else
        {
            var dir = head / speed;
            if (strokeDir != Vector3.Zero && Vector3.Dot(dir, strokeDir) > 0f)
                strokeTime += dt;
            else
            {
                strokeDir = dir;
                strokeTime = dt;
                counted = false;
            }
            if (!counted && strokeTime >= MinStroke)
            {
                counted = true;
                if (now - lastAt > MaxGap)
                    count = 0;
                // Back and forth: a stroke the same way as the last is the same lean going on.
                if (count == 0 || Vector3.Dot(strokeDir, lastDir) < 0f)
                    count++;
                lastDir = strokeDir;
                lastAt = now;
            }
        }
        if (now - lastAt > MaxGap)
            count = 0;
        return count >= Strokes;
    }
}
