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

    [Theory]
    [InlineData(0, true)] // not reported: assume shown, so a missing state callback never blanks the mirror
    [InlineData(5, true)] // FOCUSED
    [InlineData(4, true)] // VISIBLE: the dashboard is over the game, which still renders
    [InlineData(3, false)] // SYNCHRONIZED: frames are paced but shouldRender is false (headset off)
    [InlineData(6, false)] // STOPPING: the session ends before the next frame
    [InlineData(1, false)] // IDLE: no session, the eye textures are the runtime's
    [InlineData(2, false)] // READY: not begun yet
    public void OnlyVisibleOrFocusedFramesReachTheHeadset(int state, bool shown) =>
        Assert.Equal(shown, XRFocus.Shown(state));

    [Fact]
    public void NamesTheSpecStates()
    {
        Assert.Equal("VISIBLE", XRFocus.Name(4));
        Assert.Equal("FOCUSED", XRFocus.Name(5));
        Assert.Equal("not reported", XRFocus.Name(0));
        Assert.Equal("state 42", XRFocus.Name(42));
    }

    [Theory]
    [InlineData(false, true, false, true)] // worn -> taken off: pause
    [InlineData(true, false, false, true)] // still off: stays paused
    [InlineData(true, false, true, false)] // worn again: resume
    [InlineData(true, false, null, false)] // sensor gone: never hold a pause on nothing
    [InlineData(false, null, false, false)] // never reported worn: a stuck "not worn" never freezes play
    [InlineData(false, false, false, false)] // off from the start: same
    [InlineData(false, true, true, false)] // worn: play
    public void WearSensorPausesOnlyAfterAWornReading(
        bool paused,
        bool? was,
        bool? now,
        bool expected
    ) => Assert.Equal(expected, XRFocus.TakenOff(paused, was, now));

    [Theory]
    [InlineData(0.0, 33)]
    [InlineData(0.010, 23)]
    [InlineData(0.0334, 0)]
    [InlineData(0.5, 0)]
    [InlineData(-1.0, 33)] // a clock that went back sleeps one frame, never longer
    public void IdlePacingSleepsTheRestOfAFrameAtIdleRate(double elapsed, int ms) =>
        Assert.Equal(ms, XRFocus.IdleSleepMs(elapsed));
}
