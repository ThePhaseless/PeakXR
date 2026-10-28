namespace PeakXR.Core.Tests;

public class FadeCutTests
{
    [Fact]
    public void Ready_AtDrawnBlack_OrAfterTheTimeout()
    {
        Assert.False(FadeCut.Ready(0.6f, 0.1f));
        Assert.True(FadeCut.Ready(0.995f, 0f));
        Assert.True(FadeCut.Ready(0f, FadeCut.MaxWaitSeconds));
    }

    static CameraTakeover Takeover() =>
        new()
        {
            EnterDelay = 0f,
            FadeSeconds = 0.25f,
            HoldSeconds = 0.1f,
        };

    [Fact]
    public void Takeover_WaitsForTheDrawnFade()
    {
        var t = Takeover();
        t.DrawnFade = 0.5f;
        for (int i = 0; i < 50; i++)
        {
            t.Update(true, 0.01f);
            Assert.False(t.SwitchedThisFrame);
        }
        Assert.Equal(1f, t.Fade);
        Assert.False(t.Detached);
        t.DrawnFade = 1f;
        t.Update(true, 0.01f);
        Assert.True(t.SwitchedThisFrame);
        Assert.True(t.Detached);
    }

    [Fact]
    public void Takeover_StuckDrawnFade_SwitchesAfterTheTimeout()
    {
        var t = Takeover();
        t.DrawnFade = 0f;
        bool switched = false;
        float s = 0f;
        for (; s < 5f && !switched; s += 0.01f)
        {
            t.Update(true, 0.01f);
            switched = t.SwitchedThisFrame;
        }
        Assert.True(switched);
        Assert.InRange(s, FadeCut.MaxWaitSeconds, FadeCut.MaxWaitSeconds + 0.4f);
    }

    [Fact]
    public void Blink_WaitsForTheDrawnFade()
    {
        var t = Takeover();
        t.DrawnFade = 0.3f;
        t.Blink();
        for (int i = 0; i < 40; i++)
        {
            t.Update(false, 0.01f);
            Assert.False(t.SwitchedThisFrame);
        }
        t.DrawnFade = 1f;
        t.Update(false, 0.01f);
        Assert.True(t.SwitchedThisFrame);
    }

    [Fact]
    public void RecenterFade_WaitsForTheDrawnFade()
    {
        var f = new RecenterFade { FadeTime = 0.2f, DrawnFade = 0.4f };
        Assert.False(f.Update(true, 0.1f));
        Assert.False(f.Update(true, 0.3f));
        Assert.Equal(1f, f.Target);
        f.DrawnFade = 1f;
        Assert.True(f.Update(true, 0.01f));
    }

    [Fact]
    public void RecenterFade_StuckDrawnFade_SnapsAfterTheTimeout()
    {
        var f = new RecenterFade { FadeTime = 0.2f, DrawnFade = 0f };
        f.Update(true, 0.01f);
        Assert.False(f.Update(true, FadeCut.MaxWaitSeconds - 0.1f));
        Assert.True(f.Update(true, 0.2f));
    }

    [Fact]
    public void Holds_StrongestWins_AndClearDrops()
    {
        var h = new FadeHolds();
        var a = new object();
        var b = new object();
        h.Set(a, 0.4f);
        h.Set(b, 1f);
        Assert.Equal(1f, h.Target(0f));
        h.Clear(b);
        Assert.Equal(0.4f, h.Target(0f));
        h.Clear(a);
        Assert.Equal(0f, h.Target(0f));
    }

    [Fact]
    public void Holds_ATimedHoldLapsesOnItsOwn()
    {
        // A holder that never clears (an emote replaced mid-fade) must not leave the headset black.
        var h = new FadeHolds();
        var stuck = new object();
        h.Set(stuck, 1f, until: 3f);
        Assert.Equal(1f, h.Target(2.9f));
        Assert.Equal(0f, h.Target(3.1f));
        h.Set(stuck, 1f, until: 6f);
        Assert.Equal(1f, h.Target(4f));
    }
}
