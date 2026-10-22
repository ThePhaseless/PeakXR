namespace PeakXR.Core.Tests;

public class ComfortTests
{
    [Theory]
    [InlineData(ComfortPreset.Comfortable)]
    [InlineData(ComfortPreset.Moderate)]
    [InlineData(ComfortPreset.Intense)]
    public void Identify_RoundTripsEveryPreset(ComfortPreset preset)
    {
        Assert.Equal(preset, ComfortPresets.Identify(ComfortPresets.Get(preset)));
    }

    [Fact]
    public void Comfortable_IsSnapOnly_WithInstantStartAndStop()
    {
        // Constant speed beats a ramp: acceleration is what makes vection sick (vr-practices-review P1-1).
        var p = ComfortPresets.Get(ComfortPreset.Comfortable);
        Assert.Equal(TurnMode.Snap, p.Turn);
        Assert.True(p.Vignette);
        Assert.Equal(0f, p.MoveSmoothing);
    }

    [Fact]
    public void Moderate_MatchesDesignDefaults()
    {
        var p = ComfortPresets.Get(ComfortPreset.Moderate);
        Assert.Equal(TurnMode.Snap, p.Turn);
        Assert.Equal(30f, p.SnapAngle);
        Assert.True(p.Vignette);
        Assert.Equal(0f, p.MoveSmoothing);
        // Smooth turn, when chosen, inside the 60-75 deg/s band (P2-2).
        Assert.InRange(p.SmoothTurnSpeed, 60f, 75f);
    }

    [Theory]
    [InlineData(ComfortPreset.Moderate, 45f)]
    [InlineData(ComfortPreset.Comfortable, 32f)]
    public void Vignette_ShowsAtWalkingSpeed(ComfortPreset preset, float maxClearDegrees)
    {
        // vr.17 walked 2.69 m/s; a typical headset shows 50-55 degrees to each side (P0-1).
        var v = new ComfortVignette { Strength = ComfortPresets.Get(preset).VignetteStrength };
        for (int i = 0; i < 300; i++)
            v.Update(2.69f, 0f, 1f / 90f);
        Assert.True(
            VignetteShape.InnerAngle(v.Intensity) <= maxClearDegrees,
            $"clear to {VignetteShape.InnerAngle(v.Intensity):0} deg"
        );
    }

    [Fact]
    public void Drive_IsTheSpeedCurve()
    {
        Assert.Equal(0f, ComfortVignette.Drive(0.1f, 0.2f, 2.5f));
        Assert.Equal(1f, ComfortVignette.Drive(2.7f, 0.2f, 2.5f));
        TestUtil.Near(0.5f, ComfortVignette.Drive(1.35f, 0.2f, 2.5f), 1e-3f);
    }

    [Fact]
    public void Vignette_StartsWithAnyArtificialMotion()
    {
        var v = new ComfortVignette { Strength = 0.5f };
        for (int i = 0; i < 90; i++)
            v.Update(0.4f, 0f, 1f / 90f);
        Assert.True(v.Intensity > 0.01f);
    }

    [Theory]
    [InlineData(ComfortPreset.Comfortable)]
    [InlineData(ComfortPreset.Moderate)]
    public void Migration_MovesAnOldPresetsUntouchedValues(ComfortPreset preset)
    {
        var old = ComfortPresets.Old(preset);
        Assert.True(ComfortPresets.FromOld(preset, old, out var updated));
        Assert.Equal(preset, ComfortPresets.Identify(updated));
    }

    [Fact]
    public void Migration_KeepsAPlayersOwnValues()
    {
        var mine = ComfortPresets.Old(ComfortPreset.Comfortable);
        mine.MoveSmoothing = 0.5f;
        Assert.False(ComfortPresets.FromOld(ComfortPreset.Comfortable, mine, out _));
        Assert.False(
            ComfortPresets.FromOld(
                ComfortPreset.Custom,
                ComfortPresets.Old(ComfortPreset.Moderate),
                out _
            )
        );
    }

    [Fact]
    public void Identify_EditedValueIsCustom()
    {
        var p = ComfortPresets.Get(ComfortPreset.Moderate);
        p.SnapAngle = 45f;
        Assert.Equal(ComfortPreset.Custom, ComfortPresets.Identify(p));
    }

    [Fact]
    public void Identify_IgnoresSettingsTheModeDoesNotUse()
    {
        var p = ComfortPresets.Get(ComfortPreset.Moderate);
        p.SmoothTurnSpeed = 300f;
        Assert.Equal(ComfortPreset.Moderate, ComfortPresets.Identify(p));

        var i = ComfortPresets.Get(ComfortPreset.Intense);
        i.SnapAngle = 90f;
        i.VignetteStrength = 0.1f;
        Assert.Equal(ComfortPreset.Intense, ComfortPresets.Identify(i));
    }

    [Fact]
    public void Ramp_ZeroTimePassesThrough()
    {
        var r = new InputRamp { RiseTime = 0f };
        Assert.Equal(new Vector2(0f, 1f), r.Update(new Vector2(0f, 1f), 0.01f));
    }

    [Fact]
    public void Ramp_LimitsAccelerationAndStopsFaster()
    {
        var r = new InputRamp { RiseTime = 0.5f };
        var v = r.Update(new Vector2(0f, 1f), 0.1f);
        TestUtil.Near(0.2f, v.Length());
        for (int i = 0; i < 10; i++)
            v = r.Update(new Vector2(0f, 1f), 0.1f);
        TestUtil.Near(1f, v.Length());
        v = r.Update(Vector2.Zero, 0.1f);
        TestUtil.Near(0.6f, v.Length());
    }

    [Fact]
    public void Ramp_TurnsWithoutSlowingDown()
    {
        var r = new InputRamp { RiseTime = 0.5f };
        for (int i = 0; i < 10; i++)
            r.Update(new Vector2(0f, 1f), 0.1f);
        var v = r.Update(new Vector2(1f, 0f), 0.1f);
        Assert.True(v.Length() > 0.6f);
    }

    [Fact]
    public void VignetteShape_ClosesWithIntensity()
    {
        Assert.True(VignetteShape.InnerAngle(0f) > VignetteShape.InnerAngle(1f));
        Assert.Equal(VignetteShape.OpenAngle, VignetteShape.InnerAngle(0f));
        Assert.Equal(VignetteShape.ClosedAngle, VignetteShape.InnerAngle(1f));
        Assert.Equal(VignetteShape.ClosedAngle, VignetteShape.InnerAngle(2f));
    }
}
