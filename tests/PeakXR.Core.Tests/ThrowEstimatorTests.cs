namespace PeakXR.Core.Tests;

public class ThrowEstimatorTests
{
    [Fact]
    public void NoSamples_GivesZero()
    {
        Assert.Equal(Vector3.Zero, new ThrowEstimator().GetReleaseVelocity());
        var t = new ThrowEstimator();
        t.AddSample(Vector3.One, 1f);
        Assert.Equal(Vector3.Zero, t.GetReleaseVelocity());
    }

    [Fact]
    public void LinearMotion_GivesExactVelocity()
    {
        var t = new ThrowEstimator();
        var v = new Vector3(2f, 3f, -1f);
        for (int i = 0; i < 60; i++)
        {
            float time = i / 90f;
            t.AddSample(new Vector3(1, 1, 1) + v * time, time);
        }
        TestUtil.Near(v, t.GetReleaseVelocity(), 1e-3f);
    }

    [Fact]
    public void UsesOnlyRecentWindow()
    {
        var t = new ThrowEstimator { Window = 0.08f };
        float time = 0f;
        var pos = Vector3.Zero;
        // Slow for a second, then fast for the last 100 ms.
        for (; time < 1f; time += 1f / 90f) { pos += new Vector3(0, 0, 0.5f) / 90f; t.AddSample(pos, time); }
        for (int i = 0; i < 9; i++, time += 1f / 90f) { pos += new Vector3(0, 0, 6f) / 90f; t.AddSample(pos, time); }
        TestUtil.Near(6f, t.GetReleaseVelocity().Z, 0.05f);
    }

    [Fact]
    public void NoisyMotion_StaysClose()
    {
        var rng = new Random(5);
        var t = new ThrowEstimator();
        var v = new Vector3(0f, 4f, 3f);
        for (int i = 0; i < 30; i++)
        {
            float time = i / 120f;
            var noise = new Vector3(rng.NextSingle() - 0.5f, rng.NextSingle() - 0.5f, rng.NextSingle() - 0.5f) * 0.004f;
            t.AddSample(v * time + noise, time);
        }
        Assert.True(Vector3.Distance(v, t.GetReleaseVelocity()) < 0.5f);
    }

    [Fact]
    public void ClampsToMaxSpeed()
    {
        var t = new ThrowEstimator { MaxSpeed = 10f };
        for (int i = 0; i < 10; i++)
            t.AddSample(new Vector3(100f * i / 90f, 0, 0), i / 90f);
        TestUtil.Near(10f, t.GetReleaseVelocity().Length(), 1e-3f);
    }

    [Fact]
    public void TimeGoingBackwards_RestartsHistory()
    {
        var t = new ThrowEstimator();
        for (int i = 0; i < 10; i++) t.AddSample(new Vector3(i, 0, 0), 5f + i / 90f);
        t.AddSample(Vector3.Zero, 0f);
        t.AddSample(new Vector3(0, 0.01f, 0), 0.01f);
        TestUtil.Near(new Vector3(0, 1f, 0), t.GetReleaseVelocity(), 1e-3f);
    }

    [Fact]
    public void Clear_DropsSamples()
    {
        var t = new ThrowEstimator();
        t.AddSample(Vector3.Zero, 0f);
        t.AddSample(Vector3.One, 0.01f);
        t.Clear();
        Assert.Equal(Vector3.Zero, t.GetReleaseVelocity());
    }
}
