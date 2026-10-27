using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// The environment as one continuous rumble in the hands: fire by distance to its flames, wind by the force the game
/// pushes with, heat and cold by how fast the game raises those statuses. Each source is a level from 0 to 1 that
/// moves smoothly, given its own texture by <see cref="SmoothNoise"/>, and the sources add up with <see cref="Mix"/>
/// without passing full strength. Pure math for the environment rumble in interact.
/// </summary>
public static class EnvironmentFeel
{
    /// <summary>
    /// Metres outside a flame's surface where its rumble starts. The surface is the flame particles' own bounds,
    /// which reach a little past the visible fire (billboards are wider than their flame), so 0.3 m beyond it is
    /// about a hand's width from the visible flames: the hand is clearly at the fire. The old warm-palm feel started
    /// 1 m from a point inside the fire, while the hand was still well away from it.
    /// </summary>
    public const float FlameOnset = 0.3f;

    /// <summary>Metres inside the flame's surface where the rumble is full: just under the flame texture.</summary>
    public const float FullInset = 0.05f;

    /// <summary>A status rise this fast (status per second) gives half strength. The desert sun adds 0.1/s
    /// (HotSun.cs:8-10) and reads 0.77; a fog sphere's 0.0105/s cold (FogSphere.cs:108) reads 0.26.</summary>
    public const float StatusHalfRate = 0.03f;

    /// <summary>
    /// Rumble of a hand at <paramref name="point"/> by a flame filling the ellipsoid <paramref name="centre"/> ±
    /// <paramref name="extents"/>: 1 within <see cref="FullInset"/> under its surface and anywhere deeper, 0 from
    /// <see cref="FlameOnset"/> outside it, smooth (smoothstep) between. Distance is measured along the line from
    /// the centre, so the side and the top of a tall flame feel alike at the same gap.
    /// </summary>
    public static float FlameStrength(Vector3 point, Vector3 centre, Vector3 extents) =>
        1f - SmoothStep(-FullInset, FlameOnset, FlameGap(point, centre, extents));

    /// <summary>Metres <paramref name="point"/> lies outside the flame ellipsoid's surface, along the line from its
    /// centre; negative inside.</summary>
    public static float FlameGap(Vector3 point, Vector3 centre, Vector3 extents)
    {
        var e = Vector3.Max(extents, new Vector3(0.01f));
        var d = point - centre;
        float k = (d / e).Length();
        if (k < 1e-6f)
            return float.NegativeInfinity;
        float dist = d.Length();
        return dist - dist / k;
    }

    /// <summary>Strength (0 to 1) of a status rising at <paramref name="ratePerSecond"/>: half at
    /// <see cref="StatusHalfRate"/>, nearing 1 for fast rises, 0 for none.</summary>
    public static float StatusStrength(float ratePerSecond) =>
        ratePerSecond > 0f ? ratePerSecond / (ratePerSecond + StatusHalfRate) : 0f;

    /// <summary>
    /// <paramref name="level"/> given a texture: it wanders between (1 - <paramref name="depth"/>) and 1 of the
    /// level, following smooth noise at <paramref name="frequency"/> (Hz) of time <paramref name="t"/>.
    /// </summary>
    public static float Textured(float level, float t, float frequency, float depth, uint seed) =>
        level * (1f - depth + depth * SmoothNoise.Value01(t * frequency, seed));

    /// <summary>Two rumbles felt together: each fills part of what the other leaves, so the sum never passes 1.</summary>
    public static float Mix(float a, float b) => 1f - (1f - Clamp01(a)) * (1f - Clamp01(b));

    /// <summary>Moves <paramref name="current"/> toward <paramref name="target"/> with time constant
    /// <paramref name="tau"/>: continuous, so a source switching on or off fades rather than steps.</summary>
    public static float Ease(float current, float target, float dt, float tau) =>
        tau > 0f ? current + (target - current) * (1f - MathF.Exp(-dt / tau)) : target;

    static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    static float Clamp01(float v) =>
        v < 0f ? 0f
        : v > 1f ? 1f
        : v;
}

/// <summary>
/// The visible flames of a fire as a box: its live flame particles' centres, each widened by <see cref="Fill"/> of its
/// size. A particle renderer's own bounds count every billboard at its full size and grew to 3 x 3.5 x 3 m on a
/// beach campfire (VR run 5), so a hand a metre out read as inside the flames.
/// </summary>
public sealed class FlameVolume
{
    /// <summary>Part of a flame billboard's size its visible fire reaches from the particle centre: the flame
    /// texture fills about the middle half of its quad.</summary>
    public const float Fill = 0.25f;

    Vector3 min,
        max;

    public int Count { get; private set; }

    public Vector3 Centre => (min + max) * 0.5f;

    public Vector3 Extents => (max - min) * 0.5f;

    public void Reset() => Count = 0;

    public void Add(Vector3 position, float size)
    {
        var r = new Vector3(MathF.Max(size, 0f) * Fill);
        if (Count == 0)
        {
            min = position - r;
            max = position + r;
        }
        else
        {
            min = Vector3.Min(min, position - r);
            max = Vector3.Max(max, position + r);
        }
        Count++;
    }
}

/// <summary>1D value noise: random values on whole numbers, joined by a quintic curve, so it has no steps and no
/// kinks. Allocation-free and deterministic per seed.</summary>
public static class SmoothNoise
{
    /// <summary>Noise in 0..1 at <paramref name="t"/> (about one new value per unit).</summary>
    public static float Value01(float t, uint seed)
    {
        float fl = MathF.Floor(t);
        int i = (int)fl;
        float f = t - fl;
        float u = f * f * f * (f * (f * 6f - 15f) + 10f);
        float a = Hash(i, seed);
        return a + (Hash(i + 1, seed) - a) * u;
    }

    static float Hash(int i, uint seed)
    {
        unchecked
        {
            uint h = ((uint)i * 0x9E3779B1u) ^ (seed * 0x85EBCA77u);
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            h *= 0x297A2D39u;
            h ^= h >> 15;
            return (h & 0xFFFFFFu) / 16777215f;
        }
    }
}

/// <summary>
/// How fast a value rises, per second, read from its per-frame samples. Rises go into a leaky bucket (time constant
/// <see cref="Tau"/>), whose level reads as a rate; a second smoothing makes that rate continuous. So a status the game
/// adds in steps (the desert sun: +0.05 every 0.5 s) reads as an even rate with no jumps, and falls read as none.
/// </summary>
public sealed class RateMeter
{
    public float Tau = 0.8f;

    float last;
    bool started;
    float bucket;
    float rate;

    /// <returns>The smoothed rise rate (per second), never negative.</returns>
    public float Update(float value, float dt)
    {
        if (!started)
        {
            started = true;
            last = value;
            return 0f;
        }
        float rise = value - last;
        last = value;
        if (!(dt > 0f))
            return rate;
        float keep = MathF.Exp(-dt / Tau);
        bucket = bucket * keep + (rise > 0f ? rise : 0f);
        // A steady rise r fills the bucket to r * dt / (1 - keep), so this reads r exactly.
        float raw = bucket * (1f - keep) / dt;
        rate += (raw - rate) * (1f - keep);
        return rate;
    }

    public void Reset()
    {
        started = false;
        bucket = 0f;
        rate = 0f;
    }
}
