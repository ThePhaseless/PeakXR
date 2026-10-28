namespace PeakXR.Core.Tests;

public class FrameStatsTests
{
    [Fact]
    public void Empty_SummaryIsEmpty()
    {
        var r = new FrameRing(8);
        var s = r.Summarize(11f);
        Assert.True(s.Empty);
        Assert.Equal(0, s.Count);
    }

    [Fact]
    public void Percentiles_NearestRank()
    {
        var r = new FrameRing(128);
        for (int i = 100; i >= 1; i--)
            r.Add(i);
        var s = r.Summarize(0f);
        Assert.Equal(100, s.Count);
        Assert.Equal(50f, s.P50);
        Assert.Equal(95f, s.P95);
        Assert.Equal(99f, s.P99);
        Assert.Equal(0, s.Over);
    }

    [Fact]
    public void Ring_KeepsOnlyTheLastCapacitySamples()
    {
        var r = new FrameRing(4);
        r.Add(100f);
        r.Add(100f);
        for (int i = 0; i < 4; i++)
            r.Add(10f);
        var s = r.Summarize(0f);
        Assert.Equal(4, s.Count);
        Assert.Equal(10f, s.P99);
    }

    [Fact]
    public void Over_CountsSamplesPastTheLimit()
    {
        var r = new FrameRing(16);
        r.Add(11f);
        r.Add(11f);
        r.Add(22f);
        r.Add(16.7f);
        Assert.Equal(2, r.Summarize(11.1f * FrameBudget.DroppedFactor).Over);
    }

    [Fact]
    public void Add_IgnoresInvalidSamples()
    {
        var r = new FrameRing(8);
        r.Add(0f);
        r.Add(-1f);
        r.Add(float.NaN);
        r.Add(float.PositiveInfinity);
        Assert.Equal(0, r.Count);
    }

    [Fact]
    public void Summarize_DoesNotDisturbTheRing()
    {
        var r = new FrameRing(8);
        r.Add(3f);
        r.Add(1f);
        r.Add(2f);
        r.Summarize(0f);
        r.Add(10f);
        var s = r.Summarize(0f);
        Assert.Equal(4, s.Count);
        Assert.Equal(10f, s.P99);
        Assert.Equal(2f, s.P50);
    }

    [Fact]
    public void Clear_Empties()
    {
        var r = new FrameRing(8);
        r.Add(5f);
        r.Clear();
        Assert.True(r.Summarize(0f).Empty);
    }

    [Fact]
    public void Budget_FromRefreshRate()
    {
        TestUtil.Near(11.111f, FrameBudget.BudgetMs(90f), 1e-3f);
        Assert.Equal(0f, FrameBudget.BudgetMs(0f));
        Assert.Equal(0f, FrameBudget.BudgetMs(float.NaN));
    }

    static FrameSummary Steady(float ms, int n, float hz)
    {
        var r = new FrameRing(256);
        for (int i = 0; i < n; i++)
            r.Add(ms);
        return r.Summarize(FrameBudget.BudgetMs(hz) * FrameBudget.DroppedFactor);
    }

    [Fact]
    public void Judge_SkipsWithoutRefreshOrSamples()
    {
        Assert.Equal(FrameVerdict.Skip, FrameBudget.Judge(Steady(11f, 200, 90f), 0f));
        Assert.Equal(FrameVerdict.Skip, FrameBudget.Judge(Steady(11f, 5, 90f), 90f));
    }

    [Fact]
    public void Judge_PassesWithinMargin_FailsPastIt()
    {
        Assert.Equal(FrameVerdict.Pass, FrameBudget.Judge(Steady(11.5f, 200, 90f), 90f));
        Assert.Equal(FrameVerdict.Fail, FrameBudget.Judge(Steady(13f, 200, 90f), 90f));
    }

    [Fact]
    public void Judge_FailsWhenOneFrameInTenIsShownTwice()
    {
        var r = new FrameRing(256);
        for (int i = 0; i < 200; i++)
            r.Add(i % 10 == 0 ? 22.2f : 11.1f);
        var s = r.Summarize(16.7f);
        Assert.Equal(20, s.Over);
        Assert.Equal(FrameVerdict.Fail, FrameBudget.Judge(s, 90f));
    }
}

public class AdaptiveScaleTests
{
    [Fact]
    public void ATargetChange_StartsTheCountAgain()
    {
        // FeedAdaptive switches between the GPU target and the frame-interval one window by window.
        var a = new AdaptiveScale();
        a.Feed(1.2f);
        a.Target = 1.25f;
        Assert.False(a.Feed(1.3f));
        Assert.Equal(1f, a.Scale);
        Assert.True(a.Feed(1.3f));
    }

