namespace PeakXR.Core.Tests;

public class ComfortPolicyTests
{
    [Fact]
    public void EveryEventHasAComfortableDefault()
    {
        foreach (ComfortEvent e in Enum.GetValues<ComfortEvent>())
        {
            var t = ComfortPolicy.Default(e);
            Assert.True(Enum.IsDefined(t));
        }
        Assert.Equal(ComfortTreatment.FadeHoldFade, ComfortPolicy.Default(ComfortEvent.Teleport));
        Assert.Equal(ComfortTreatment.HapticPulse, ComfortPolicy.Default(ComfortEvent.Shake));
        Assert.Equal(ComfortTreatment.ThirdPerson, ComfortPolicy.Default(ComfortEvent.Launch));
        Assert.Equal(ComfortTreatment.VignettePulse, ComfortPolicy.Default(ComfortEvent.Knockback));
        Assert.Equal(ComfortTreatment.VignettePulse, ComfortPolicy.Default(ComfortEvent.WindPush));
    }

    [Fact]
    public void None_DoesNothing()
    {
        var r = ComfortPolicy.Resolve(ComfortEvent.Knockback, ComfortTreatment.None, 1f, 1f, true);
        Assert.False(r.Any);
    }

    [Fact]
    public void ZeroIntensity_DisablesEverything()
    {
        foreach (ComfortEvent e in Enum.GetValues<ComfortEvent>())
        foreach (ComfortTreatment t in Enum.GetValues<ComfortTreatment>())
            Assert.False(ComfortPolicy.Resolve(e, t, 1f, 0f, true).Any);
    }

    [Fact]
    public void Vignette_ScalesWithMagnitudeAndIntensity()
    {
        var full = ComfortPolicy.Resolve(
            ComfortEvent.Knockback,
            ComfortTreatment.VignettePulse,
            1f,
            1f,
            false
        );
        var half = ComfortPolicy.Resolve(
            ComfortEvent.Knockback,
            ComfortTreatment.VignettePulse,
            1f,
            0.5f,
            false
        );
        var small = ComfortPolicy.Resolve(
            ComfortEvent.Knockback,
            ComfortTreatment.VignettePulse,
            0.2f,
            1f,
            false
        );
        Assert.True(full.Vignette > half.Vignette);
        Assert.True(full.Vignette > small.Vignette);
        Assert.True(small.Vignette > 0f);
        Assert.True(full.Vignette <= 1f);
        Assert.True(full.VignetteSeconds > 0f);
        Assert.Equal(0f, full.Haptic);
    }

    [Fact]
    public void PhysicalEvents_AlsoRumbleWhenHapticsOn()
    {
        var on = ComfortPolicy.Resolve(
            ComfortEvent.Knockback,
            ComfortTreatment.VignettePulse,
            1f,
            1f,
            true
        );
        Assert.True(on.Haptic > 0f);
        var flash = ComfortPolicy.Resolve(
            ComfortEvent.StatusFlash,
            ComfortTreatment.VignettePulse,
            1f,
            1f,
            true
        );
        Assert.Equal(0f, flash.Haptic);
    }

    [Fact]
    public void Haptic_RumblesEvenWithAccompanimentOff()
    {
        var r = ComfortPolicy.Resolve(
            ComfortEvent.Shake,
            ComfortTreatment.HapticPulse,
            0.5f,
            1f,
            false
        );
        Assert.True(r.Haptic > 0f);
        Assert.True(r.HapticSeconds > 0f);
        Assert.Equal(0f, r.Vignette);
        Assert.False(r.Fade);
    }

    [Fact]
    public void FadeAndThirdPerson_AreBinary()
    {
        var f = ComfortPolicy.Resolve(
            ComfortEvent.Teleport,
            ComfortTreatment.FadeHoldFade,
            0.1f,
            0.3f,
            false
        );
        Assert.True(f.Fade);
        var d = ComfortPolicy.Resolve(
            ComfortEvent.Launch,
            ComfortTreatment.ThirdPerson,
            0.1f,
            0.3f,
            false
        );
        Assert.True(d.DetachSeconds > 0f);
    }

