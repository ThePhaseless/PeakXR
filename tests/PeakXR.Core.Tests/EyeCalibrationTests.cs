namespace PeakXR.Core.Tests;

public class EyeCalibrationTests
{
    static readonly Vector2 Eyes = new(0.08f, 0.82f);

    [Fact]
    public void FirstSampleOnASnap_IsTakenAtOnce()
    {
        var cal = new EyeCalibration(new Vector2(0.1f, 0.8f));
        Assert.True(cal.Sample(Eyes, leaning: false, snap: true, dt: 0.011f));
        Assert.Equal(Eyes, cal.Offset);
        Assert.True(cal.Converged);
    }

    [Fact]
    public void WithoutASnap_GlidesAndConvergesBeforeTheLeanMayStart()
    {
        var cal = new EyeCalibration(new Vector2(0.1f, 0.6f));
        for (int i = 0; i < 90 && !cal.Converged; i++)
            cal.Sample(Eyes, false, false, 1f / 90f);
        Assert.False(cal.Converged, "converged within a second of a 0.2 m glide");
        for (int i = 0; i < 900 && !cal.Converged; i++)
            cal.Sample(Eyes, false, false, 1f / 90f);
        Assert.True(cal.Converged);
        Assert.True(Vector2.Distance(Eyes, cal.Offset) < 0.01f);
    }

    [Fact]
    public void ASmallHeldLean_NeverMovesTheOffset()
    {
        // The lean puts the avatar's eyes on the headset; sampling them would walk the view after the head.
        var cal = new EyeCalibration(Eyes);
        cal.Sample(Eyes, false, true, 0.011f);
        var leaned = Eyes + new Vector2(0.02f, -0.005f);
        for (int i = 0; i < 900; i++)
            Assert.False(cal.Sample(leaned, leaning: true, snap: false, dt: 1f / 90f));
        Assert.Equal(Eyes, cal.Offset);
    }

    [Fact]
    public void Reset_ForgetsTheCalibration()
    {
        var cal = new EyeCalibration(new Vector2(0.1f, 0.8f));
        cal.Sample(Eyes, false, true, 0.011f);
        cal.Reset();
        Assert.False(cal.Have);
        Assert.False(cal.Converged);
        Assert.Equal(new Vector2(0.1f, 0.8f), cal.Offset);
    }
}
