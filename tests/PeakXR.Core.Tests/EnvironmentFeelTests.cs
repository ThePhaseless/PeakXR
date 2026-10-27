namespace PeakXR.Core.Tests;

public class EnvironmentFeelTests
{
    // A campfire flame: 0.3 m wide each way, 0.4 m tall each way from its centre.
    static readonly Vector3 Centre = new(0, 0.4f, 0);
    static readonly Vector3 Extents = new(0.3f, 0.4f, 0.3f);

    static float Side(float x) =>
        EnvironmentFeel.FlameStrength(new Vector3(x, 0.4f, 0), Centre, Extents);

    [Fact]
    public void Flame_FullInsideTheFlames()
    {
        Assert.Equal(1f, Side(0f));
        Assert.Equal(1f, Side(0.2f));
        // Just under the visible surface is already full.
        Assert.Equal(1f, Side(0.3f - EnvironmentFeel.FullInset), 3);
    }

    [Fact]
    public void Flame_NothingFromTheOnsetOut()
    {
        Assert.Equal(0f, Side(0.3f + EnvironmentFeel.FlameOnset));
        Assert.Equal(0f, Side(0.3f + EnvironmentFeel.FlameOnset + 0.5f));
        Assert.Equal(0f, Side(5f));
    }

    [Fact]
    public void Flame_FallsSmoothlyFromTheSurfaceToTheOnset()
    {
        float last = 1f;
        float biggestStep = 0f;
        for (float x = 0.2f; x <= 0.3f + EnvironmentFeel.FlameOnset; x += 0.005f)
        {
            float v = Side(x);
            Assert.True(v <= last + 1e-5f, $"rose at {x}");
            biggestStep = MathF.Max(biggestStep, last - v);
            last = v;
        }
        // 5 mm steps never drop more than a few percent: no edge to feel.
        Assert.True(biggestStep < 0.05f, $"step {biggestStep}");
        Assert.InRange(Side(0.3f), 0.8f, 0.99f);
    }

    [Fact]
    public void Flame_MeasuresFromTheSurface_NotTheCentre()
    {
        // 0.1 m outside the flame's side and 0.1 m over its top feel the same.
        float side = Side(0.4f);
        float top = EnvironmentFeel.FlameStrength(new Vector3(0, 0.9f, 0), Centre, Extents);
        Assert.Equal(side, top, 2);
    }

    [Fact]
    public void Flame_DegenerateBoundsAreSafe()
    {
        Assert.Equal(
            0f,
            EnvironmentFeel.FlameStrength(new Vector3(1, 0, 0), Vector3.Zero, Vector3.Zero)
        );
        Assert.Equal(1f, EnvironmentFeel.FlameStrength(Vector3.Zero, Vector3.Zero, Vector3.Zero));
    }

    [Fact]
    public void Noise_StaysInRange_AndIsSmooth()
    {
        float last = SmoothNoise.Value01(0f, 7);
        float lo = 1f,
            hi = 0f;
        for (int i = 1; i < 4000; i++)
        {
            float t = i * 0.005f;
            float v = SmoothNoise.Value01(t, 7);
            Assert.InRange(v, 0f, 1f);
            // At 5 ms a sample the curve moves only a little: no jumps.
            Assert.True(MathF.Abs(v - last) < 0.02f, $"jump at {t}");
            lo = MathF.Min(lo, v);
            hi = MathF.Max(hi, v);
            last = v;
        }
        // And it does wander: a random variable, not a constant.
        Assert.True(hi - lo > 0.5f);
    }

    [Fact]
    public void Noise_SeedsDiffer_AndRepeat()
    {
        Assert.Equal(SmoothNoise.Value01(3.3f, 1), SmoothNoise.Value01(3.3f, 1));
        Assert.NotEqual(SmoothNoise.Value01(3.3f, 1), SmoothNoise.Value01(3.3f, 2));
    }

    [Fact]
    public void Textured_KeepsTheLevelsPeak_AndNeverGoesNegative()
    {
        for (int i = 0; i < 500; i++)
        {
            float v = EnvironmentFeel.Textured(0.8f, i * 0.01f, 5f, 0.3f, 3);
            Assert.InRange(v, 0.8f * 0.7f - 1e-4f, 0.8f + 1e-4f);
        }
        Assert.Equal(0f, EnvironmentFeel.Textured(0f, 1f, 5f, 0.3f, 3));
    }

    [Fact]
    public void Mix_NeverExceedsFull()
    {
        float m = EnvironmentFeel.Mix(EnvironmentFeel.Mix(0.9f, 0.9f), 1f);
        Assert.Equal(1f, m);
        Assert.Equal(0.5f, EnvironmentFeel.Mix(0f, 0.5f));
        Assert.Equal(0.75f, EnvironmentFeel.Mix(0.5f, 0.5f), 4);
        Assert.InRange(EnvironmentFeel.Mix(0.3f, 0.2f), 0.3f, 0.5f);
    }

