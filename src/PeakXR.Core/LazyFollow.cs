using System;

namespace PeakXR.Core;

/// <summary>
/// Lazy follow for a world-locked panel (menus, popups, the top-of-view stamina meter): it stays put while the head
/// looks around it, so a menu never swims under the laser; once the head has been more than <see cref="Threshold"/>
/// off it for <see cref="Dwell"/> seconds it glides back in front on a critically damped spring, which starts and stops
/// gently, never overshoots and is capped at <see cref="MaxSpeed"/>. While held (the laser is on a menu or a control
/// is pressed) it never starts a glide and stops one where it is.
/// </summary>
public sealed class LazyFollow
{
    /// <summary>Degrees off a menu's centre before it follows, the <c>UI.MenuFollowAngle</c> default.</summary>
    public const float DefaultThreshold = 30f;

    /// <summary>Degrees past a wide panel's edge the head may look before it follows (<see cref="ThresholdFor"/>).</summary>
    public const float EdgeMargin = 5f;

    public float Threshold = DefaultThreshold;

    /// <summary>Seconds the head must stay past the threshold, so a glance never moves the panel.</summary>
    public float Dwell = 0.5f;

    /// <summary>Spring time of the glide, seconds: about the time to cover most of the way.</summary>
    public float SmoothTime = 0.35f;

    /// <summary>Fastest the glide turns, degrees per second.</summary>
    public float MaxSpeed = 120f;

    /// <summary>Degrees from the head at which a glide ends.</summary>
    public float Settle = 1f;

    private float away,
        yawSpeed,
        pitchSpeed;

    /// <summary>Panel heading, degrees (-180, 180].</summary>
    public float Yaw { get; private set; }

    /// <summary>Panel pitch, degrees (only for panels that follow up and down too; menus pass 0).</summary>
    public float Pitch { get; private set; }

    public bool Gliding { get; private set; }

    /// <summary>The threshold for a panel <paramref name="halfSpanDegrees"/> wide on each side of its centre: a wide
    /// panel (the 58 degree title screen) is read at its edges, which must not move it.</summary>
    public static float ThresholdFor(float threshold, float halfSpanDegrees) =>
        MathF.Max(threshold, halfSpanDegrees + EdgeMargin);

    /// <summary>Places the panel at once (it opened), with no glide or dwell carried over.</summary>
    public void Snap(float yaw, float pitch = 0f)
    {
        Yaw = LookMath.WrapDegrees(yaw);
        Pitch = pitch;
        Gliding = false;
        away = yawSpeed = pitchSpeed = 0f;
    }

    /// <summary>One frame toward the head's <paramref name="yaw"/> and <paramref name="pitch"/>.</summary>
    /// <returns>True when the panel moved this frame.</returns>
    public bool Update(float yaw, float pitch, bool hold, float dt)
    {
        float dy = LookMath.WrapDegrees(yaw - Yaw);
        float dp = pitch - Pitch;
        if (hold || dt <= 0f)
        {
            if (hold)
            {
                Gliding = false;
                away = yawSpeed = pitchSpeed = 0f;
            }
            return false;
        }
        if (!Gliding)
        {
            away = dy * dy + dp * dp > Threshold * Threshold ? away + dt : 0f;
            if (away < Dwell)
                return false;
            Gliding = true;
            away = 0f;
        }
        Yaw = LookMath.WrapDegrees(Yaw + Damp(dy, ref yawSpeed, dt));
        Pitch += Damp(dp, ref pitchSpeed, dt);
        dy = LookMath.WrapDegrees(yaw - Yaw);
        dp = pitch - Pitch;
        if (dy * dy + dp * dp <= Settle * Settle)
        {
            Gliding = false;
            yawSpeed = pitchSpeed = 0f;
        }
        return true;
    }

    /// <summary>Step of a critically damped spring closing a <paramref name="gap"/> (target minus current), the
    /// Game Programming Gems 4 form Unity's SmoothDamp uses, clamped so it never passes the target.</summary>
    private float Damp(float gap, ref float speed, float dt)
    {
        float omega = 2f / MathF.Max(SmoothTime, 1e-4f);
        float x = omega * dt;
        float decay = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
        float limit = MaxSpeed * SmoothTime;
        float change = Math.Clamp(-gap, -limit, limit);
        float temp = (speed + omega * change) * dt;
        speed = (speed - omega * temp) * decay;
        float step = -change + (change + temp) * decay;
        // A step past the target ends on it.
        if (gap > 0f == step > gap)
        {
            speed = 0f;
            return gap;
        }
        return step;
    }
}
