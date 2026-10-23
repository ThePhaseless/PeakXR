using System;

namespace PeakXR.Core;

/// <summary>
/// Target prompts draw on top of the world so smoke and particles never swallow them, but text drawn in front of rock
/// that hides its target sends a conflicting depth cue. A terrain hit on the line from the eye to the prompt dims it
/// to <see cref="DimAlpha"/>; hits at or past the target's own surface (the target itself, the ledge an item lies on)
/// do not count.
/// </summary>
public static class PromptOcclusion
{
    /// <summary>Alpha of a prompt behind rock: still findable, plainly not in front.</summary>
    public const float DimAlpha = 0.3f;

    /// <summary>Seconds between terrain checks: a few per second is enough for something that eases anyway.</summary>
    public const float CheckSeconds = 0.25f;

    /// <summary>Seconds a full swing between dimmed and full takes.</summary>
    public const float FadeSeconds = 0.15f;

    /// <summary>Hits this much (plus the target's radius) short of the prompt still count as its own surface.</summary>
    public const float MinMargin = 0.25f;

    /// <summary>Whether a terrain hit <paramref name="hitDistance"/> metres from the eye (negative: none) hides a
    /// prompt <paramref name="promptDistance"/> metres away above a target of <paramref name="targetRadius"/>.</summary>
    public static bool Blocked(float hitDistance, float promptDistance, float targetRadius) =>
        hitDistance >= 0f && hitDistance < promptDistance - MinMargin - MathF.Max(0f, targetRadius);

    /// <summary>The prompt's alpha <paramref name="dt"/> seconds on from <paramref name="alpha"/>, easing toward
    /// <see cref="DimAlpha"/> while <paramref name="blocked"/>, else toward 1.</summary>
    public static float Ease(float alpha, bool blocked, float dt)
    {
        float target = blocked ? DimAlpha : 1f;
        float step = (1f - DimAlpha) / FadeSeconds * MathF.Max(0f, dt);
        return alpha < target ? MathF.Min(target, alpha + step) : MathF.Max(target, alpha - step);
    }
}
