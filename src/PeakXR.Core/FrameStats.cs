using System;

namespace PeakXR.Core
{
    /// <summary>Percentiles of one window of frame samples (ms). <see cref="Over"/> counts samples past the limit
    /// <see cref="FrameRing.Summarize"/> was given.</summary>
    public readonly struct FrameSummary
    {
        public readonly int Count;
        public readonly float P50;
        public readonly float P95;
        public readonly float P99;
        public readonly int Over;

        public FrameSummary(int count, float p50, float p95, float p99, int over)
        {
            Count = count;
            P50 = p50;
            P95 = p95;
            P99 = p99;
            Over = over;
        }

        public bool Empty => Count == 0;
    }

    /// <summary>
    /// The last N frame samples (ms) in a fixed ring. <see cref="Add"/> is O(1) and allocation-free, so it runs every
    /// frame at 90-144 Hz; <see cref="Summarize"/> sorts a preallocated copy and is for report time only.
    /// </summary>
    public sealed class FrameRing
    {
        readonly float[] _samples;
        readonly float[] _scratch;
        int _next;
        int _count;

        public FrameRing(int capacity = 1024)
        {
            _samples = new float[capacity];
            _scratch = new float[capacity];
        }

        public int Count => _count;

        public void Clear() => _next = _count = 0;

        /// <summary>Adds one sample; zero, negative and non-finite values (no timing this frame) are skipped.</summary>
        public void Add(float ms)
        {
            if (!(ms > 0f) || float.IsInfinity(ms))
                return;
            _samples[_next] = ms;
            _next = (_next + 1) % _samples.Length;
            if (_count < _samples.Length)
                _count++;
        }

        /// <summary>Nearest-rank p50/p95/p99 and the samples above <paramref name="overMs"/> (0 counts none).</summary>
        public FrameSummary Summarize(float overMs)
        {
            if (_count == 0)
                return default;
            // Until the ring wraps, the valid samples are exactly 0.._count-1.
            Array.Copy(_samples, _scratch, _count);
            Array.Sort(_scratch, 0, _count);
            int over = 0;
            if (overMs > 0f)
                for (int i = 0; i < _count; i++)
                    if (_scratch[i] > overMs)
                        over++;
            return new FrameSummary(_count, Rank(0.5f), Rank(0.95f), Rank(0.99f), over);
        }

        float Rank(float q) =>
            _scratch[Math.Clamp((int)MathF.Ceiling(q * _count) - 1, 0, _count - 1)];
    }

    public enum FrameVerdict
    {
        Skip,
        Pass,
        Fail,
    }

    /// <summary>The display's frame budget and the self-test's rule for it.</summary>
    public static class FrameBudget
    {
        /// <summary>A frame interval this many budgets long missed its vsync: the runtime showed the previous frame
        /// again or reprojected it.</summary>
        public const float DroppedFactor = 1.5f;

        /// <summary>The self-test lets the p95 frame interval run 10 % over the budget (pacing jitter) before it fails.</summary>
        public const float SelfTestMargin = 1.1f;

        /// <summary>Fewer samples than this judge nothing.</summary>
        public const int MinSamples = 30;

        /// <summary>Milliseconds per frame at <paramref name="hz"/>; 0 when the runtime reports no refresh rate.</summary>
        public static float BudgetMs(float hz) =>
            hz > 0f && !float.IsInfinity(hz) ? 1000f / hz : 0f;

        /// <summary>Skip without a refresh rate or enough samples; fail when the p95 frame interval is past the
        /// budget times <see cref="SelfTestMargin"/>.</summary>
        public static FrameVerdict Judge(in FrameSummary interval, float hz)
        {
            float budget = BudgetMs(hz);
            if (budget <= 0f || interval.Count < MinSamples)
                return FrameVerdict.Skip;
            return interval.P95 <= budget * SelfTestMargin ? FrameVerdict.Pass : FrameVerdict.Fail;
        }
    }

    /// <summary>
    /// Adaptive render scale for a runtime where only the eye texture itself can shrink (URP <c>renderScale</c> under
    /// OpenXR), which reallocates the eye buffers, so it moves rarely: fed one load per window (the window's p95 GPU
    /// time over the frame budget), it steps down after <see cref="DownWindows"/> hot windows in a row and up only
    /// after <see cref="UpWindows"/> cool ones, then rests for <see cref="CooldownWindows"/>. "Cool" means the load
    /// predicted after the step up, scaled by the pixel count, still sits under the target with a 10 % margin, so a
    /// step up never lands over the target and the scale does not pump.
    /// </summary>
    public sealed class AdaptiveScale
    {
        public float Min = 0.7f;

        /// <summary>Load (GPU time / budget) to stay under.</summary>
        public float Target = 0.85f;

        public float DownStep = 0.1f;
        public float UpStep = 0.05f;
        public int DownWindows = 2;
        public int UpWindows = 6;
        public int CooldownWindows = 2;

        const float UpMargin = 1.1f;

        int _over;
        int _under;
        int _cooldown;

        /// <summary>Multiplier on the player's render scale, in [<see cref="Min"/>, 1].</summary>
        public float Scale { get; private set; } = 1f;

        public void Reset(float scale = 1f)
        {
            Scale = Math.Clamp(scale, Min, 1f);
            _over = _under = _cooldown = 0;
        }

        /// <param name="load">The window's p95 frame time over the budget; non-positive or non-finite (no timing) holds.</param>
        /// <param name="canRaise">False when the load carries no headroom (frame interval only: a paced runtime never
        /// reports a frame shorter than the budget), so the scale only steps down.</param>
        /// <returns>True when <see cref="Scale"/> changed.</returns>
        public bool Feed(float load, bool canRaise = true)
        {
            if (!(load > 0f) || float.IsInfinity(load))
                return false;
            if (_cooldown > 0)
            {
                _cooldown--;
                _over = _under = 0;
                return false;
            }
            if (load > Target)
            {
                _under = 0;
                return ++_over >= DownWindows && Step(-DownStep);
            }
            float up = Math.Min(1f, Scale + UpStep) / Scale;
            if (canRaise && Scale < 1f && load * up * up * UpMargin < Target)
            {
                _over = 0;
                return ++_under >= UpWindows && Step(UpStep);
            }
            _over = _under = 0;
            return false;
        }

        bool Step(float delta)
        {
            _over = _under = 0;
            float next = Math.Clamp(MathF.Round((Scale + delta) * 100f) / 100f, Min, 1f);
            if (next == Scale)
                return false;
            Scale = next;
            _cooldown = CooldownWindows;
            return true;
        }
    }
}