    [Fact]
    public void NaNMagnitude_IsTreatedAsFull()
    {
        var r = ComfortPolicy.Resolve(
            ComfortEvent.Knockback,
            ComfortTreatment.VignettePulse,
            float.NaN,
            1f,
            false
        );
        Assert.True(r.Vignette > 0f && r.Vignette <= 1f);
    }
}

public class ComfortPulsesTests
{
    const float Dt = 1f / 90f;

    static void Run(ComfortPulses p, float seconds)
    {
        for (float t = 0f; t < seconds - 1e-6f; t += Dt)
            p.Update(Dt);
    }

    [Fact]
    public void Idle_IsClear()
    {
        var p = new ComfortPulses();
        p.Update(Dt);
        Assert.Equal(0f, p.Vignette);
        Assert.Equal(0f, p.Fade);
        Assert.False(p.Detached);
    }

    [Fact]
    public void VignettePulse_HoldsThenDecays()
    {
        var p = new ComfortPulses();
        p.Add(new ComfortResponse { Vignette = 0.6f, VignetteSeconds = 0.5f });
        p.Update(Dt);
        TestUtil.Near(0.6f, p.Vignette);
        Run(p, 0.4f);
        TestUtil.Near(0.6f, p.Vignette);
        Run(p, 0.5f);
        Assert.Equal(0f, p.Vignette);
    }

    [Fact]
    public void Vignette_StrongestPulseWins()
    {
        var p = new ComfortPulses();
        p.Add(new ComfortResponse { Vignette = 0.3f, VignetteSeconds = 1f });
        p.Add(new ComfortResponse { Vignette = 0.7f, VignetteSeconds = 0.2f });
        p.Update(Dt);
        TestUtil.Near(0.7f, p.Vignette);
        Run(p, 0.5f);
        Assert.True(p.Vignette > 0f && p.Vignette <= 0.3f + 1e-4f);
    }

    [Fact]
    public void RenewedPulse_DoesNotCrowdOutOthers()
    {
        var p = new ComfortPulses();
        for (int i = 0; i < 20; i++)
        {
            p.Add(new ComfortResponse { Vignette = 0.5f, VignetteSeconds = 0.2f });
            p.Update(Dt);
        }
        p.Add(new ComfortResponse { Vignette = 0.3f, VignetteSeconds = 1f });
        p.Update(Dt);
        Run(p, 0.6f);
        TestUtil.Near(0.3f, p.Vignette);
    }

    [Fact]
    public void Fade_SnapsBlackHoldsThenFadesIn()
    {
        var p = new ComfortPulses { FadeInSeconds = 0.3f };
        p.Add(new ComfortResponse { Fade = true, FadeHoldSeconds = 0.2f });
        p.Update(Dt);
        Assert.True(p.SnapBlack);
        Assert.Equal(1f, p.Fade);
        p.Update(Dt);
        Assert.False(p.SnapBlack);
        Run(p, 0.15f);
        Assert.Equal(1f, p.Fade);
        Run(p, 0.15f);
        Assert.True(p.Fade < 1f && p.Fade > 0f);
        Run(p, 0.4f);
        Assert.Equal(0f, p.Fade);
    }

    [Fact]
    public void Fade_RepeatedRequestsExtendTheHoldWithoutBlinking()
    {
        var p = new ComfortPulses();
        p.Add(new ComfortResponse { Fade = true, FadeHoldSeconds = 0.2f });
        p.Update(Dt);
        Run(p, 0.1f);
        p.Add(new ComfortResponse { Fade = true, FadeHoldSeconds = 0.2f });
        p.Update(Dt);
        Assert.Equal(1f, p.Fade);
        Run(p, 0.15f);
        Assert.Equal(1f, p.Fade);
    }

    [Fact]
    public void Detach_LastsTheRequestedTimeAndStaysWhileRenewed()
    {
        var p = new ComfortPulses();
        p.Add(new ComfortResponse { DetachSeconds = 0.5f });
        p.Update(Dt);
        Assert.True(p.Detached);
        for (int i = 0; i < 90; i++)
        {
            p.Add(new ComfortResponse { DetachSeconds = 0.5f });
            p.Update(Dt);
        }
        Assert.True(p.Detached);
        Run(p, 0.6f);
        Assert.False(p.Detached);
    }

