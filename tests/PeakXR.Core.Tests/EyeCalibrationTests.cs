namespace PeakXR.Core.Tests;

public class EyeCalibrationTests
{
    static readonly Vector2 Eyes = new(0.08f, 0.82f);

    [Fact]
    public void FirstSample_IsTakenAtOnce()
    {
        // The first sample waits for a still stand, long after the view was placed: it still replaces the guess.
        var cal = new EyeCalibration(new Vector2(0.1f, 0.6f));
        Assert.True(cal.Sample(Eyes, leaning: false, dt: 0.011f));
        Assert.Equal(Eyes, cal.Offset);
        Assert.True(cal.Converged);
    }

    [Fact]
    public void OnlyAFirstSampleThatWouldVisiblyMoveTheView_NeedsABlink()
    {
        var cal = new EyeCalibration(new Vector2(0.1f, 0.6f));
        Assert.True(cal.FirstSampleNeedsBlink(Eyes));
        Assert.False(cal.FirstSampleNeedsBlink(new Vector2(0.1f, 0.61f)));
        cal.Sample(Eyes, false, 0.011f);
        // Later samples glide: never a blink.
        Assert.False(cal.FirstSampleNeedsBlink(new Vector2(0.1f, 0.6f)));
    }

    [Fact]
    public void LaterSamples_GlideNeverJump()
    {
        var cal = new EyeCalibration(new Vector2(0.1f, 0.6f));
        cal.Sample(new Vector2(0.08f, 0.62f), false, 0.011f);
        for (int i = 0; i < 90; i++)
            cal.Sample(Eyes, false, 1f / 90f);
        Assert.True(
            Vector2.Distance(Eyes, cal.Offset) > 0.05f,
            "a 0.2 m change landed within a second"
        );
        for (int i = 0; i < 900; i++)
            cal.Sample(Eyes, false, 1f / 90f);
        Assert.True(Vector2.Distance(Eyes, cal.Offset) < 0.01f);
    }

    [Fact]
    public void ASmallHeldLean_NeverMovesTheOffset()
    {
        // The lean puts the avatar's eyes on the headset; sampling them would walk the view after the head.
        var cal = new EyeCalibration(Eyes);
        cal.Sample(Eyes, false, 0.011f);
        var leaned = Eyes + new Vector2(0.02f, -0.005f);
        for (int i = 0; i < 900; i++)
            Assert.False(cal.Sample(leaned, leaning: true, dt: 1f / 90f));
        Assert.Equal(Eyes, cal.Offset);
    }

    [Fact]
    public void Reset_ForgetsTheCalibration()
    {
        var cal = new EyeCalibration(new Vector2(0.1f, 0.8f));
        cal.Sample(Eyes, false, 0.011f);
        cal.Reset();
        Assert.False(cal.Have);
        Assert.False(cal.Converged);
        Assert.Equal(new Vector2(0.1f, 0.8f), cal.Offset);
    }
}
