using System;

namespace PeakXR.Core;

/// <summary>
/// The scout's standing eye height over the ground, measured the way the floor is judged (eyes to the ground under
/// them) for the Auto world scale (<see cref="WorldScale.Auto"/>). One frame is not a stand: run 10 took its first
/// upright frame and logged 1.56 m at the airport and 1.42 m at the beach, where the scout was still getting up from
/// the wake-up fall with bent legs (hip 0.14 m lower, eyes the same 0.99 m over it). So frames count only after
/// <see cref="SettleSeconds"/> of an unbroken steady stand, and the measure is the median of the next
/// <see cref="SampleSeconds"/>, kept only when the middle half spreads under <see cref="MaxSpread"/>. One measure per
/// character (<see cref="Reset"/> on a new one). Allocation-free.
/// </summary>
public sealed class StandingEye
{
    public const float SettleSeconds = 1f;
    public const float SampleSeconds = 1f;
    public const int MinSamples = 30;

    /// <summary>Widest interquartile range (m) that counts as standing still.</summary>
    public const float MaxSpread = 0.03f;

    /// <summary>A new measure closer than this (m) to the kept one keeps it: no scale change, no blink.</summary>
    public const float Deadband = 0.02f;

    private readonly float[] samples = new float[1024];
    private int count;
    private float steadySince = -1f;

    public bool Measured { get; private set; }

    /// <summary>The median eye height (m) once <see cref="Measured"/>.</summary>
    public float Value { get; private set; }

    /// <summary>The interquartile range of the samples behind <see cref="Value"/>.</summary>
    public float Spread { get; private set; }

    /// <summary>How many frames <see cref="Value"/> is the median of.</summary>
    public int Count { get; private set; }

    /// <summary>
    /// One frame: <paramref name="steady"/> when the body stands still, upright and uncrouched on flat ground,
    /// <paramref name="height"/> its eyes over the ground under them (NaN breaks the stand). True on the frame the
    /// measure lands.
    /// </summary>
    public bool Update(bool steady, float height, float now)
    {
        if (Measured)
            return false;
        if (!steady || float.IsNaN(height))
        {
            steadySince = -1f;
            count = 0;
            return false;
        }
        if (steadySince < 0f)
            steadySince = now;
        if (now - steadySince < SettleSeconds)
            return false;
        if (count < samples.Length)
            samples[count++] = height;
        if (now - steadySince < SettleSeconds + SampleSeconds || count < MinSamples)
            return false;
        Array.Sort(samples, 0, count);
        float spread = samples[count * 3 / 4] - samples[count / 4];
        if (spread > MaxSpread)
        {
            // Still moving: start the sample second again from here.
            steadySince = now - SettleSeconds;
            count = 0;
            return false;
        }
        int m = count / 2;
        Value = count % 2 == 1 ? samples[m] : 0.5f * (samples[m - 1] + samples[m]);
        Spread = spread;
        Count = count;
        Measured = true;
        return true;
    }

    public void Reset()
    {
        Measured = false;
        steadySince = -1f;
        count = 0;
    }

    /// <summary>The height to use after a new <paramref name="measured"/> one: the <paramref name="kept"/> one (NaN
    /// for none) unless they differ by more than <see cref="Deadband"/>, so the same scout gives the same scale in
    /// every scene.</summary>
    public static float Keep(float kept, float measured) =>
        float.IsNaN(kept) || MathF.Abs(measured - kept) > Deadband ? measured : kept;
}
