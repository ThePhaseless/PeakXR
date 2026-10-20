namespace PeakXR.Core.Tests;

public class CameraTakeoverTests
{
    static CameraTakeover Make() =>
        new()
        {
            EnterDelay = 0.2f,
            ExitDelay = 0.5f,
            FadeSeconds = 0.25f,
            HoldSeconds = 0.1f,
        };

    static int Run(CameraTakeover t, bool want, float seconds, float dt = 0.01f)
    {
        int switches = 0;
        for (float s = 0f; s < seconds - 1e-6f; s += dt)
        {
            t.Update(want, dt);
            if (t.SwitchedThisFrame)
                switches++;
        }
        return switches;
    }

    [Fact]
    public void Steady_NoFade()
    {
        var t = Make();
        Assert.Equal(0, Run(t, false, 2f));
        Assert.False(t.Detached);
        Assert.Equal(0f, t.Fade);
    }

    [Fact]
    public void Takeover_FadesOutSwitchesAtBlackThenFadesIn()
    {
        var t = Make();
        Run(t, true, 0.19f);
        Assert.Equal(0f, t.Fade);
        Assert.False(t.Detached);

        // Past the enter delay the fade starts; the view switches only when fully black.
        bool sawSwitch = false;
        for (int i = 0; i < 40; i++)
        {
            t.Update(true, 0.01f);
            if (t.SwitchedThisFrame)
            {
                Assert.Equal(1f, t.Fade);
                Assert.True(t.Detached);
                sawSwitch = true;
            }
            else if (!sawSwitch)
            {
                Assert.False(t.Detached);
            }
        }
        Assert.True(sawSwitch);

        Run(t, true, 1f);
        Assert.True(t.Detached);
        Assert.Equal(0f, t.Fade);
    }

    [Fact]
    public void ShortGlitch_ShorterThanEnterDelay_IsIgnored()
    {
        var t = Make();
        Run(t, true, 0.1f);
        Run(t, false, 1f);
        Assert.False(t.Detached);
        Assert.Equal(0f, t.Fade);
    }

    [Fact]
    public void WantRevertsDuringFadeOut_FadesBackWithoutSwitching()
    {
        var t = Make();
        t.ExitDelay = 0.01f;
        Run(t, true, 0.25f); // committed and partly faded
        Assert.True(t.Fade > 0f && t.Fade < 1f);
        int switches = Run(t, false, 1f);
        Assert.Equal(0, switches);
        Assert.False(t.Detached);
        Assert.Equal(0f, t.Fade);
    }

    [Fact]
    public void Return_UsesExitDelay()
    {
        var t = Make();
        t.Reset(detached: true);
        Run(t, false, 0.45f);
        Assert.True(t.Detached);
        Assert.Equal(0f, t.Fade);
        Assert.Equal(1, Run(t, false, 1f));
        Assert.False(t.Detached);
    }

    [Fact]
    public void Blink_SwitchesAtBlackWithoutChangingView()
    {
        var t = Make();
        t.Blink();
        int switches = Run(t, false, 1f);
        Assert.Equal(1, switches);
        Assert.False(t.Detached);
        Assert.Equal(0f, t.Fade);
    }

    [Fact]
    public void ZeroFadeTime_SwitchesImmediately()
    {
        var t = Make();
        t.FadeSeconds = 0f;
        t.HoldSeconds = 0f;
        t.EnterDelay = 0f;
        t.Update(true, 0.01f);
        Assert.True(t.Detached);
        Assert.True(t.SwitchedThisFrame);
    }
}
