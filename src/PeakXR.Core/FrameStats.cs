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
    /// The last <see cref="DefaultCapacity"/> frame samples (ms) in a fixed ring. <see cref="Add"/> is O(1) and allocation-free, so it runs every
    /// frame at 90-144 Hz; <see cref="Summarize"/> sorts a preallocated copy and is for report time only.
    /// </summary>
    public sealed class FrameRing
    {
        /// <summary>The last 2048 frames: a whole 5 s adaptive window even at 240 Hz; a longer window keeps its
        /// last 2048 (reports name how many of the window's frames that is).</summary>
        public const int DefaultCapacity = 2048;

        readonly float[] _samples;
        readonly float[] _scratch;
        int _next;
        int _count;

        public FrameRing(int capacity = DefaultCapacity)
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

    /// <summary>Where <see cref="RefreshTracker"/> found the refresh rate.</summary>
    public enum RefreshSource
    {
        None,

        /// <summary><c>XRDisplaySubsystem.TryGetDisplayRefreshRate</c>.</summary>
        Display,

        /// <summary><c>XRDevice.refreshRate</c>.</summary>
        Device,

        /// <summary>The session's headset rate, kept while the runtime reports a throttled fraction of it.</summary>
        LastGood,

        /// <summary>The desktop monitor (<c>Screen.currentResolution</c>), when the headset never reported one. Shown in
        /// reports only: it is not the headset's budget, so nothing adapts to it.</summary>
        Screen,
    }

    /// <summary>
    /// The headset refresh rate across a session. SteamVR's OpenXR runtime reports the throttled rate (run 57 over
    /// ALVR: a 72 Hz headset read as 36, 24 and 18 Hz), so a reading outside <see cref="FrameBudget.Sane"/> is no
    /// reading, and a lower sane one that is a whole fraction of the session's rate (120 read as 60 or 40) keeps that
    /// rate. A lower one that is no fraction (120 to 90 Hz) is a real change. Only headset sources are
    /// <see cref="Trusted"/>; the desktop monitor fills in for reports when the headset never gave a rate.
    /// </summary>
    public sealed class RefreshTracker
    {
        float _session;

        public float Hz { get; private set; }
        public RefreshSource Source { get; private set; }

        /// <summary>The rate is the headset's, so budgets, adaptive quality and the self-test may use it.</summary>
        public bool Trusted =>
            Source is RefreshSource.Display or RefreshSource.Device or RefreshSource.LastGood;

        public void Reset()
        {
            _session = Hz = 0f;
            Source = RefreshSource.None;
        }

        /// <summary>One frame's readings (Hz, 0 when a source has none); returns <see cref="Hz"/>.</summary>
        public float Update(float display, float device, float screen)
        {
            var source =
                FrameBudget.Sane(display) ? RefreshSource.Display
                : FrameBudget.Sane(device) ? RefreshSource.Device
                : RefreshSource.None;
            float head = source == RefreshSource.Display ? display : device;
            // A hair off the session rate is the same rate (run 59: 72 Hz jittered every frame).
            if (source != RefreshSource.None && Same(_session, head))
                Set(_session, source);
            else if (
                source != RefreshSource.None
                && !(head < _session && Throttled(_session, head))
            )
            {
                _session = head;
                Set(head, source);
            }
            else if (_session > 0f)
                Set(_session, RefreshSource.LastGood);
            else if (FrameBudget.Sane(screen))
                Set(screen, RefreshSource.Screen);
            else
                Set(0f, RefreshSource.None);
            return Hz;
        }

        /// <summary>Within 1 %: reading jitter, not a new rate.</summary>
        static bool Same(float a, float b) => a > 0f && MathF.Abs(a - b) <= 0.01f * a;

        /// <summary><paramref name="hz"/> is <paramref name="full"/> divided by a whole number of at least 2.</summary>
        public static bool Throttled(float full, float hz)
        {
            float ratio = full / hz;
            float n = MathF.Round(ratio);
            return n >= 2f && MathF.Abs(ratio - n) < 0.05f * n;
        }

        void Set(float hz, RefreshSource source)
        {
            Hz = hz;
            Source = source;
        }
    }

    /// <summary>
    /// When the chosen refresh rate is worth a log line: the first one, a rate that moves by more than
    /// <see cref="RateStep"/>, or a source flip at the same rate at most once per <see cref="FlipSeconds"/> (a throttled
    /// runtime flips Display and LastGood several times a second, run 59).
    /// </summary>
    public sealed class RefreshLogGate
    {
        public const float RateStep = 0.5f;
        public const float FlipSeconds = 10f;

        bool _any;
        float _hz;
        RefreshSource _source;
        float _at;

        public bool Due(float hz, RefreshSource source, float now)
        {
            bool rate = !_any || MathF.Abs(hz - _hz) > RateStep;
            bool flip = source != _source && now - _at >= FlipSeconds;
            if (!rate && !flip)
                return false;
            _any = true;
            _hz = hz;
            _source = source;
            _at = now;
            return true;
        }
    }

    /// <summary>The display's frame budget and the self-test's rule for it.</summary>
    public static class FrameBudget
    {
        /// <summary>Headset refresh rates outside this range are not real: SteamVR's OpenXR runtime reports the
        /// throttled rate (72 Hz as 36, 24 or 18 in run 57 over ALVR), which would set a 41.67 ms budget.</summary>
        public const float MinRefresh = 60f;

        public const float MaxRefresh = 240f;

        public static bool Sane(float hz) => hz >= MinRefresh && hz <= MaxRefresh;

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

        /// <summary>Load over the target by this factor jumps to the fitting scale instead of one step.</summary>
        const float FarOver = 1.5f;

        int _over;
        int _under;
        int _cooldown;
        float _countedTarget = float.NaN;

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
            // A window judged against another target (GPU time or frame interval) starts the count again.
            if (Target != _countedTarget)
            {
                _countedTarget = Target;
                _over = _under = 0;
            }
            if (_cooldown > 0)
            {
                _cooldown--;
                _over = _under = 0;
                return false;
            }
            if (load > Target)
            {
                _under = 0;
                if (++_over < DownWindows)
                    return false;
                // Far over (a step of DownStep cannot fit): go straight to the scale whose pixel count fits the
                // target, rounded down to 0.05, so a slow GPU reaches it at the first fade instead of one step each.
                float fits = Scale * MathF.Sqrt(Target / load);
                float next = Scale - DownStep;
                if (load > FarOver * Target)
                    next = Math.Min(next, MathF.Floor(fits * 20f + 1e-3f) / 20f);
                return Step(next - Scale);
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