    [Fact]
    public void FarOverBudget_JumpsToTheScaleThatFits()
    {
        // Run 57: 38 ms GPU frames on a 72 Hz (13.9 ms) headset, load 2.8. One 0.1 step per fade would take the
        // whole run; the pixel count says 0.55 fits.
        var a = new AdaptiveScale { Min = 0.5f };
        a.Feed(2.8f);
        Assert.True(a.Feed(2.8f));
        TestUtil.Near(0.55f, a.Scale, 1e-5f);

        var floor = new AdaptiveScale { Min = 0.7f };
        floor.Feed(2.8f);
        floor.Feed(2.8f);
        TestUtil.Near(0.7f, floor.Scale, 1e-5f);
    }

    [Fact]
    public void OneHotWindow_Holds_TwoStepDown()
    {
        var a = new AdaptiveScale();
        Assert.False(a.Feed(1.2f));
        Assert.Equal(1f, a.Scale);
        Assert.True(a.Feed(1.2f));
        TestUtil.Near(0.9f, a.Scale, 1e-5f);
    }

    [Fact]
    public void Cooldown_AfterAChange()
    {
        var a = new AdaptiveScale { CooldownWindows = 2 };
        a.Feed(1.2f);
        a.Feed(1.2f);
        Assert.False(a.Feed(1.2f));
        Assert.False(a.Feed(1.2f));
        Assert.False(a.Feed(1.2f));
        Assert.True(a.Feed(1.2f));
        TestUtil.Near(0.8f, a.Scale, 1e-5f);
    }

    [Fact]
    public void ClampsAtMin()
    {
        var a = new AdaptiveScale { Min = 0.7f, CooldownWindows = 0 };
        for (int i = 0; i < 50; i++)
            a.Feed(2f);
        TestUtil.Near(0.7f, a.Scale, 1e-5f);
    }

    [Fact]
    public void StepsUpOnlyAfterManyCoolWindows()
    {
        var a = new AdaptiveScale { CooldownWindows = 0 };
        a.Reset(0.7f);
        for (int i = 0; i < a.UpWindows - 1; i++)
            Assert.False(a.Feed(0.3f));
        Assert.True(a.Feed(0.3f));
        TestUtil.Near(0.75f, a.Scale, 1e-5f);
    }

    [Fact]
    public void FrameIntervalOnly_NeverStepsUp()
    {
        var a = new AdaptiveScale { CooldownWindows = 0 };
        a.Reset(0.7f);
        for (int i = 0; i < 100; i++)
            a.Feed(0.3f, canRaise: false);
        TestUtil.Near(0.7f, a.Scale, 1e-5f);
    }

    [Fact]
    public void LoadAtTheBoundary_NeverMoves()
    {
        var a = new AdaptiveScale();
        for (int i = 0; i < 500; i++)
            Assert.False(a.Feed(i % 2 == 0 ? a.Target + 0.02f : a.Target - 0.02f));
        Assert.Equal(1f, a.Scale);
    }

    [Fact]
    public void InvalidLoad_Holds()
    {
        var a = new AdaptiveScale();
        a.Feed(1.2f);
        Assert.False(a.Feed(float.NaN));
        Assert.False(a.Feed(0f));
        // The hot window before the gap still counts: no timing is not a cool window.
        Assert.True(a.Feed(1.2f));
    }

    /// <summary>A GPU-bound scene whose load scales with the pixel count settles and never pumps, for every target
    /// and minimum the settings allow.</summary>
    [Theory]
    [InlineData(0.6f, 0.5f)]
    [InlineData(0.85f, 0.7f)]
    [InlineData(0.95f, 0.5f)]
    [InlineData(0.95f, 1f)]
    public void PixelBoundScene_SettlesWithoutPumping(float target, float min)
    {
        for (float full = 0.4f; full <= 3f; full += 0.05f)
        {
            var a = new AdaptiveScale { Target = target, Min = min };
            int changes = 0;
            for (int w = 0; w < 400; w++)
            {
                bool changed = a.Feed(full * a.Scale * a.Scale);
                if (w >= 200 && changed)
                    changes++;
            }
            Assert.Equal(0, changes);
            float settled = full * a.Scale * a.Scale;
            Assert.True(
                settled <= target || a.Scale <= min + 1e-5f,
                $"full {full}: load {settled}"
            );
        }
    }
}

