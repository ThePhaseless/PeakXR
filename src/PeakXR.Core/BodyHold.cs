using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Holds the body's horizontal mass centre still while the avatar leans. Bending moves the slippery ragdoll's
/// parts against the planted feet, and the contacts push the whole body sideways or toward the bend
/// (run 30: 0.02-0.03 m creep over a second even with <see cref="BodyLean.LegShift"/>, and 0.04 m on standing back
/// up), which the view rides. A gentle, clamped spring-damper acceleration (applied to every part, so it moves only
/// the mass centre, never the pose) pulls it back to where the lean started. It lets go for stick or room-scale
/// input, past <see cref="MaxOffset"/> (a real push or a slide), and at least <see cref="ReleaseSeconds"/> after the lean, once its push has gone quiet.
/// </summary>
public sealed class BodyHold
{
    /// <summary>Spring (1/s^2) and damper (1/s), about critically damped. Stiff enough that a back bend's
    /// contact creep (run 32: about 1.3 m/s^2, 0.064 m off a 20/s^2 spring) holds within a centimetre.</summary>
    public float Stiffness = 150f;
    public float Damping = 24f;

    /// <summary>Largest corrective acceleration (m/s^2): enough for the creep, small beside a shove.</summary>
    public float MaxAccel = 4f;

    /// <summary>Speed (m/s) above which the body is still coasting (a follow walk ending): it is braked and the
    /// anchor moves with it, so the hold keeps the spot it stops on (run 32 pulled it 0.08 m back). Creep is slower.</summary>
    public float CoastSpeed = 0.15f;

    /// <summary>Drift (m) beyond which the hold re-anchors instead of pulling the body back.</summary>
    public float MaxOffset = 0.1f;

    /// <summary>Seconds the hold lasts at least after the lean ends: the body creeps while standing back up.</summary>
    public float ReleaseSeconds = 1f;

    /// <summary>After <see cref="ReleaseSeconds"/>, the hold ends only once its push has stayed under
    /// <see cref="QuietAccel"/> (m/s^2) for <see cref="QuietSeconds"/>: the feet a lean planted can keep pushing
    /// after it (run 32 slid 0.026 m once let go), and letting go then would hand that creep back.</summary>
    public float QuietAccel = 0.3f;

    public float QuietSeconds = 0.5f;

    float _quiet;

    public Vector2 Anchor { get; private set; }
    public bool Holding { get; private set; }

    float _sinceLean = float.MaxValue;

    /// <param name="centre">Horizontal mass centre (x, z).</param>
    /// <param name="velocity">Its horizontal velocity.</param>
    /// <param name="leaning">The avatar is bent by the lean this tick.</param>
    /// <param name="free">Grounded, upright, no move input: nothing else is moving the body on purpose.</param>
    /// <returns>Acceleration (x, z) to add to every part.</returns>
    public Vector2 Update(Vector2 centre, Vector2 velocity, bool leaning, bool free, float dt)
    {
        if (!float.IsFinite(centre.X) || !float.IsFinite(centre.Y))
        {
            Holding = false;
            return Vector2.Zero;
        }
        if (!free)
            _sinceLean = float.MaxValue;
        else if (leaning)
            _sinceLean = 0f;
        else if (_sinceLean < float.MaxValue)
            _sinceLean += dt;
        // Input or a follow (or no lean yet) lets go at once; after a lean, only once the push has gone quiet.
        bool done =
            _sinceLean == float.MaxValue
            || _sinceLean >= ReleaseSeconds && (!Holding || _quiet >= QuietSeconds);
        if (done || Vector2.Distance(centre, Anchor) > MaxOffset)
        {
            Holding = false;
            _quiet = 0f;
            Anchor = centre;
            if (done)
                return Vector2.Zero;
        }
        if (!Holding)
        {
            Holding = true;
            Anchor = centre;
        }
        if (!float.IsFinite(velocity.X) || !float.IsFinite(velocity.Y))
            velocity = Vector2.Zero;
        if (velocity.Length() > CoastSpeed)
            Anchor = centre;
        var a = -Stiffness * (centre - Anchor) - Damping * velocity;
        float len = a.Length();
        _quiet = len < QuietAccel ? _quiet + dt : 0f;
        return len > MaxAccel ? a * (MaxAccel / len) : a;
    }
}
