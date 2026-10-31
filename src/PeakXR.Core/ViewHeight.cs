using System;

namespace PeakXR.Core;

/// <summary>
/// Keeps the first-person view's height over the ground under its feet while only idling or PeakXR's own motion moves
/// the body (a lean, a head turn, the stance walk and its settle). The view rode the hip's height, which the game's
/// stand force sets from the footing and the animation's head target: on level ground a bend, a turned head or a
/// stance walk's new footing moved it 0.03-0.1 m (run 14: the view rose and fell 0.061 m in vr.45's slow leans,
/// 0.088 m in vr.45b, 0.054 m in vr.273, while only the head moved). Held, the view follows the ground (a step up
/// still lifts it) and the tracked head gives every real duck or rise. A closed gate (a jump, a fall, a crouch, stick
/// walking, an outside force) releases it onto the body, and a body more than <see cref="Deadband"/> off the held
/// height carries it along. Vertical only: <see cref="ViewHold"/> keeps the idle sway out of the level view.
/// </summary>
public sealed class ViewHeight
{
    /// <summary>Seconds the gate must stay open (on ground) before the height holds: a body still rising from a
    /// crouch or settling from a landing is followed, not frozen part way.</summary>
    public const float EngageSeconds = 0.5f;

    /// <summary>Most the held view may sit off the body's own height (m). Above the 0.105 m vr.273's step back raised
    /// the hip in run 14; a body further off stands on something the ground ray missed.</summary>
    public const float Deadband = 0.12f;

    /// <summary>Time constant (s) of the release onto the body.</summary>
    public const float ReleaseSeconds = 0.15f;

    // The view's height over the ground while held (tracked the rest of the time), and its offset from the body.
    private float above;
    private float off;
    private float openFor;
    private bool have;

    /// <summary>The height is held this update.</summary>
    public bool Holding { get; private set; }

    /// <summary>The view's height less the body's (m), for tracing.</summary>
    public float Offset => off;

    /// <param name="body">The view's height without the hold (the body's eye anchor).</param>
    /// <param name="ground">Height of the ground under the feet (a lean leaves them planted, #107); NaN without ground.</param>
    /// <param name="open">Only idling or PeakXR's own motion moves the body.</param>
    /// <param name="dt">Seconds since the last update.</param>
    /// <returns>The view's height.</returns>
    public float Update(float body, float ground, bool open, float dt)
    {
        bool grounded = !float.IsNaN(ground);
        openFor = open && grounded ? openFor + dt : 0f;
        Holding = have && openFor >= EngageSeconds;
        if (Holding)
            off = Math.Clamp(ground + above - body, -Deadband, Deadband);
        else
            off *= MathF.Exp(-dt / ReleaseSeconds);
        float output = body + off;
        have = grounded;
        if (grounded)
            above = output - ground;
        return output;
    }

    /// <summary>Forget the held height: the next update starts on the body (a new view, a warp).</summary>
    public void Reset()
    {
        have = false;
        Holding = false;
        openFor = 0f;
        off = 0f;
    }
}
