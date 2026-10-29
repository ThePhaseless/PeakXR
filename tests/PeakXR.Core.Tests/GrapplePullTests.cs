namespace PeakXR.Core.Tests;

public class GrapplePullTests
{
    [Fact]
    public void NoShot_CoversNothing() => Assert.False(new GrapplePull().Covers(1f, falling: true));

    [Fact]
    public void WhilePulling_CoversTheHooksOwnFall()
    {
        var g = new GrapplePull();
        g.Shot(10f);
        Assert.True(g.Covers(10.5f, falling: true));
    }

    [Fact]
    public void PullThatNeverLetsGo_StopsCoveringAfterTheCap()
    {
        var g = new GrapplePull();
        g.Shot(10f);
        Assert.False(g.Covers(10f + GrapplePull.MaxPullSeconds + 0.01f, falling: true));
    }

    [Fact]
    public void AfterLetGo_CoversTheRecovery()
    {
        var g = new GrapplePull();
        g.Shot(10f);
        g.LetGo(10.8f);
        Assert.True(g.Covers(10.8f + GrapplePull.RecoverSeconds - 0.01f, falling: false));
        Assert.False(g.Covers(10.8f + GrapplePull.RecoverSeconds + 0.01f, falling: false));
    }

    [Fact]
    public void NewFallAfterLetGo_IsNotTheHooks()
    {
        var g = new GrapplePull();
        g.Shot(10f);
        g.LetGo(10.8f);
        Assert.False(g.Covers(11f, falling: true));
    }

    [Fact]
    public void LetGoWithoutAShot_CoversNothing()
    {
        var g = new GrapplePull();
        g.LetGo(5f);
        Assert.False(g.Covers(5.1f, falling: false));
    }

    [Fact]
    public void Reset_EndsTheCover()
    {
        var g = new GrapplePull();
        g.Shot(10f);
        g.Reset();
        Assert.False(g.Covers(10.1f, falling: true));
    }

    [Theory]
    [InlineData(0f, 0.5f)]
    [InlineData(0.1f, 0.3f)]
    [InlineData(0.25f, 0f)]
    [InlineData(0.9f, 0f)]
    public void ClimbHoldWait_IsTheGamesRampToAQuarter(float control, float seconds) =>
        Assert.Equal(seconds, TraversalRefusal.ClimbHoldWait(control), 3);
}
