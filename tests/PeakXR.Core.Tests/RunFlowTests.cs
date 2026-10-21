namespace PeakXR.Core.Tests;

public class RunFlowFadeTests
{
    static RunFlowFade Make() =>
        new RunFlowFade
        {
            FadeOutSeconds = 0.5f,
            MinHoldSeconds = 0.2f,
            MaxHoldSeconds = 2f,
            FadeInSeconds = 1f,
        };

    [Fact]
    public void StartsIdleAndClear()
    {
        var f = Make();
        Assert.Equal(RunFlowPhase.Idle, f.Phase);
        Assert.False(f.Active);
        Assert.Equal(0f, f.Update(0.1f, busy: true));
        Assert.Equal(RunFlowPhase.Idle, f.Phase);
    }

    [Fact]
    public void Request_RampsOutThenHoldsBlack()
    {
        var f = Make();
        f.Request();
        Assert.Equal(RunFlowPhase.FadingOut, f.Phase);
        TestUtil.Near(0.5f, f.Update(0.25f, busy: false));
        Assert.Equal(1f, f.Update(0.25f, busy: false));
        Assert.Equal(RunFlowPhase.Holding, f.Phase);
    }

    [Fact]
    public void Hold_LastsMinHoldEvenWhenNotBusy()
    {
        var f = Make();
        f.Request(1f);
        f.Update(0.1f, busy: false);
        Assert.Equal(RunFlowPhase.Holding, f.Phase);
        f.Update(0.1f, busy: false);
        Assert.Equal(RunFlowPhase.FadingIn, f.Phase);
    }

    [Fact]
    public void Hold_StaysBlackWhileBusyThenFadesInToIdle()
    {
        var f = Make();
        f.Request(1f);
        for (int i = 0; i < 10; i++)
            Assert.Equal(1f, f.Update(0.1f, busy: true));
        Assert.Equal(RunFlowPhase.Holding, f.Phase);
        f.Update(0.1f, busy: false);
        Assert.Equal(RunFlowPhase.FadingIn, f.Phase);
        TestUtil.Near(0.5f, f.Update(0.5f, busy: false));
        Assert.Equal(0f, f.Update(0.5f, busy: false));
        Assert.Equal(RunFlowPhase.Idle, f.Phase);
        Assert.False(f.TimedOut);
    }

    [Fact]
    public void Hold_TimesOutSoItNeverStaysBlack()
    {
        var f = Make();
        f.Request(1f);
        for (int i = 0; i < 19; i++)
            f.Update(0.1f, busy: true);
        Assert.Equal(RunFlowPhase.Holding, f.Phase);
        f.Update(0.11f, busy: true);
        Assert.Equal(RunFlowPhase.FadingIn, f.Phase);
        Assert.True(f.TimedOut);
        f.Update(1f, busy: true);
        Assert.Equal(RunFlowPhase.Idle, f.Phase);
        Assert.Equal(0f, f.Alpha);
    }

    [Fact]
    public void Request_FullAlphaSkipsFadeOut()
    {
        var f = Make();
        f.Request(1f);
        Assert.Equal(RunFlowPhase.Holding, f.Phase);
        Assert.Equal(1f, f.Alpha);
    }

    [Fact]
    public void Request_ZeroFadeOutGoesStraightToHold()
    {
        var f = Make();
        f.FadeOutSeconds = 0f;
        f.Request();
        Assert.Equal(RunFlowPhase.Holding, f.Phase);
        Assert.Equal(1f, f.Alpha);
    }

    [Fact]
    public void Request_StartsFromGivenAlpha()
    {
        var f = Make();
        f.Request(0.6f);
        Assert.Equal(RunFlowPhase.FadingOut, f.Phase);
        TestUtil.Near(0.8f, f.Update(0.1f, busy: false));
    }

    [Fact]
    public void Request_DuringFadeInReversesWithoutJump()
    {
        var f = Make();
        f.Request(1f);
        f.Update(0.3f, busy: false);
        f.Update(0.4f, busy: false);
        Assert.Equal(RunFlowPhase.FadingIn, f.Phase);
        TestUtil.Near(0.6f, f.Alpha);
        f.Request();
        Assert.Equal(RunFlowPhase.FadingOut, f.Phase);
        TestUtil.Near(0.6f, f.Alpha);
        TestUtil.Near(0.8f, f.Update(0.1f, busy: false));
    }

    [Fact]
    public void Request_DuringHoldRestartsHoldTimer()
    {
        var f = Make();
        f.Request(1f);
        f.Update(1.9f, busy: true);
        f.Request();
        Assert.Equal(0f, f.HoldTime);
        f.Update(1.9f, busy: true);
        Assert.Equal(RunFlowPhase.Holding, f.Phase);
    }

    [Fact]
    public void Release_EndsHoldEarly()
    {
        var f = Make();
        f.Request(1f);
        f.Release();
        Assert.Equal(RunFlowPhase.FadingIn, f.Phase);
        Assert.Equal(1f, f.Alpha);
    }

    [Fact]
    public void Release_WhileIdleStaysIdle()
    {
        var f = Make();
        f.Release();
        Assert.Equal(RunFlowPhase.Idle, f.Phase);
    }

