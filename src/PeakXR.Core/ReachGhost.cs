using System;

namespace PeakXR.Core;

/// <summary>
/// How strongly to draw the ghost hand at a controller the avatar's arm cannot reach: fed the arm's geometric
/// shortfall (shoulder to controller minus arm length, never lag), none under <see cref="Enter"/>, rising to full at
/// <see cref="Full"/>. A shortfall must last <see cref="Persist"/> before it first shows; until it falls under
/// <see cref="Exit"/> it may fade back in at once, and the alpha eases at <see cref="Rate"/>, so the edge only ever
/// shimmers faintly.
/// </summary>
public sealed class GhostFade
{
    public const float Enter = 0.05f;
    public const float Exit = 0.03f;
    public const float Full = 0.15f;

    /// <summary>Alpha change per second.</summary>
    public const float Rate = 6f;

    /// <summary>Seconds a shortfall past <see cref="Enter"/> lasts before the ghost shows.</summary>
    public const float Persist = 0.15f;

    private bool shown;
    private float pending;

    /// <summary>0 hidden .. 1 fully drawn.</summary>
    public float Alpha { get; private set; }

    public bool Visible => Alpha > 0f;

    /// <param name="gap">How far the arm falls short of its controller, metres.</param>
    public float Update(float gap, float dt)
    {
        if (shown)
        {
            shown = gap >= Exit;
            // The next shortfall waits its own Persist; the old count would re-show it at once (flicker).
            if (!shown)
                pending = 0f;
        }
        else
        {
            pending = gap > Enter ? pending + MathF.Max(dt, 0f) : 0f;
            shown = pending >= Persist;
        }
        // Drawn only past Enter: a straight arm at rest reads a few centimetres short (vr70) and must not show it.
        // The hysteresis keeps the shown state, so a return past Enter fades straight back without the delay.
        float target = shown ? Math.Clamp((gap - Enter) / (Full - Enter), 0f, 1f) : 0f;
        float step = Rate * MathF.Max(dt, 0f);
        Alpha = Alpha < target ? MathF.Min(target, Alpha + step) : MathF.Max(target, Alpha - step);
        return Alpha;
    }

    /// <summary>How far a hand reaches past the arm from where the shoulder belongs under the tracked head: the
    /// avatar's <paramref name="shoulder"/> moved by <paramref name="bodyToHead"/> (tracked head minus the avatar's
    /// eyes), so a body that lags the head (a warp, a recenter, a push) is not mistaken for a reach (vr72).</summary>
    public static float Shortfall(
        System.Numerics.Vector3 shoulder,
        System.Numerics.Vector3 bodyToHead,
        System.Numerics.Vector3 hand,
        float armLength
    ) => WeldGate.Shortfall(shoulder + bodyToHead, hand, armLength);

    public void Reset()
    {
        shown = false;
        pending = 0f;
        Alpha = 0f;
    }
}