public class RefreshTrackerTests
{
    [Fact]
    public void JitterAroundTheSessionRate_KeepsItExactly()
    {
        // Run 59: the display reported 72 Hz give or take a hair every frame, and each new value was a "change".
        var t = new RefreshTracker();
        t.Update(72.0003f, 0f, 0f);
        Assert.Equal(72.0003f, t.Update(71.9997f, 0f, 0f));
        Assert.Equal(72.0003f, t.Update(72.0004f, 0f, 0f));
        Assert.Equal(RefreshSource.Display, t.Source);
    }

    [Fact]
    public void HeadsetRate_IsTrusted()
    {
        var t = new RefreshTracker();
        Assert.Equal(90f, t.Update(90f, 0f, 60f));
        Assert.Equal(RefreshSource.Display, t.Source);
        Assert.True(t.Trusted);
        Assert.Equal(72f, t.Update(24f, 72f, 60f));
        Assert.Equal(RefreshSource.Device, t.Source);
    }

    [Fact]
    public void ThrottledReadings_KeepTheSessionRate()
    {
        // Run 57: a 72 Hz ALVR headset read as 72, 36, 24 and 18 Hz.
        var t = new RefreshTracker();
        t.Update(72f, 0f, 144f);
        foreach (float hz in new[] { 36f, 24f, 18f, 0f })
        {
            Assert.Equal(72f, t.Update(hz, 0f, 144f));
            Assert.Equal(RefreshSource.LastGood, t.Source);
            Assert.True(t.Trusted);
        }
    }

    [Fact]
    public void A120HzHeadsetThrottledTo60_KeepsItsBudget()
    {
        var t = new RefreshTracker();
        t.Update(120f, 0f, 60f);
        Assert.Equal(120f, t.Update(60f, 0f, 60f));
        Assert.Equal(120f, t.Update(40f, 0f, 60f));
    }

    [Fact]
    public void ARateChangeThatIsNoFraction_IsTaken()
    {
        // The player switched the headset from 120 to 90 Hz: 120/90 is not a whole number.
        var t = new RefreshTracker();
        t.Update(120f, 0f, 60f);
        Assert.Equal(90f, t.Update(90f, 0f, 60f));
        Assert.Equal(RefreshSource.Display, t.Source);
        Assert.Equal(90f, t.Update(45f, 0f, 60f));
    }

    [Fact]
    public void ThrottledFromTheStart_FallsBackToTheMonitor_Untrusted()
    {
        var t = new RefreshTracker();
        Assert.Equal(144f, t.Update(36f, 36f, 144f));
        Assert.Equal(RefreshSource.Screen, t.Source);
        Assert.False(t.Trusted);
        // A real reading later replaces it.
        Assert.Equal(72f, t.Update(72f, 0f, 144f));
        Assert.True(t.Trusted);
    }

    [Fact]
    public void NothingSane_IsUnknown()
    {
        var t = new RefreshTracker();
        Assert.Equal(0f, t.Update(500f, float.NaN, 30f));
        Assert.Equal(RefreshSource.None, t.Source);
        Assert.False(t.Trusted);
    }

    [Fact]
    public void Reset_ForgetsTheSession()
    {
        var t = new RefreshTracker();
        t.Update(90f, 0f, 60f);
        t.Reset();
        Assert.Equal(0f, t.Update(30f, 0f, 0f));
    }

    [Theory]
    [InlineData(59.9f, false)]
    [InlineData(60f, true)]
    [InlineData(72f, true)]
    [InlineData(240f, true)]
    [InlineData(241f, false)]
    public void Sane_IsAHeadsetRate(float hz, bool sane) =>
        Assert.Equal(sane, FrameBudget.Sane(hz));
}

public class RefreshLogGateTests
{
    [Fact]
    public void LogsTheFirstRate_ThenOnlyRealChanges()
    {
        var g = new RefreshLogGate();
        Assert.True(g.Due(72f, RefreshSource.Display, 0f));
        Assert.False(g.Due(72f, RefreshSource.Display, 0.1f));
        Assert.False(g.Due(72.2f, RefreshSource.Display, 0.2f));
        Assert.True(g.Due(90f, RefreshSource.Display, 0.3f));
    }

    [Fact]
    public void SourceFlipsAtTheSameRate_AtMostEveryTenSeconds()
    {
        var g = new RefreshLogGate();
        g.Due(72f, RefreshSource.Display, 0f);
        Assert.False(g.Due(72f, RefreshSource.LastGood, 1f));
        Assert.False(g.Due(72f, RefreshSource.Display, 2f));
        Assert.True(g.Due(72f, RefreshSource.LastGood, 10.5f));
        Assert.False(g.Due(72f, RefreshSource.Display, 11f));
    }

    [Fact]
    public void ARateChange_LogsAtOnceEvenRightAfterAFlip()
    {
        var g = new RefreshLogGate();
        g.Due(72f, RefreshSource.Display, 0f);
        Assert.True(g.Due(0f, RefreshSource.None, 0.5f));
        Assert.True(g.Due(144f, RefreshSource.Screen, 0.6f));
    }
}

