using System;

namespace PeakXR.Core;

/// <summary>Frame-rate independent exponential smoothing, one rule for every follower: x += (target - x) * Factor.</summary>
public static class ExpSmooth
{
    /// <summary>
    /// Share of the remaining gap closed over <paramref name="dt"/> seconds at <paramref name="rate"/> (1/s):
    /// 1 - e^(-rate * dt). A rate of 0 or below snaps (1), so a bad setting never freezes or runs away; pass
    /// 1 / tau for a time constant (tau 0 is then an infinite rate and snaps too). No time passed holds (0).
    /// </summary>
    public static float Factor(float rate, float dt) =>
        rate <= 0f ? 1f
        : dt <= 0f ? 0f
        : 1f - MathF.Exp(-rate * dt);
}