    [Fact]
    public void Haptic_IsReportedOnceThenCleared()
    {
        var p = new ComfortPulses();
        p.Add(new ComfortResponse { Haptic = 0.4f, HapticSeconds = 0.1f });
        p.Add(new ComfortResponse { Haptic = 0.8f, HapticSeconds = 0.05f });
        p.Update(Dt);
        TestUtil.Near(0.8f, p.HapticAmplitude);
        TestUtil.Near(0.1f, p.HapticSeconds);
        p.Update(Dt);
        Assert.Equal(0f, p.HapticAmplitude);
    }

    [Fact]
    public void Reset_ClearsEverything()
    {
        var p = new ComfortPulses();
        p.Add(
            new ComfortResponse
            {
                Vignette = 1f,
                VignetteSeconds = 1f,
                Fade = true,
                FadeHoldSeconds = 1f,
                DetachSeconds = 1f,
            }
        );
        p.Update(Dt);
        p.Reset();
        p.Update(Dt);
        Assert.Equal(0f, p.Vignette);
        Assert.Equal(0f, p.Fade);
        Assert.False(p.Detached);
    }
}

public class CameraPolicyEventDetachTests
{
    [Theory]
    [InlineData(CameraState.Body, TakeoverView.ThirdPerson, ViewKind.ThirdPerson)]
    [InlineData(CameraState.Body, TakeoverView.FirstPerson, ViewKind.ThirdPerson)]
    [InlineData(CameraState.Ragdoll, TakeoverView.FirstPerson, ViewKind.ThirdPerson)]
    [InlineData(CameraState.Spectate, TakeoverView.FirstPerson, ViewKind.FollowFirstPerson)]
    [InlineData(CameraState.Cutscene, TakeoverView.ThirdPerson, ViewKind.Static)]
    public void EventDetach_OnlyTakesTheOwnBodyViews(
        CameraState state,
        TakeoverView pref,
        ViewKind expected
    ) => Assert.Equal(expected, CameraPolicy.View(state, pref, false, eventDetach: true));
}

public class LaunchTrackerTests
{
    const float Dt = 1f / 90f;

    [Fact]
    public void Start_StaysAirborneUntilLandedForTheSettleTime()
    {
        var t = new LaunchTracker();
        t.Start();
        Assert.True(t.Update(grounded: true, upSpeed: 0f, Dt));
        for (int i = 0; i < 45; i++)
            Assert.True(t.Update(false, 5f, Dt));
        // Landed: holds a little, then ends.
        Assert.True(t.Update(true, 0f, Dt));
        bool active = true;
        for (int i = 0; i < 45 && active; i++)
            active = t.Update(true, 0f, Dt);
        Assert.False(active);
    }

    [Fact]
    public void GroundedRightAfterStart_StillShowsTheMinimum()
    {
        var t = new LaunchTracker { MinSeconds = 0.4f };
        t.Start();
        int frames = 0;
        while (t.Update(true, 0f, Dt) && frames < 1000)
            frames++;
        Assert.InRange(frames * Dt, 0.35f, 0.7f);
    }

    [Fact]
    public void EndsAtTheCap()
    {
        var t = new LaunchTracker { MaxSeconds = 2f };
        t.Start();
        int frames = 0;
        while (t.Update(false, 1f, Dt) && frames < 1000)
            frames++;
        Assert.InRange(frames * Dt, 1.9f, 2.1f);
    }

    [Fact]
    public void Detect_SuddenUpwardSpeedIsALaunch()
    {
        var t = new LaunchTracker { DetectSpeed = 8f };
        Assert.False(t.Update(true, 0f, Dt));
        Assert.True(t.Update(false, 12f, Dt));
    }

    [Fact]
    public void Detect_JumpsAndSteadyRisesAreNot()
    {
        var t = new LaunchTracker { DetectSpeed = 8f };
        t.Update(true, 0f, Dt);
        Assert.False(t.Update(false, 5f, Dt));
        // Slow build-up (rope, wind): never a sudden jump.
        for (float v = 5f; v < 20f; v += 0.05f)
            Assert.False(t.Update(false, v, Dt));
    }

    [Fact]
    public void Cancel_EndsAtOnce()
    {
        var t = new LaunchTracker();
        t.Start();
        t.Update(false, 5f, Dt);
        t.Cancel();
        Assert.False(t.Update(false, 5f, Dt));
    }
}
