using System.Collections.Generic;

namespace PeakXR.Core.Tests;

public class GripLatchTests
{
    const float Dt = 1f / 90f;

    // One frame: raw grip, engaged (the hand holds something by its grip), mode, tracked.
    static GripLatchEvent Step(
        GripLatch l,
        float raw,
        bool engaged = true,
        GripMode mode = GripMode.Toggle,
        bool tracked = true,
        float dt = Dt
    ) => l.Update(raw, tracked, engaged, mode, dt);

    // Holds raw for the given seconds, collecting the events seen.
    static List<GripLatchEvent> Hold(
        GripLatch l,
        float raw,
        float seconds,
        bool engaged = true,
        GripMode mode = GripMode.Toggle,
        bool tracked = true
    )
    {
        var events = new List<GripLatchEvent>();
        for (float t = 0f; t < seconds; t += Dt)
        {
            var e = Step(l, raw, engaged, mode, tracked);
            if (e != GripLatchEvent.None)
                events.Add(e);
        }
        return events;
    }

    static GripLatch Latched()
    {
        var l = new GripLatch();
        Hold(l, 1f, 0.3f);
        Assert.Equal(GripLatchEvent.Latched, Step(l, 0f));
        return l;
    }

    [Fact]
    public void Hold_PassesTheGripThrough()
    {
        var l = new GripLatch();
        Assert.Empty(Hold(l, 1f, 0.3f, mode: GripMode.Hold));
        Assert.Equal(1f, l.Grip);
        Assert.Equal(GripLatchEvent.None, Step(l, 0.1f, mode: GripMode.Hold));
        Assert.Equal(0.1f, l.Grip);
        Assert.False(l.Latched);
    }

    [Fact]
    public void Press_PassesThrough_ReleaseLatches_AndKeepsTheHandClosed()
    {
        var l = new GripLatch();
        Assert.Equal(GripLatchEvent.None, Step(l, 0.7f));
        Assert.Equal(0.7f, l.Grip);
        Hold(l, 1f, 0.3f);
        Assert.Equal(GripLatchEvent.Latched, Step(l, 0.2f));
        Assert.True(l.Latched);
        Assert.Equal(1f, l.Grip);
        Assert.Empty(Hold(l, 0f, 5f));
        Assert.Equal(1f, l.Grip);
    }

    [Fact]
    public void ReleaseWithNothingHeld_IsAPlainRelease()
    {
        var l = new GripLatch();
        Hold(l, 1f, 0.3f, engaged: false);
        Assert.Equal(GripLatchEvent.None, Step(l, 0f, engaged: false));
        Assert.False(l.Latched);
        Assert.Equal(0f, l.Grip);
    }

    [Fact]
    public void SecondPressAndRelease_Unlatches_OnTheRelease()
    {
        var l = Latched();
        Assert.Empty(Hold(l, 1f, 0.3f));
        Assert.True(l.Latched);
        Assert.Equal(1f, l.Grip);
        Assert.Empty(Hold(l, 0.5f, 0.2f));
        Assert.Equal(1f, l.Grip);
        Assert.Equal(GripLatchEvent.Unlatched, Step(l, 0.3f));
        Assert.False(l.Latched);
        Assert.Equal(0.3f, l.Grip);
        // The next press is a new grip, not another unlatch.
        Assert.Empty(Hold(l, 0f, 0.2f));
        Assert.Equal(0f, l.Grip);
    }

    [Fact]
    public void ThirdPress_LatchesAgain()
    {
        var l = Latched();
        Hold(l, 1f, 0.3f);
        Assert.Equal(GripLatchEvent.Unlatched, Step(l, 0f));
        Hold(l, 1f, 0.3f);
        Assert.Equal(GripLatchEvent.Latched, Step(l, 0f));
    }

    [Theory]
    [InlineData(0.55f, 0.3f)]
    [InlineData(0.7f, 0.1f)]
    [InlineData(0.74f, 0f)]
    public void RelaxedOrTwitchingHand_NeverUnlatches(float high, float low)
    {
        var l = Latched();
        for (int i = 0; i < 200; i++)
            Assert.Equal(GripLatchEvent.None, Step(l, i % 2 == 0 ? high : low));
        Assert.True(l.Latched);
    }

