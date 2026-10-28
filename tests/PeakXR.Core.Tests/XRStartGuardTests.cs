namespace PeakXR.Core.Tests;

public class XRStartGuardTests
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("garbage", 0)]
    [InlineData("-3", 0)]
    [InlineData("1", 1)]
    [InlineData(" 2\n", 2)]
    public void ReadsTheUnfinishedStartCount(string text, int expected) =>
        Assert.Equal(expected, XRStartGuard.Parse(text));

    [Fact]
    public void OneCrashRetriesTwoInARowStartFlat()
    {
        Assert.False(XRStartGuard.StartFlat(0));
        Assert.False(XRStartGuard.StartFlat(1));
        Assert.True(XRStartGuard.StartFlat(2));
        Assert.True(XRStartGuard.StartFlat(5));
    }

    [Fact]
    public void EachStartAddsOne() => Assert.Equal("2", XRStartGuard.Next(XRStartGuard.Parse("1")));

    [Fact]
    public void RenderedFramesAloneKeepTheGuardOpen() =>
        Assert.Equal(XRStartGuard.Close.Open, XRStartGuard.CloseAt(false, frames: 3, seconds: 1f));

    [Fact]
    public void RuntimeReportingAFrameShownClosesIt() =>
        Assert.Equal(
            XRStartGuard.Close.Shown,
            XRStartGuard.CloseAt(true, frames: 3, seconds: 0.1f)
        );

    [Fact]
    public void ShownBeforeTheFirstFramesWaitsForThem() =>
        Assert.Equal(XRStartGuard.Close.Open, XRStartGuard.CloseAt(true, frames: 1, seconds: 0.1f));

    [Fact]
    public void NoSignalClosesItAtTheTimeout()
    {
        Assert.Equal(
            XRStartGuard.Close.Open,
            XRStartGuard.CloseAt(
                false,
                frames: 500,
                seconds: XRStartGuard.ShownTimeoutSeconds - 0.1f
            )
        );
        Assert.Equal(
            XRStartGuard.Close.TimedOut,
            XRStartGuard.CloseAt(false, frames: 500, seconds: XRStartGuard.ShownTimeoutSeconds)
        );
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public void OpenXRShowsFramesWhenVisibleOrFocused(int state, bool shown) =>
        Assert.Equal(shown, XRStartGuard.OpenXRShown(state));
}