    [Fact]
    public void Reset_ClearsAtOnce()
    {
        var f = Make();
        f.Request(1f);
        f.Reset();
        Assert.Equal(RunFlowPhase.Idle, f.Phase);
        Assert.Equal(0f, f.Alpha);
    }

    [Fact]
    public void ZeroFadeIn_ClearsOnTheFrameTheHoldEnds()
    {
        var f = Make();
        f.FadeInSeconds = 0f;
        f.Request(1f);
        f.Update(0.3f, busy: false);
        Assert.Equal(RunFlowPhase.FadingIn, f.Phase);
        Assert.Equal(0f, f.Update(0.01f, busy: false));
        Assert.Equal(RunFlowPhase.Idle, f.Phase);
    }

    [Fact]
    public void NegativeDeltaIsIgnored()
    {
        var f = Make();
        f.Request();
        Assert.Equal(0f, f.Update(-1f, busy: false));
        Assert.Equal(RunFlowPhase.FadingOut, f.Phase);
    }
}

public class RunFlowRumbleTests
{
    [Fact]
    public void Idle_IsZero()
    {
        var r = new RunFlowRumble();
        Assert.Equal(0f, r.Current);
        Assert.Equal(0f, r.Update(0.1f));
    }

    [Fact]
    public void Add_DecaysLinearlyToZero()
    {
        var r = new RunFlowRumble();
        r.Add(0.8f, 2f);
        TestUtil.Near(0.8f, r.Current);
        TestUtil.Near(0.4f, r.Update(1f));
        Assert.Equal(0f, r.Update(1f));
        Assert.Equal(0f, r.Update(1f));
    }

    [Fact]
    public void Add_StrongerReplacesWeakerIsIgnored()
    {
        var r = new RunFlowRumble();
        r.Add(0.5f, 2f);
        r.Update(1f);
        r.Add(0.2f, 5f);
        TestUtil.Near(0.25f, r.Current);
        r.Add(0.6f, 1f);
        TestUtil.Near(0.6f, r.Current);
        TestUtil.Near(0.3f, r.Update(0.5f));
    }

    [Fact]
    public void Add_ClampsAmplitudeAndIgnoresEmptyPulses()
    {
        var r = new RunFlowRumble();
        r.Add(3f, 1f);
        Assert.Equal(1f, r.Current);
        r.Reset();
        r.Add(0.5f, 0f);
        Assert.Equal(0f, r.Current);
    }

    [Theory]
    [InlineData(6f, 1f, 0.75f)]
    [InlineData(5f, 0.5f, 0.3125f)]
    [InlineData(20f, 1f, 1f)]
    [InlineData(-1f, 1f, 0f)]
    [InlineData(6f, 0f, 0f)]
    public void FromShake_ScalesGameShakeToAmplitude(float amount, float strength, float expected)
    {
        TestUtil.Near(expected, RunFlowRumble.FromShake(amount, strength));
    }
}

public class BeachWakeTests
{
    const float Dt = 0.1f;

    static BeachWake Started()
    {
        var w = new BeachWake();
        w.Start();
        return w;
    }

    [Fact]
    public void IdleUntilStarted()
    {
        var w = new BeachWake();
        Assert.False(w.Update(Dt, lying: true, grounded: true, speed: 0f));
        Assert.Equal(0f, w.Remaining);
    }

    [Fact]
    public void HoldsWhileLyingAndEndsWhenUp()
    {
        var w = Started();
        Assert.True(w.Update(Dt, lying: true, grounded: true, speed: 0f));
        Assert.False(w.Update(Dt, lying: false, grounded: true, speed: 0f));
        Assert.False(w.Active);
    }

    [Fact]
    public void OneBudgetSharedByTheRoomAndTheBlackHold()
    {
        var w = Started();
        // The room holds part of the budget, the black hold may only use what is left.
        for (int i = 0; i < 40; i++)
            w.Update(Dt, lying: true, grounded: true, speed: 1f);
        Assert.Equal(BeachWake.Budget - 4f, w.Remaining, 3);
        float held = 4f;
        while (w.Update(Dt, lying: true, grounded: true, speed: 1f))
            held += Dt;
        Assert.True(held <= BeachWake.Budget + Dt);
        Assert.Equal(0f, w.Remaining);
    }

    [Fact]
    public void BodyRestingUngroundedEndsTheHold()
    {
        // fallSeconds drains at 0.2x while not grounded (Character.cs:1166-1175): a body stuck on a slope.
        var w = Started();
        float t = 0f;
        while (w.Update(Dt, lying: true, grounded: false, speed: 0.05f))
            t += Dt;
        Assert.True(t < BeachWake.SettleSeconds + 2 * Dt);
    }

    [Fact]
    public void RestingOnTheGroundWaitsForTheGame()
    {
        // Grounded, fallSeconds drains at full rate: the body gets up on its own.
        var w = Started();
        for (int i = 0; i < 30; i++)
            Assert.True(w.Update(Dt, lying: true, grounded: true, speed: 0f));
    }

    [Fact]
    public void MovingUngroundedBodyKeepsHolding()
    {
        var w = Started();
        for (int i = 0; i < 30; i++)
            Assert.True(w.Update(Dt, lying: true, grounded: false, speed: 2f));
    }
}
