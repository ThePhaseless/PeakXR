namespace PeakXR.Core.Tests;

public class DrawnPixelsTests
{
    private static byte[] Image(int pixels, byte value)
    {
        var data = new byte[pixels * 4];
        Array.Fill(data, value);
        return data;
    }

    [Fact]
    public void ChangedShareCountsPixelsThatMovedPastTheStep()
    {
        var a = Image(4, 100);
        var b = Image(4, 100);
        b[0] = 100 + DrawnPixels.ChannelStep; // first pixel, red, moved far enough
        b[6] = 100 - DrawnPixels.ChannelStep; // second pixel, blue, downward
        b[8] = 100 + DrawnPixels.ChannelStep - 1; // third pixel, just short of it
        Assert.Equal(0.5f, DrawnPixels.ChangedShare(a, b));
    }

    [Fact]
    public void ChangedShareIgnoresAlpha()
    {
        var a = Image(1, 100);
        var b = Image(1, 100);
        b[3] = 255;
        Assert.Equal(0f, DrawnPixels.ChangedShare(a, b));
    }

    [Fact]
    public void ChangedShareOfMismatchedOrEmptyImagesIsZero()
    {
        Assert.Equal(0f, DrawnPixels.ChangedShare([], Image(1, 0)));
        Assert.Equal(0f, DrawnPixels.ChangedShare(Image(1, 0), Image(2, 0)));
    }

    [Theory]
    [InlineData(0f, 0.067f, true)] // the title popup: about 7 % of an eye
    [InlineData(0.004f, 0.02f, true)]
    [InlineData(0f, 0.005f, false)] // a laser dot's worth
    [InlineData(0.03f, 0.031f, false)] // moving scene: hiding the layer changed no more than a frame does anyway
    [InlineData(0f, 0f, false)]
    public void PanelCountsAsDrawnOnlyWhenHidingItChangesMoreThanTheSceneDoes(
        float noise,
        float signal,
        bool drawn
    ) => Assert.Equal(drawn, DrawnPixels.PanelDrawn(noise, signal));

    [Fact]
    public void SteadyFramesCountsFramesSinceTheLastChange()
    {
        var steady = new SteadyFrames();
        Assert.Equal(0, steady.Feed(7)); // the first look has nothing to match
        Assert.Equal(1, steady.Feed(7));
        Assert.Equal(2, steady.Feed(7));
        Assert.Equal(0, steady.Feed(8)); // a plate arrives
        Assert.Equal(1, steady.Feed(8));
    }

    [Fact]
    public void SteadyFramesFirstLookDoesNotMatchAnUnsetLast() =>
        Assert.Equal(0, new SteadyFrames().Feed(0));
}
