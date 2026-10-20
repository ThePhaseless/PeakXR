using System;

namespace PeakXR.Core;

/// <summary>
/// Math for the VR speaking icon above remote players. The game only shows who is talking on the
/// name tag, which needs a look at the player; in VR the icon must stay readable at any distance and
/// in both eyes, so it is sized by visual angle instead of screen pixels.
/// </summary>
public static class VoiceIndicator
{
    /// <summary>Sprite for a mouth amplitude index, the same mapping as the game's name tag
    /// (UIPlayerNames.UpdateName). -1 when there are no sprites.</summary>
    public static int SpriteIndex(int amplitudeIndex, int spriteCount)
    {
        if (spriteCount <= 0)
            return -1;
        int i = amplitudeIndex * 2;
        return i < 0 ? 0
            : i > spriteCount - 1 ? spriteCount - 1
            : i;
    }

    /// <summary>World size (metres) that covers <paramref name="degrees"/> of view at
    /// <paramref name="distance"/> metres.</summary>
    public static float WorldSize(float distance, float degrees) =>
        distance <= 0f ? 0f : 2f * distance * MathF.Tan(degrees * MathF.PI / 360f);
}

/// <summary>
/// Visibility of one speaking icon: fades in while the player talks, stays for
/// <see cref="HoldSeconds"/> after the last sound (the game's name tag waits one second too), then
/// fades out, so the icon does not flicker between words. Call <see cref="Update"/> once per frame.
/// </summary>
public sealed class VoiceIndicatorFade
{
    public float HoldSeconds = 1f;
    public float FadeSeconds = 0.25f;

    private float hold;

    public float Alpha { get; private set; }

    public bool Visible => Alpha > 0f;

    public float Update(bool speaking, float dt)
    {
        if (speaking)
        {
            hold = HoldSeconds;
            Alpha = Step(Alpha, dt);
            return Alpha;
        }
        if (hold > dt)
        {
            hold -= dt;
            return Alpha;
        }
        float fadeTime = dt - hold;
        hold = 0f;
        Alpha = Step(Alpha, -fadeTime);
        return Alpha;
    }

    public void Reset()
    {
        hold = 0f;
        Alpha = 0f;
    }

    private float Step(float alpha, float signedTime)
    {
        if (FadeSeconds <= 0f)
            return signedTime > 0f ? 1f
                : signedTime < 0f ? 0f
                : alpha;
        float next = alpha + signedTime / FadeSeconds;
        return next < 0f ? 0f
            : next > 1f ? 1f
            : next;
    }
}