    [Fact]
    public void Status_RateCurveRisesAndSaturates()
    {
        Assert.Equal(0f, EnvironmentFeel.StatusStrength(0f));
        Assert.Equal(0f, EnvironmentFeel.StatusStrength(-1f));
        Assert.Equal(0.5f, EnvironmentFeel.StatusStrength(EnvironmentFeel.StatusHalfRate), 4);
        Assert.True(EnvironmentFeel.StatusStrength(1f) > 0.9f);
        Assert.True(EnvironmentFeel.StatusStrength(1f) <= 1f);
    }

    [Fact]
    public void RateMeter_SteadyRiseReadsItsRate()
    {
        var m = new RateMeter();
        float value = 0f,
            rate = 0f;
        for (int i = 0; i < 600; i++)
        {
            value += 0.1f / 60f;
            rate = m.Update(value, 1f / 60f);
        }
        Assert.Equal(0.1f, rate, 3);
    }

    [Fact]
    public void RateMeter_PeriodicStepsReadAsAnEvenRate_WithoutJumps()
    {
        // The desert sun: +0.05 every 0.5 s (HotSun.cs:8-10, :41-45).
        var m = new RateMeter();
        float value = 0f,
            last = 0f,
            lo = 1f,
            hi = 0f,
            jump = 0f;
        for (int i = 0; i < 1200; i++)
        {
            if (i % 30 == 29)
                value += 0.05f;
            float r = m.Update(value, 1f / 60f);
            jump = MathF.Max(jump, MathF.Abs(r - last));
            last = r;
            if (i > 600)
            {
                lo = MathF.Min(lo, r);
                hi = MathF.Max(hi, r);
            }
        }
        Assert.InRange((lo + hi) * 0.5f, 0.08f, 0.12f);
        Assert.True(hi - lo < 0.04f, $"ripple {lo}..{hi}");
        Assert.True(jump < 0.01f, $"jump {jump}");
    }

    [Fact]
    public void RateMeter_FallingOrSteadyValuesReadZero_AndTheFirstReadingIsNoRise()
    {
        var m = new RateMeter();
        Assert.Equal(0f, m.Update(0.7f, 0.02f));
        float r = 0f;
        for (int i = 0; i < 300; i++)
            r = m.Update(0.7f - i * 0.001f, 0.02f);
        Assert.Equal(0f, r, 4);
    }

    [Fact]
    public void Ease_IsContinuous_AndReachesTheTarget()
    {
        float v = 0f;
        float last = 0f;
        for (int i = 0; i < 120; i++)
        {
            v = EnvironmentFeel.Ease(v, 1f, 1f / 60f, 0.2f);
            Assert.True(v - last < 0.1f);
            last = v;
        }
        Assert.True(v > 0.99f);
    }
}

public class FlameVolumeTests
{
    [Fact]
    public void Gap_IsTheDistanceOutsideTheSurface()
    {
        var centre = new Vector3(0, 0.4f, 0);
        var extents = new Vector3(0.3f, 0.4f, 0.3f);
        Assert.Equal(
            0.2f,
            EnvironmentFeel.FlameGap(new Vector3(0.5f, 0.4f, 0), centre, extents),
            4
        );
        Assert.True(EnvironmentFeel.FlameGap(new Vector3(0.1f, 0.4f, 0), centre, extents) < 0f);
    }

    [Fact]
    public void Box_SpansTheParticlesWidenedByPartOfTheirSize()
    {
        var v = new FlameVolume();
        v.Add(new Vector3(-0.2f, 0.1f, 0f), 0.4f);
        v.Add(new Vector3(0.2f, 0.5f, 0f), 0.8f);
        Assert.Equal(2, v.Count);
        float r1 = 0.4f * FlameVolume.Fill,
            r2 = 0.8f * FlameVolume.Fill;
        var min = new Vector3(-0.2f - r1, 0.1f - r1, -r2);
        var max = new Vector3(0.2f + r2, 0.5f + r2, r2);
        TestUtil.Near((min + max) / 2f, v.Centre);
        TestUtil.Near((max - min) / 2f, v.Extents);
    }

    [Fact]
    public void Box_ResetsBetweenFrames()
    {
        var v = new FlameVolume();
        v.Add(new Vector3(5f, 5f, 5f), 3f);
        v.Reset();
        v.Add(Vector3.Zero, 0.4f);
        TestUtil.Near(Vector3.Zero, v.Centre);
        TestUtil.Near(new Vector3(0.4f * FlameVolume.Fill), v.Extents);
    }
}
