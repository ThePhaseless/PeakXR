namespace PeakXR.Core.Tests;

public class RebindPromptInputTests
{
    private const float Frame = 0.016f;

    private static RebindPromptStep Step(
        RebindPromptInput input,
        bool menu = false,
        bool dashboard = false,
        bool left = false,
        bool right = false,
        bool off = false,
        float dt = Frame
    ) => input.Step(menu, dashboard, off, left, right, dt);

    [Fact]
    public void QuietFrameDoesNothing() =>
        Assert.Equal(RebindPromptStep.None, Step(new RebindPromptInput()));

    [Fact]
    public void MenuTapCancels() =>
        Assert.Equal(RebindPromptStep.Cancel, Step(new RebindPromptInput(), menu: true));

    [Fact]
    public void AControlHeldAsThePromptOpensIsNoPress()
    {
        var input = new RebindPromptInput();
        Assert.Equal(RebindPromptStep.None, Step(input, right: true));
        Assert.Equal(RebindPromptStep.None, Step(input, right: true));
    }

    [Fact]
    public void EachNewPressRefusesOnThatHandOnce()
    {
        var input = new RebindPromptInput();
        Step(input);
        Assert.Equal(RebindPromptStep.RefuseRight, Step(input, right: true));
        Assert.Equal(RebindPromptStep.None, Step(input, right: true));
        Step(input);
        Assert.Equal(RebindPromptStep.RefuseLeft, Step(input, left: true));
    }

    [Fact]
    public void BothHandsPressingOnOneFrameRefuseBoth()
    {
        var input = new RebindPromptInput();
        Step(input);
        Assert.Equal(RebindPromptStep.RefuseLeft, Step(input, left: true, right: true));
        Assert.Equal(RebindPromptStep.RefuseRight, Step(input, left: true, right: true));
        Assert.Equal(RebindPromptStep.None, Step(input, left: true, right: true));
    }

    [Fact]
    public void DashboardCancelsOnlyOnceItStays()
    {
        var input = new RebindPromptInput();
        Step(input);
        Assert.Equal(
            RebindPromptStep.None,
            Step(input, dashboard: true, dt: RebindPromptInput.DashboardSeconds * 0.9f)
        );
        Assert.Equal(
            RebindPromptStep.Cancel,
            Step(input, dashboard: true, dt: RebindPromptInput.DashboardSeconds * 0.2f)
        );
    }

    [Fact]
    public void ABriefPassThroughTheDashboardStateStartsOver()
    {
        var input = new RebindPromptInput();
        Step(input);
        Step(input, dashboard: true, dt: RebindPromptInput.DashboardSeconds * 0.9f);
        Step(input);
        Assert.Equal(
            RebindPromptStep.None,
            Step(input, dashboard: true, dt: RebindPromptInput.DashboardSeconds * 0.9f)
        );
    }

    [Fact]
    public void APromptOpenedUnderTheDashboardIsNotCancelledByIt()
    {
        var input = new RebindPromptInput();
        Assert.Equal(
            RebindPromptStep.None,
            Step(input, dashboard: true, dt: RebindPromptInput.DashboardSeconds * 2f)
        );
        Assert.Equal(
            RebindPromptStep.None,
            Step(input, dashboard: true, dt: RebindPromptInput.DashboardSeconds * 2f)
        );
    }

    [Fact]
    public void AHeadsetOffNeverCountsAsTheDashboard()
    {
        var input = new RebindPromptInput();
        Step(input);
        Assert.Equal(
            RebindPromptStep.None,
            Step(input, dashboard: true, off: true, dt: RebindPromptInput.DashboardSeconds * 2f)
        );
    }

    [Fact]
    public void TheDashboardStateOnTheWayBackOnIsIgnoredUntilFocusIsSeen()
    {
        var input = new RebindPromptInput();
        Step(input);
        Step(input, off: true);
        Assert.Equal(
            RebindPromptStep.None,
            Step(input, dashboard: true, dt: RebindPromptInput.DashboardSeconds * 2f)
        );
        Step(input);
        Assert.Equal(
            RebindPromptStep.Cancel,
            Step(input, dashboard: true, dt: RebindPromptInput.DashboardSeconds * 2f)
        );
    }

    [Fact]
    public void AMenuTapCancelsWithTheHeadsetOff() =>
        Assert.Equal(RebindPromptStep.Cancel, Step(new RebindPromptInput(), menu: true, off: true));

    [Fact]
    public void ResetForgetsHeldControlsAndTheDashboardWait()
    {
        var input = new RebindPromptInput();
        Step(input);
        Step(input, dashboard: true, dt: RebindPromptInput.DashboardSeconds * 0.9f);
        input.Reset();
        Assert.Equal(RebindPromptStep.None, Step(input, right: true));
        Assert.Equal(RebindPromptStep.None, Step(input, right: true));
    }
}
