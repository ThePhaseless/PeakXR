namespace PeakXR.Core.Tests;

public class ReleaseQueueTests
{
    [Fact]
    public void NothingQueued_Waits()
    {
        var q = new ReleaseQueue();
        Assert.Equal(ReleaseStep.Wait, q.Step(inHand: true, casting: false, 1f, out _));
        Assert.False(q.Pending);
    }

    [Fact]
    public void KeepIsNeverQueued()
    {
        var q = new ReleaseQueue();
        q.Queue(GripReleaseAction.Keep, 1f);
        Assert.False(q.Pending);
    }

    [Fact]
    public void ReleasedBeforeTheItemLanded_DropsOnArrival()
    {
        // Issue #13: a client's pickup lands a round trip after the press (Item.cs:589-591, CharacterItems.cs:441);
        // the grip opened first, and nothing dropped the item when it came.
        var q = new ReleaseQueue();
        q.Queue(GripReleaseAction.Drop, 1f);
        Assert.Equal(ReleaseStep.Wait, q.Step(inHand: false, casting: false, 1.1f, out _));
        Assert.Equal(ReleaseStep.Run, q.Step(inHand: true, casting: false, 1.2f, out var action));
        Assert.Equal(GripReleaseAction.Drop, action);
        Assert.False(q.Pending);
        Assert.Equal(ReleaseStep.Wait, q.Step(true, false, 1.3f, out _));
    }

    [Fact]
    public void AQueuedThrowStaysAThrow()
    {
        var q = new ReleaseQueue();
        q.Queue(GripReleaseAction.Throw, 1f);
        Assert.Equal(ReleaseStep.Run, q.Step(true, false, 1.1f, out var action));
        Assert.Equal(GripReleaseAction.Throw, action);
    }

    [Fact]
    public void NeverArrives_GivesUp()
    {
        var q = new ReleaseQueue();
        q.Queue(GripReleaseAction.Drop, 1f);
        Assert.Equal(
            ReleaseStep.Wait,
            q.Step(false, false, 1f + ReleaseQueue.ArriveSeconds + 0.01f, out _)
        );
        Assert.False(q.Pending);
        // A late arrival after that stays in hand: no drop out of nowhere.
        Assert.Equal(ReleaseStep.Wait, q.Step(true, false, 4f, out _));
    }

    [Fact]
    public void ACastTheOpeningEnds_DropsOnceItHasEnded()
    {
        // The game ignores a drop while the item casts (CharacterItems.cs:254-258); the trigger opening with the grip
        // cancels the cast on the game's next use read (CharacterItems.cs:209-212).
        var q = new ReleaseQueue();
        q.Queue(GripReleaseAction.Drop, 1f);
        Assert.Equal(ReleaseStep.Wait, q.Step(inHand: true, casting: true, 1f, out _));
        Assert.Equal(ReleaseStep.Run, q.Step(inHand: true, casting: false, 1.02f, out _));
    }

    [Fact]
    public void ACastThatKeepsRunning_IsRefused()
    {
        var q = new ReleaseQueue();
        q.Queue(GripReleaseAction.Drop, 1f);
        Assert.Equal(ReleaseStep.Wait, q.Step(true, true, 1f, out _));
        Assert.Equal(
            ReleaseStep.Refused,
            q.Step(true, true, 1f + ReleaseQueue.CastSeconds + 0.01f, out _)
        );
        Assert.False(q.Pending);
    }

    [Fact]
    public void TheCastWaitStartsWhenTheItemLands()
    {
        var q = new ReleaseQueue();
        q.Queue(GripReleaseAction.Drop, 1f);
        Assert.Equal(ReleaseStep.Wait, q.Step(false, false, 1.5f, out _));
        Assert.Equal(ReleaseStep.Wait, q.Step(true, true, 1.6f, out _));
        Assert.Equal(
            ReleaseStep.Wait,
            q.Step(true, true, 1.6f + ReleaseQueue.CastSeconds * 0.5f, out _)
        );
        Assert.Equal(ReleaseStep.Run, q.Step(true, false, 1.8f, out _));
    }

    [Fact]
    public void LeftTheHandAnotherWay_NothingToDrop()
    {
        // Eaten by the cast, stashed or put away by a climb start: the queued drop must not fire on the next item.
        var q = new ReleaseQueue();
        q.Queue(GripReleaseAction.Drop, 1f);
        Assert.Equal(ReleaseStep.Wait, q.Step(true, true, 1f, out _));
        Assert.Equal(ReleaseStep.Wait, q.Step(false, false, 1.05f, out _));
        Assert.False(q.Pending);
    }

    [Fact]
    public void AFreshClose_CancelsIt()
    {
        var q = new ReleaseQueue();
        q.Queue(GripReleaseAction.Drop, 1f);
        q.Clear();
        Assert.Equal(ReleaseStep.Wait, q.Step(true, false, 1.1f, out _));
    }
}
