namespace PeakXR.Core.Tests;

public class WheelGripLatchTests
{
    // Shorthand: open, opened by an extension, left/right injected interact, left/right grip still closed.
    static void Step(
        WheelGripLatch l,
        bool open,
        bool byExtension = false,
        bool leftInjected = false,
        bool rightInjected = false,
        bool leftClosed = false,
        bool rightClosed = false
    ) => l.Update(open, byExtension, leftInjected, rightInjected, leftClosed, rightClosed);

    [Fact]
    public void GameOpenedWheel_HeldByTheInjectingGrip_UntilItOpens()
    {
        var l = new WheelGripLatch();
        Step(l, open: false, rightInjected: true, rightClosed: true);
        Assert.False(l.Holds);
        Step(l, open: true, rightInjected: true, rightClosed: true);
        Assert.True(l.Right);
        Assert.False(l.Left);
        Step(l, open: true, rightInjected: true, rightClosed: true);
        Assert.True(l.Holds);
        Step(l, open: true, rightInjected: true, rightClosed: false);
        Assert.False(l.Holds);
    }

    [Fact]
    public void ShoulderWheel_ItemHandStillClosed_IsNotLatched()
    {
        // Right grip holds a picked-up item (no longer injecting); the left reaches over the shoulder.
        var l = new WheelGripLatch();
        Step(l, open: false, rightClosed: true);
        Step(l, open: true, byExtension: true, leftClosed: true, rightClosed: true);
        Assert.False(l.Holds);
        Step(l, open: true, leftClosed: false, rightClosed: true);
        Assert.False(l.Holds);
    }

    [Fact]
    public void ShoulderWheel_InteractFistOnTheOtherHand_IsNotLatched()
    {
        var l = new WheelGripLatch();
        Step(l, open: false, rightInjected: true, rightClosed: true);
        Step(
            l,
            open: true,
            byExtension: true,
            rightInjected: true,
            leftClosed: true,
            rightClosed: true
        );
        Assert.False(l.Holds);
    }

    [Fact]
    public void BothHandsInjecting_HoldsUntilBothOpen()
    {
        var l = new WheelGripLatch();
        Step(
            l,
            open: true,
            leftInjected: true,
            rightInjected: true,
            leftClosed: true,
            rightClosed: true
        );
        Step(l, open: true, leftClosed: false, rightClosed: true);
        Assert.True(l.Holds);
        Step(l, open: true, leftClosed: false, rightClosed: false);
        Assert.False(l.Holds);
    }

    [Fact]
    public void GripClosedAgainWhileOpen_DoesNotRelatch()
    {
        var l = new WheelGripLatch();
        Step(l, open: true, rightInjected: true, rightClosed: true);
        Step(l, open: true, rightClosed: false);
        Step(l, open: true, rightInjected: true, rightClosed: true);
        Assert.False(l.Holds);
    }

    [Fact]
    public void WheelClosed_ClearsTheLatch_AndTheNextOpenDecidesAgain()
    {
        var l = new WheelGripLatch();
        Step(l, open: true, rightInjected: true, rightClosed: true);
        Step(l, open: false, rightInjected: true, rightClosed: true);
        Assert.False(l.Holds);
        Step(l, open: true, leftInjected: true, leftClosed: true, rightClosed: true);
        Assert.True(l.Left);
        Assert.False(l.Right);
    }
}
