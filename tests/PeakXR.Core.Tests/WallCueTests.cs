namespace PeakXR.Core.Tests;

public class WallCueTests
{
    static readonly Vector3 Ahead = new(0f, 0f, 1f);

    [Theory]
    [InlineData(0f, 1f, 0f)]
    [InlineData(1f, 0f, 90f)]
    [InlineData(-1f, 0f, -90f)]
    [InlineData(0f, -1f, 180f)]
    public void Arrow_PointsTowardOpenSpace_UpIsAhead(float x, float z, float degrees)
    {
        float a = WallCueMath.ArrowDegrees(new Vector3(x, 0f, z), Ahead);
        // Straight back is +180 or -180, the same arrow.
        Assert.Equal(degrees, degrees == 180f ? MathF.Abs(a) : a, 1);
    }

    [Fact]
    public void Arrow_IgnoresHeightAndHeadPitch()
    {
        // Body centre below a head looking down: still "back", not "down".
        var lookDown = Vector3.Normalize(new Vector3(0f, -0.9f, 0.3f));
        Assert.Equal(
            180f,
            MathF.Abs(WallCueMath.ArrowDegrees(new Vector3(0f, -0.6f, -0.3f), lookDown)),
            1
        );
        Assert.Equal(90f, WallCueMath.ArrowDegrees(new Vector3(0.5f, -2f, 0f), lookDown), 1);
    }

    [Fact]
    public void Arrow_NoHorizontalWay_NaN()
    {
        Assert.True(float.IsNaN(WallCueMath.ArrowDegrees(new Vector3(0f, -1f, 0f), Ahead)));
    }

    [Fact]
    public void Sound_OnEachNewBlock_NotMoreOftenThanTheRepeat()
    {
        var g = new WallCueGate { RepeatSeconds = 2f };
        Assert.False(g.Update(false, 0f));
        Assert.True(g.Update(true, 0.1f));
        Assert.False(g.Update(true, 0.2f));
        Assert.False(g.Update(false, 0.3f));
        // Back in within the repeat: quiet, the ring still shows.
        Assert.False(g.Update(true, 0.5f));
        Assert.False(g.Update(false, 0.6f));
        Assert.True(g.Update(true, 2.5f));
    }
}
