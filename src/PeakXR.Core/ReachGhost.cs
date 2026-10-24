using System;

namespace PeakXR.Core;

/// <summary>
/// How strongly to draw the ghost hand at a controller the avatar's arm cannot reach: fed the arm's geometric
/// shortfall (shoulder to controller minus arm length, never lag), none under <see cref="Enter"/>, rising to full at
/// <see cref="Full"/>. A shortfall must last <see cref="Persist"/> before it shows; once shown it stays until the
/// shortfall falls under <see cref="Exit"/>, and the alpha eases at <see cref="Rate"/>, so the edge never flickers.
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
            shown = gap >= Exit;
        else
        {
            pending = gap > Enter ? pending + MathF.Max(dt, 0f) : 0f;
            shown = pending >= Persist;
        }
        float target = shown ? Math.Clamp((gap - Exit) / (Full - Exit), 0.05f, 1f) : 0f;
        float step = Rate * MathF.Max(dt, 0f);
        Alpha = Alpha < target ? MathF.Min(target, Alpha + step) : MathF.Max(target, Alpha - step);
        return Alpha;
    }

    public void Reset()
    {
        shown = false;
        pending = 0f;
        Alpha = 0f;
    }
}
