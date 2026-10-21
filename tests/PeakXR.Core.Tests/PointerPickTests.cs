namespace PeakXR.Core.Tests;

public class PointerPickTests
{
    private const PointerPick.Hit Blocker = PointerPick.Hit.None;
    private const PointerPick.Hit Drawn = PointerPick.Hit.Visible;
    private const PointerPick.Hit Pad = PointerPick.Hit.Usable;
    private const PointerPick.Hit Button = PointerPick.Hit.Visible | PointerPick.Hit.Usable;

    [Fact]
    public void Nothing_IsNoHit()
    {
        var p = PointerPick.Resolve(new PointerPick.Hit[4], 0);
        Assert.Equal(-1, p.Visual);
        Assert.False(p.Blocked);
    }

    [Fact]
    public void FrontDrawnGraphic_IsWhereTheBeamEnds()
    {
        var p = PointerPick.Resolve(new[] { Button, Drawn }, 2);
        Assert.Equal(0, p.Visual);
        Assert.False(p.Blocked);
    }

    [Fact]
    public void InvisibleHitPadOfAControl_Counts()
    {
        var p = PointerPick.Resolve(new[] { Pad, Drawn }, 2);
        Assert.Equal(0, p.Visual);
        Assert.False(p.Blocked);
    }

    [Fact]
    public void EmptyArea_BeamPassesThroughToWhatIsDrawnBehind()
    {
        var p = PointerPick.Resolve(new[] { Blocker, Drawn }, 2);
        Assert.Equal(1, p.Visual);
        // Text or artwork behind: nothing to click, so nothing is refused.
        Assert.False(p.Blocked);
    }

    [Fact]
    public void EmptyAreaOnly_IsAMiss()
    {
        var p = PointerPick.Resolve(new[] { Blocker, Blocker }, 2);
        Assert.Equal(-1, p.Visual);
        Assert.False(p.Blocked);
    }

    [Fact]
    public void InvisibleBlockerInFrontOfAButton_IsBlocked()
    {
        // A modal fading in: its alpha-0 graphic takes the click in vanilla, the button behind shows through.
        var p = PointerPick.Resolve(new[] { Blocker, Blocker, Button }, 3);
        Assert.Equal(2, p.Visual);
        Assert.True(p.Blocked);
        p = PointerPick.Resolve(new[] { Blocker, Pad }, 2);
        Assert.True(p.Blocked);
    }

    [Fact]
    public void OnlyTheFirstCountedEntriesAreRead()
    {
        var p = PointerPick.Resolve(new[] { Blocker, Button }, 1);
        Assert.Equal(-1, p.Visual);
        Assert.False(p.Blocked);
    }
}
