namespace PeakXR.Core.Tests;

public class InjectedPressTests
{
    const float Dt = 1f / 90f;

    [Fact]
    public void Idle_EmitsNothing()
    {
        var p = new InjectedPress();
        var f = p.Update(false, false, Dt);
        Assert.False(f.Pressed);
        Assert.False(f.Held);
        Assert.False(f.Released);
        Assert.False(p.Active);
    }

    [Fact]
    public void FirstFrame_PressesAndHolds()
    {
        var p = new InjectedPress();
        p.Begin(1f);
        var f = p.Update(false, false, Dt);
        Assert.True(f.Pressed);
        Assert.True(f.Held);
        Assert.False(f.Released);
        Assert.True(p.Active);
    }

    [Fact]
    public void HoldsUntilDone_ThenReleasesOnce()
    {
        var p = new InjectedPress();
        p.Begin(5f);
        p.Update(false, false, Dt);
        var held = p.Update(false, false, Dt);
        Assert.False(held.Pressed);
        Assert.True(held.Held);
        Assert.False(held.Released);

        var end = p.Update(false, true, Dt);
        Assert.False(end.Held);
        Assert.True(end.Released);
        Assert.False(p.Active);

        var after = p.Update(false, false, Dt);
        Assert.False(after.Released);
        Assert.False(after.Held);
    }

    [Fact]
    public void ReleasesAfterMaxSeconds()
    {
        var p = new InjectedPress();
        p.Begin(0.5f);
        p.Update(false, false, Dt);
        bool released = false;
        int frames = 0;
        while (p.Active && frames < 200)
        {
            released |= p.Update(false, false, Dt).Released;
            frames++;
        }
        Assert.True(released);
        Assert.InRange(frames, 44, 47);
    }

    [Fact]
    public void UserPress_TakesOver_WithoutRelease()
    {
        var p = new InjectedPress();
        p.Begin(5f);
        p.Update(false, false, Dt);
        var f = p.Update(true, false, Dt);
        Assert.False(f.Pressed);
        Assert.False(f.Held);
        Assert.False(f.Released);
        Assert.False(p.Active);
    }

    [Fact]
    public void UserAlreadyPressing_NeverStarts()
    {
        var p = new InjectedPress();
        p.Begin(5f);
        var f = p.Update(true, false, Dt);
        Assert.False(f.Pressed);
        Assert.False(p.Active);
    }

    [Fact]
    public void DoneBeforeFirstFrame_EmitsNothing()
    {
        var p = new InjectedPress();
        p.Begin(5f);
        var f = p.Update(false, true, Dt);
        Assert.False(f.Pressed);
        Assert.False(f.Released);
        Assert.False(p.Active);
    }

    [Fact]
    public void BeginWhileActive_DoesNotRestart()
    {
        var p = new InjectedPress();
        p.Begin(5f);
        p.Update(false, false, Dt);
        p.Begin(5f);
        var f = p.Update(false, false, Dt);
        Assert.False(f.Pressed);
        Assert.True(f.Held);
    }

    [Fact]
    public void Reset_StopsSilently()
    {
        var p = new InjectedPress();
        p.Begin(5f);
        p.Update(false, false, Dt);
        p.Reset();
        Assert.False(p.Active);
        var f = p.Update(false, false, Dt);
        Assert.False(f.Released);
    }
}
