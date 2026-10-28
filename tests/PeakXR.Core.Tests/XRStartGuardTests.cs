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
}
