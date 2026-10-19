namespace PeakXR.Core.Tests;

public class ComfortVignetteTests
{
    static ComfortVignette Make() => new ComfortVignette
    {
        Enabled = true,
        Strength = 1f,
        LinearMin = 1f,
        LinearMax = 3f,
        AngularMin = 30f,
        AngularMax = 90f,
        AttackRate = 1000f,
        ReleaseRate = 1000f,
    };

    [Fact]
    public void BelowThresholds_IsZero()
    {
        var v = Make();
        Assert.Equal(0f, v.Update(0.5f, 10f, 0.1f));
    }

    [Fact]
    public void RampsLinearlyBetweenMinAndMax()
    {
        var v = Make();
        TestUtil.Near(0.5f, v.Update(2f, 0f, 0.1f), 1e-3f);
        TestUtil.Near(1f, v.Update(10f, 0f, 0.1f), 1e-3f);
    }

    [Fact]
    public void UsesStrongerOfLinearAndAngular()
    {
        var v = Make();
        TestUtil.Near(0.75f, v.Update(1.5f, 75f, 0.1f), 1e-3f);
    }

    [Fact]
    public void StrengthScalesOutput()
    {
        var v = Make();
        v.Strength = 0.4f;
        TestUtil.Near(0.4f, v.Update(10f, 0f, 0.1f), 1e-3f);
        TestUtil.Near(0.6f, v.Aperture, 1e-3f);
    }

    [Fact]
    public void AttackAndRelease_SmoothTowardTarget()
    {
        var v = Make();
        v.AttackRate = 10f;
        v.ReleaseRate = 2f;
        float up = v.Update(10f, 0f, 0.1f);
        TestUtil.Near(1f - MathF.Exp(-1f), up, 1e-3f);
        for (int i = 0; i < 100; i++) v.Update(10f, 0f, 0.1f);
        float down = v.Update(0f, 0f, 0.1f);
        TestUtil.Near(MathF.Exp(-0.2f), down, 1e-3f);
    }

    [Fact]
    public void Disabled_IsZeroImmediately()
    {
        var v = Make();
        v.Update(10f, 0f, 0.1f);
        v.Enabled = false;
        Assert.Equal(0f, v.Update(10f, 500f, 0.1f));
        Assert.Equal(0f, v.Intensity);
        Assert.Equal(1f, v.Aperture);
    }
}
