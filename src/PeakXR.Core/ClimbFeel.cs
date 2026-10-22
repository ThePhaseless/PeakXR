using System;

namespace PeakXR.Core;

/// <summary>Climb-only sounds the cue set has no word for: the lunge's rush of air and a vine slide's whirr.</summary>
public enum ClimbSound
{
    Whoosh,
    Whirr,
}

/// <summary>How a climb feels in the hand beyond the grab itself.</summary>
public static class ClimbFeel
{
    /// <summary>Fall speed (m/s, down) below which a grab is clean: a step or a hop, not a fall.</summary>
    public const float CleanCatch = 1.5f;

    /// <summary>
    /// A falling catch: the game turns the fall into a slide down the wall (CharacterClimbing.cs:371-374), so the
    /// hold pulse grows into a long, hard ratchet with the fall speed. No pulse (amplitude 0) for a clean grab, which
    /// keeps its surface feel.
    /// </summary>
    public static HapticPulse Catch(float fallSpeed)
    {
        if (fallSpeed < CleanCatch)
            return default;
        float amplitude = MathF.Min(1f, 0.6f + 0.06f * fallSpeed);
        float seconds = MathF.Min(0.5f, 0.08f + 0.04f * fallSpeed);
        return new HapticPulse(0f, amplitude, seconds);
    }
}

/// <summary>
/// A faint, continuous texture under the climbing hands by surface (<c>ClimbModifierSurface</c>): smooth slow ticks
/// on an easy ("ladder") hold, a fast fine grit on a slide-only one, sharp irregular pricks on a hazard, and a
/// shudder whenever the wind makes the hold slippy (<c>CharacterData.slippy</c>, WindChillZone.cs:211). Plain rock
/// stays quiet. One instance per climber; allocation-free.
/// </summary>
public sealed class SurfaceTexture
{
    private float next = float.NegativeInfinity;
    private int count;

    public void Reset()
    {
        next = float.NegativeInfinity;
        count = 0;
    }

    public bool TryTick(HoldKind kind, float slippy, float now, out HapticPulse pulse)
    {
        pulse = default;
        float amplitude,
            seconds,
            interval;
        if (slippy > 0.05f && kind is HoldKind.Normal or HoldKind.Easy)
        {
            float s = MathF.Min(1f, slippy);
            amplitude = 0.1f + 0.2f * s;
            seconds = 0.04f;
            interval = 0.1f;
        }
        else
        {
            switch (kind)
            {
                case HoldKind.Easy:
                    amplitude = 0.05f;
                    seconds = 0.03f;
                    interval = 0.6f;
                    break;
                case HoldKind.Slippery:
                    amplitude = 0.12f;
                    seconds = 0.015f;
                    interval = 0.06f;
                    break;
                case HoldKind.Hazard:
                    amplitude = 0.25f;
                    seconds = 0.01f;
                    // Uneven spacing reads as prickles, not a beat.
                    interval = (count & 1) == 0 ? 0.12f : 0.24f;
                    break;
                default:
                    return false;
            }
        }
        if (now < next)
            return false;
        next = now + interval;
        count++;
        pulse = new HapticPulse(now, amplitude, seconds);
        return true;
    }
}

/// <summary>Synthesised <see cref="ClimbSound"/> clips, as quiet as the cue tones (<see cref="CueTones.Peak"/>).</summary>
public static class ClimbTones
{
    public static float[] Make(ClimbSound sound, int sampleRate) =>
        sound == ClimbSound.Whirr ? Whirr(sampleRate) : Whoosh(sampleRate);

    /// <summary>0.28 s of noise whose brightness rises and falls under a raised-cosine envelope: air rushing past.</summary>
    private static float[] Whoosh(int rate)
    {
        int n = (int)(rate * 0.28f);
        var samples = new float[n];
        uint seed = 0x2545F491u;
        float low = 0f;
        for (int i = 0; i < n; i++)
        {
            float u = (float)i / (n - 1);
            float env = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * u);
            // One-pole low-pass whose cutoff sweeps up then down: the rush passes by.
            float cutoff = 0.02f + 0.25f * MathF.Sin(MathF.PI * u);
            seed = seed * 1664525u + 1013904223u;
            float white = (seed >> 8) / 8388608f - 1f;
            low += cutoff * (white - low);
            samples[i] = CueTones.Peak * env * Clamp(low * 2.5f);
        }
        return samples;
    }

    /// <summary>0.16 s grain of a low 110 Hz buzz fluttering at 40 Hz: a line running fast through the hands.</summary>
    private static float[] Whirr(int rate)
    {
        int n = (int)(rate * 0.16f);
        var samples = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            float env = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * i / (n - 1));
            float flutter = 0.6f + 0.4f * MathF.Sin(2f * MathF.PI * 40f * t);
            float s =
                MathF.Sin(2f * MathF.PI * 110f * t) + 0.35f * MathF.Sin(2f * MathF.PI * 220f * t);
            samples[i] = CueTones.Peak * env * flutter * s / 1.35f;
        }
        return samples;
    }

    private static float Clamp(float v) =>
        v > 1f ? 1f
        : v < -1f ? -1f
        : v;
}
