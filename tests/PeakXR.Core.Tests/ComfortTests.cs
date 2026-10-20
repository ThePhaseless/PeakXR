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
    public void Comfortable_IsSnapOnlyWithSmoothing()
    {
        var p = ComfortPresets.Get(ComfortPreset.Comfortable);
        Assert.Equal(TurnMode.Snap, p.Turn);
        Assert.True(p.Vignette);
        Assert.True(p.MoveSmoothing > 0f);
    }

    [Fact]
    public void Moderate_MatchesDesignDefaults()
    {
        var p = ComfortPresets.Get(ComfortPreset.Moderate);
        Assert.Equal(TurnMode.Snap, p.Turn);
        Assert.Equal(30f, p.SnapAngle);
        Assert.True(p.Vignette);
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