public class FramePacingTests
{
    const float Budget120 = 1000f / 120f;

    [Fact]
    public void FullRate_IsOnRate()
    {
        Assert.Equal(1, FramePacing.Divisor(8.35f, Budget120));
        Assert.Equal(PacingVerdict.OnRate, FramePacing.Judge(8.35f, 6.5f, Budget120));
    }

    [Fact]
    public void HalvedWithTheGpuOverBudget_IsGpuBound()
    {
        // Tester log, 2026-10-24: 120 Hz headset, frames at 16.67 ms, GPU p95 14.0 ms.
        Assert.Equal(2, FramePacing.Divisor(16.67f, Budget120));
        Assert.Equal(PacingVerdict.GpuBound, FramePacing.Judge(16.67f, 14.04f, Budget120));
    }

    [Fact]
    public void HalvedWithTheGpuInsideTheBudget_IsNotTheGpu()
    {
        Assert.Equal(PacingVerdict.NotGpu, FramePacing.Judge(16.67f, 5f, Budget120));
    }

    [Fact]
    public void HalvedWithoutGpuTime_IsUnknownCause()
    {
        Assert.Equal(PacingVerdict.NotGpu, FramePacing.Judge(16.67f, 0f, Budget120));
    }

    [Fact]
    public void NoBudget_JudgesNothing()
    {
        Assert.Equal(0, FramePacing.Divisor(16.67f, 0f));
        Assert.Equal(PacingVerdict.OnRate, FramePacing.Judge(16.67f, 14f, 0f));
    }

    [Theory]
    [InlineData(12.24f, 72f)]
    [InlineData(9.5f, 90f)]
    [InlineData(6.5f, 120f)]
    [InlineData(4f, 144f)]
    [InlineData(20f, 0f)]
    public void FittingRefresh_IsTheFastestCommonRateTheGpuKeepsUpWith(float gpuP50, float hz) =>
        Assert.Equal(hz, FramePacing.FittingRefresh(gpuP50));
}

public class GraphicsAdviceTests
{
    [Fact]
    public void Vulkan_NeedsNoAdvice() =>
        Assert.Null(GraphicsAdvice.For("Vulkan", underWine: true));

    [Fact]
    public void Direct3D11OnWindows_NeedsNoAdvice() =>
        Assert.Null(GraphicsAdvice.For("Direct3D11", underWine: false));

    [Fact]
    public void Direct3D12OnWindows_PointsToDirect3D11()
    {
        var advice = GraphicsAdvice.For("Direct3D12", underWine: false);
        Assert.NotNull(advice);
        Assert.Contains("-force-d3d11", advice);
        Assert.Contains("Launch options", advice);
    }

    [Theory]
    [InlineData("Direct3D12")]
    [InlineData("Direct3D11")]
    public void AnyDirect3DUnderProton_PointsToVulkan(string api)
    {
        var advice = GraphicsAdvice.For(api, underWine: true);
        Assert.NotNull(advice);
        Assert.Contains("-force-vulkan", advice);
    }

    [Fact]
    public void VulkanUnderProtonOnNvidia_PointsToDirect3D11OnX11()
    {
        var advice = GraphicsAdvice.For(
            "Vulkan",
            underWine: true,
            nvidia: true,
            waylandDriver: false
        );
        Assert.NotNull(advice);
        Assert.Contains("-force-d3d11", advice);
        Assert.DoesNotContain("add -force-vulkan", advice);
    }

    [Fact]
    public void Direct3D11UnderProtonOnNvidiaWithX11_NeedsNoAdvice() =>
        Assert.Null(
            GraphicsAdvice.For("Direct3D11", underWine: true, nvidia: true, waylandDriver: false)
        );

    [Theory]
    [InlineData("Direct3D11")]
    [InlineData("Direct3D12")]
    public void Direct3DUnderProtonOnWaylandDriver_PointsToX11(string api)
    {
        var advice = GraphicsAdvice.For(api, underWine: true, nvidia: true, waylandDriver: true);
        Assert.NotNull(advice);
        Assert.Contains("PROTON_ENABLE_WAYLAND=0", advice);
    }

    [Fact]
    public void Direct3D12UnderProtonOnNvidia_PointsToDirect3D11()
    {
        var advice = GraphicsAdvice.For(
            "Direct3D12",
            underWine: true,
            nvidia: true,
            waylandDriver: false
        );
        Assert.NotNull(advice);
        Assert.Contains("-force-d3d11", advice);
    }
}
