namespace PeakXR.Core.Tests;

public class XRFocusTests
{
    [Theory]
    [InlineData(0, true)] // not reported yet
    [InlineData(5, true)] // FOCUSED
    [InlineData(4, false)] // VISIBLE: dashboard or another app has input (run 23 never left it)
    [InlineData(3, false)]
    [InlineData(1, false)]
    public void OnlyFocusedOrUnknownHasInput(int state, bool focused) =>
        Assert.Equal(focused, XRFocus.Focused(state));

    [Fact]
    public void NamesTheSpecStates()
    {
        Assert.Equal("VISIBLE", XRFocus.Name(4));
        Assert.Equal("FOCUSED", XRFocus.Name(5));
        Assert.Equal("not reported", XRFocus.Name(0));
        Assert.Equal("state 42", XRFocus.Name(42));
    }
}
