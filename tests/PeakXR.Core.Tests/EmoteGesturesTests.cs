namespace PeakXR.Core.Tests;

public class EmoteGesturesTests
{
    const float Dt = 1f / 90f;

    // Lateral hand position over time: a sine of the given amplitude (m) and frequency (Hz).
    static bool Wave(WaveGesture g, float amplitude, float hz, float seconds, bool raised = true)
    {
        for (float t = 0f; t < seconds; t += Dt)
            if (g.Update(raised, amplitude * MathF.Sin(2f * MathF.PI * hz * t), Dt))
                return true;
        return false;
    }

    [Fact]
    public void Wave_RaisedOpenHandSwingingSideToSide_Fires()
    {
        Assert.True(Wave(new WaveGesture(), 0.1f, 1.5f, 2f));
    }

    [Fact]
    public void Wave_HandDown_NeverFires()
    {
        Assert.False(Wave(new WaveGesture(), 0.1f, 1.5f, 3f, raised: false));
    }

    [Fact]
    public void Wave_SmallTremor_NeverFires()
    {
        Assert.False(Wave(new WaveGesture(), 0.02f, 3f, 3f));
    }

    [Fact]
    public void Wave_SlowDrift_NeverFires()
    {
        // One swing every 2 s: three swings never fit in the window.
        Assert.False(Wave(new WaveGesture(), 0.15f, 0.25f, 6f));
    }

    [Fact]
    public void Wave_OneSwingOnly_DoesNotFire()
    {
        var g = new WaveGesture();
        for (float t = 0f; t < 1f; t += Dt)
            Assert.False(g.Update(true, t * 0.3f, Dt));
    }

    [Fact]
    public void Wave_LoweringTheHand_StartsOver()
    {
        var g = new WaveGesture();
        // Two swings, then the hand drops: the count is lost.
        Assert.False(Wave(g, 0.1f, 1.5f, 0.55f));
        g.Update(false, 0f, Dt);
        Assert.False(Wave(g, 0.1f, 1.5f, 0.55f));
    }

    [Fact]
    public void Cheer_BothHandsUpForTheDwell_FiresOnce()
    {
        var g = new CheerGesture();
        int fired = 0;
        for (float t = 0f; t < 2f; t += Dt)
            if (g.Update(true, Dt))
                fired++;
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Cheer_ShorterThanDwell_DoesNotFire()
    {
        var g = new CheerGesture();
        for (float t = 0f; t < g.Dwell * 0.8f; t += Dt)
            Assert.False(g.Update(true, Dt));
        Assert.False(g.Update(false, Dt));
    }

    [Fact]
    public void Cheer_RearmsAfterTheHandsDrop()
    {
        var g = new CheerGesture();
        bool first = false;
        for (float t = 0f; t < 1f; t += Dt)
            first |= g.Update(true, Dt);
        g.Update(false, Dt);
        bool second = false;
        for (float t = 0f; t < 1f; t += Dt)
            second |= g.Update(true, Dt);
        Assert.True(first && second);
    }

    [Fact]
    public void Raised_AboveTheLineUnderTheHead()
    {
        Assert.True(EmoteGestures.Raised(handY: 1.6f, headY: 1.7f, margin: -0.15f));
        Assert.False(EmoteGestures.Raised(handY: 1.4f, headY: 1.7f, margin: -0.15f));
    }

    [Theory]
    [InlineData(true, false, false, false, false, DenyReason.NotNow)]
    [InlineData(false, true, false, false, false, DenyReason.NotNow)]
    [InlineData(false, false, true, false, false, DenyReason.NotWhileClimbing)]
    [InlineData(false, false, false, true, false, DenyReason.NotNow)]
    [InlineData(false, false, false, false, true, DenyReason.NotNow)]
    [InlineData(false, false, false, false, false, DenyReason.None)]
    public void WheelRefusal_FollowsTheGameGate(
        bool dead,
        bool won,
        bool climbing,
        bool otherWheel,
        bool window,
        DenyReason expected
    )
    {
        Assert.Equal(expected, EmoteGestures.WheelRefusal(dead, won, climbing, otherWheel, window));
    }
}
