namespace PeakXR.Core.Tests;

public class HoldGraceTests
{
    [Fact]
    public void BriefDropout_KeepsTheHold()
    {
        // A jittering hand's ray grazes off the target's edge for a frame or two.
        var g = new HoldGrace();
        Assert.Equal(HoldGraceStep.None, g.Update(holding: true, seen: true, now: 0f));
        Assert.Equal(HoldGraceStep.Keep, g.Update(true, false, 0.01f));
        Assert.Equal(HoldGraceStep.Keep, g.Update(true, false, 0.01f + HoldGrace.Grace * 0.9f));
        Assert.Equal(HoldGraceStep.None, g.Update(true, true, 0.2f));
    }

    [Fact]
    public void LongDropout_DropsOnceWithACancel()
    {
        var g = new HoldGrace();
        g.Update(true, true, 0f);
        Assert.Equal(HoldGraceStep.Keep, g.Update(true, false, 0.1f));
        Assert.Equal(HoldGraceStep.Drop, g.Update(true, false, 0.1f + HoldGrace.Grace + 0.01f));
        // The game lets go now; no second cancel.
        Assert.Equal(HoldGraceStep.None, g.Update(true, false, 0.6f));
        Assert.Equal(HoldGraceStep.None, g.Update(false, false, 0.7f));
    }

    [Fact]
    public void NoHold_DoesNothing() =>
        Assert.Equal(HoldGraceStep.None, new HoldGrace().Update(false, false, 1f));

    [Fact]
    public void NewHold_StartsAFreshGrace()
    {
        var g = new HoldGrace();
        g.Update(true, false, 0f);
        g.Update(true, false, 1f);
        g.Update(false, false, 1.1f);
        Assert.Equal(HoldGraceStep.Keep, g.Update(true, false, 2f));
    }
}