    [Fact]
    public void ShortSpikeAboveTheSqueeze_DoesNotUnlatch()
    {
        var l = Latched();
        // Under GripLatch.SqueezeSeconds above the squeeze, then open: a bump, not a squeeze.
        Assert.Empty(Hold(l, 1f, GripLatch.SqueezeSeconds * 0.5f));
        Assert.Empty(Hold(l, 0f, 0.5f));
        Assert.True(l.Latched);
    }

    [Fact]
    public void SqueezeHeldButNotReleasedPastTheThreshold_StaysLatched()
    {
        var l = Latched();
        Hold(l, 1f, 0.3f);
        // Down to 0.45: still above the release threshold.
        Assert.Empty(Hold(l, 0.45f, 1f));
        Assert.True(l.Latched);
        Assert.Equal(GripLatchEvent.Unlatched, Step(l, 0.39f));
    }

    [Fact]
    public void NoisyFirstPress_LatchesOnce()
    {
        var l = new GripLatch();
        var events = new List<GripLatchEvent>();
        float[] noisy = { 0.58f, 0.61f, 0.45f, 0.62f, 0.41f, 0.9f, 0.42f, 0.38f, 0.43f, 0.2f };
        foreach (var v in noisy)
        {
            var e = Step(l, v);
            if (e != GripLatchEvent.None)
                events.Add(e);
        }
        Assert.Equal(new[] { GripLatchEvent.Latched }, events);
    }

    [Fact]
    public void SwitchToHold_WhileLatchedAndOpen_ReleasesWithTheUnlatchTick()
    {
        var l = Latched();
        Assert.Equal(GripLatchEvent.Unlatched, Step(l, 0f, mode: GripMode.Hold));
        Assert.False(l.Latched);
        Assert.Equal(0f, l.Grip);
    }

    [Fact]
    public void SwitchToHold_WhileLatchedAndClosed_KeepsHolding_UntilTheGripOpens()
    {
        var l = Latched();
        Hold(l, 1f, 0.05f);
        Assert.Equal(GripLatchEvent.None, Step(l, 0.9f, mode: GripMode.Hold));
        Assert.False(l.Latched);
        Assert.Equal(0.9f, l.Grip);
        // Now a plain Hold grip: opening it is an ordinary release.
        Assert.Equal(GripLatchEvent.None, Step(l, 0f, mode: GripMode.Hold));
        Assert.Equal(0f, l.Grip);
    }

    [Fact]
    public void SwitchToToggle_MidGrip_LatchesOnItsRelease()
    {
        var l = new GripLatch();
        Hold(l, 1f, 0.3f, mode: GripMode.Hold);
        Assert.Equal(GripLatchEvent.Latched, Step(l, 0f));
    }

    [Fact]
    public void HoldLost_LapsesQuietly()
    {
        var l = Latched();
        // Torn off the wall, the item eaten: the game ended the hold, which has its own cue.
        Assert.Equal(GripLatchEvent.None, Step(l, 0f, engaged: false));
        Assert.False(l.Latched);
        Assert.Equal(0f, l.Grip);
    }

    [Fact]
    public void TrackingLost_FreezesTheLatch()
    {
        var l = Latched();
        Hold(l, 1f, 0.3f);
        // Whatever a lost controller reports, nothing unlatches, even a full squeeze and release.
        Assert.Empty(Hold(l, 0f, 0.5f, tracked: false));
        Assert.Empty(Hold(l, 1f, 0.5f, tracked: false));
        Assert.True(l.Latched);
        Assert.Equal(1f, l.Grip);
        Assert.Empty(Hold(l, 0f, 0.5f, tracked: false));
        Assert.True(l.Latched);
        // Back with the hand open: the squeeze before the loss still counts, so this opening is the unlatch.
        Assert.Equal(GripLatchEvent.Unlatched, Step(l, 0f));
    }

    [Fact]
    public void TrackingLost_BeforeTheSqueeze_StaysLatchedWhenBack()
    {
        var l = Latched();
        Assert.Empty(Hold(l, 1f, 0.5f, tracked: false));
        Assert.Empty(Hold(l, 0f, 0.5f));
        Assert.True(l.Latched);
    }

    [Fact]
    public void Reset_Opens()
    {
        var l = Latched();
        l.Reset();
        Assert.False(l.Latched);
        Assert.Equal(GripLatchEvent.None, Step(l, 0f));
        Assert.Equal(0f, l.Grip);
    }
}
