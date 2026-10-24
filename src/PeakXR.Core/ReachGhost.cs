using System;

namespace PeakXR.Core;

/// <summary>
/// How strongly to draw the ghost hand at a controller the avatar's hand cannot reach: none while the avatar's hand
/// is within <see cref="Enter"/> of where the controller puts it, rising to full at <see cref="Full"/>. Once shown it
/// stays until the gap falls under <see cref="Exit"/>, and the alpha eases at <see cref="Rate"/>, so tracking jitter at
/// the edge never flickers it.
/// </summary>
public sealed class GhostFade
{
    public const float Enter = 0.05f;
    public const float Exit = 0.03f;
    public const float Full = 0.15f;

    /// <summary>Alpha change per second.</summary>
    public const float Rate = 6f;

    private bool shown;

    /// <summary>0 hidden .. 1 fully drawn.</summary>
    public float Alpha { get; private set; }

    public bool Visible => Alpha > 0f;

    /// <param name="gap">Avatar hand to where its controller puts it, metres.</param>
    public float Update(float gap, float dt)
    {
        shown = shown ? gap >= Exit : gap > Enter;
        float target = shown ? Math.Clamp((gap - Exit) / (Full - Exit), 0.05f, 1f) : 0f;
        float step = Rate * MathF.Max(dt, 0f);
        Alpha = Alpha < target ? MathF.Min(target, Alpha + step) : MathF.Max(target, Alpha - step);
        return Alpha;
    }

    public void Reset()
    {
        shown = false;
        Alpha = 0f;
    }
}
