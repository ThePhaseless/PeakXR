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
}
