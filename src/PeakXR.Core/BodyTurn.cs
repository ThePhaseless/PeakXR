using System;

namespace PeakXR.Core;

/// <summary>
/// The heading the local body faces under a head that turns on its neck. The game turns the whole ragdoll to the look
/// every physics step, so a glance swings the feet across the ground, about a mass centre they stand 0.07-0.12 m behind:
/// they land off their spot, the body then slides over them for seconds, and all a hold can do is delay that (vr.302,
/// #50), or hand it to the view under wind (vr.305, #139). A person's feet stay put for a glance. So the body stays on
/// its heading until the head has stayed over <see cref="QuietDegrees"/> off for <see cref="QuietSeconds"/> or passes
/// <see cref="MaxOff"/>, then turns at <see cref="Rate"/>. A snap or smooth turn is no glance: the body turns with it. While anything but standing moves the body, it catches up at
/// <see cref="CatchUpRate"/>: the stick and the walk go along the look, and a body in the air has no feet to load.
/// </summary>
public sealed class BodyTurn
{
    /// <summary>Seconds the head must stay turned before the body comes: past vr.302's glance, 1 s out and back.</summary>
    public float QuietSeconds = 1f;

    /// <summary>A look staying within this (degrees) of where it settled is quiet. Measured over a band, not per step,
    /// so it reads the same at every frame rate: at 25 fps a physics step carries a whole frame or nothing. A head this
    /// near the body's heading turns no feet.</summary>
    public float QuietDegrees = 5f;

    /// <summary>How far a head turns on its neck (degrees): past it the body comes at once.</summary>
    public float MaxOff = 80f;

    /// <summary>How fast the standing body turns (degrees/s): a person turning on the spot.</summary>
    public float Rate = 120f;

    /// <summary>How fast the body catches up while it is not just standing (degrees/s): about 0.1 s for a turned head.</summary>
    public float CatchUpRate = 720f;

    /// <summary>The look's gap to the body (degrees) under which the eye model samples the standing eyes
    /// (rig.camera): a head turned inside it would carry its turn into the model.</summary>
    public const float HeadSlack = 6f;

    float _quiet;
    float _quietFrom;
    bool _settled;

    /// <summary>The body's heading (degrees), NaN before the first <see cref="Update"/>.</summary>
    public float Heading { get; private set; } = float.NaN;

    /// <param name="look">The look's heading (degrees).</param>
    /// <param name="free">The body just stands (<see cref="HoldGate.Free"/>, whatever an outside force does).</param>
    /// <param name="rigTurn">How far a snap or smooth turn turned the rig since the last update (degrees): the player
    /// turning the whole body, hands and all, so the body comes along at once.</param>
    /// <returns><see cref="Heading"/>.</returns>
    public float Update(float look, bool free, float dt, float rigTurn = 0f)
    {
        if (float.IsNaN(Heading))
        {
            Heading = _quietFrom = look;
            return Heading;
        }
        Heading += rigTurn;
        if (MathF.Abs(BodySlotLayout.DeltaAngle(_quietFrom, look)) > QuietDegrees)
        {
            _quietFrom = look;
            _quiet = 0f;
            _settled = false;
        }
        else
            _quiet += dt;
        // The head has come to rest here. The body goes to this spot, not to the look, so the first degrees of a glance
        // and a real head's jitter inside the band turn nothing.
        if (!_settled && _quiet >= QuietSeconds)
        {
            _quietFrom = look;
            _settled = true;
        }
        float off = BodySlotLayout.DeltaAngle(Heading, free && _settled ? _quietFrom : look);
        float rate =
            !free ? CatchUpRate
            : _settled || MathF.Abs(off) > MaxOff ? Rate
            : 0f;
        // The catch-up ends here, so a rest spot from before it can't turn a standing body back.
        if (!free)
            _quietFrom = look;
        float step = rate * dt;
        if (step > 0f)
            Heading = MathF.Abs(off) <= step ? Heading + off : Heading + MathF.Sign(off) * step;
        return Heading;
    }

    /// <summary>The rig's yaw (degrees) as last counted by <see cref="TurnRig"/>.</summary>
    public float RigYaw { get; private set; }

    /// <summary>Counts the rig's yaw at a physics step, however many frames it turned over. Runs through a
    /// <see cref="Reset"/> too, so a turn made with no heading is never counted later.</summary>
    /// <returns>The turn since the last count (degrees), for <see cref="Update"/>.</returns>
    public float TurnRig(float rigYaw)
    {
        float turn = BodySlotLayout.DeltaAngle(RigYaw, rigYaw);
        RigYaw = rigYaw;
        return turn;
    }

    /// <summary><see cref="Heading"/> with the rig's turn since the last count: the next step stands on it, and a pose
    /// built between steps has to turn with it now.</summary>
    public float HeadingAt(float rigYaw) => Heading + BodySlotLayout.DeltaAngle(RigYaw, rigYaw);

    /// <summary>What the local head takes of the look on top of the body's <paramref name="heading"/> (degrees), for what
    /// shows or reads the head: none within <see cref="HeadSlack"/>, or with no heading (NaN).</summary>
    public static float HeadOnNeck(float heading, float look)
    {
        if (float.IsNaN(heading))
            return 0f;
        float off = BodySlotLayout.DeltaAngle(heading, look);
        return off - Math.Clamp(off, -HeadSlack, HeadSlack);
    }

    public void Reset()
    {
        Heading = float.NaN;
        _quiet = 0f;
        _settled = false;
    }
}
