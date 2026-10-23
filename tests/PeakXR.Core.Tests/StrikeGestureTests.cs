namespace PeakXR.Core.Tests;

public class StrikeGestureTests
{
    static StrikeGesture Make() =>
        new StrikeGesture
        {
            Radius = 0.15f,
            RearmRadius = 0.3f,
            MinSpeed = 1.5f,
        };

    [Fact]
    public void FastHitInsideRadius_AfterComingFromOutside_Strikes()
    {
        var s = Make();
        Assert.False(s.Update(0.5f, 2f));
        Assert.True(s.Update(0.1f, 2f));
    }

    [Fact]
    public void SlowTouch_DoesNotStrike()
    {
        var s = Make();
        s.Update(0.5f, 0.5f);
        Assert.False(s.Update(0.1f, 0.5f));
    }

    [Fact]
    public void StartingInside_DoesNotStrike()
    {
        var s = Make();
        Assert.False(s.Update(0.1f, 3f));
    }

    [Fact]
    public void SecondStrike_NeedsTheHandToPullBackFirst()
    {
        var s = Make();
        s.Update(0.5f, 2f);
        Assert.True(s.Update(0.1f, 2f));
        Assert.False(s.Update(0.12f, 2f));
        Assert.False(s.Update(0.2f, 2f));
        s.Update(0.35f, 0f);
        Assert.True(s.Update(0.1f, 2f));
    }

    [Fact]
    public void Count_TracksStrikesUntilReset()
    {
        var s = Make();
        s.Update(0.5f, 2f);
        s.Update(0.1f, 2f);
        s.Update(0.5f, 2f);
        s.Update(0.1f, 2f);
        Assert.Equal(2, s.Count);
        s.Reset();
        Assert.Equal(0, s.Count);
    }

    [Theory]
    [InlineData(90f)]
    [InlineData(36f)]
    public void HammerSwing_LandsAtLowFrameRates(float fps)
    {
        // SpikeHammer's tuning; one swing from 0.45 m out to the spike in 0.12 s, eased (VR run 58 ran at 36 fps).
        var s = new StrikeGesture
        {
            Radius = 0.2f,
            RearmRadius = 0.35f,
            MinSpeed = 1.2f,
        };
        float dt = 1f / fps;
        float last = 0.45f;
        Assert.False(s.Update(last, 0f));
        bool landed = false;
        for (float t = dt; t <= 0.12f + 1e-4f; t += dt)
        {
            float u = MathF.Min(t / 0.12f, 1f);
            float d = 0.45f * (1f - u * u * (3f - 2f * u));
            landed |= s.Update(d, (last - d) / dt);
            last = d;
        }
        Assert.True(landed);
        Assert.False(s.Armed);
    }
}
