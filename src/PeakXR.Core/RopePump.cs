using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Pumping a rope swing (decision D7): leaning the head along the swing pushes the held rope segment the same way,
/// as shifting your weight does on a real rope. Only the level part of the head's motion counts, and nothing
/// happens below the deadzone, so a still player swings exactly as vanilla.
/// </summary>
public static class RopePump
{
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
/// or one lean to grab never gets there. Gates the refusal for pumping a rope someone else owns. Allocation-free.
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

    public void Reset()
    {
        strokeDir = lastDir = Vector3.Zero;
        strokeTime = 0f;
        counted = false;
        lastAt = float.NegativeInfinity;
        count = 0;
    }

    /// <param name="headVelocity">The head's velocity relative to the body, world axes, m/s.</param>
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
