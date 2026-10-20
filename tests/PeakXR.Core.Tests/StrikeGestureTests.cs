namespace PeakXR.Core.Tests;

public class StrikeGestureTests
{
    static StrikeGesture Make() =>
        new StrikeGesture
        {
            Radius = 0.15f,
            RearmRadius = 0.3f,
            MinSpeed = 1.5f,
        };

    [Fact]
    public void FastHitInsideRadius_AfterComingFromOutside_Strikes()
    {
        var s = Make();
        Assert.False(s.Update(0.5f, 2f));
        Assert.True(s.Update(0.1f, 2f));
    }

    [Fact]
    public void SlowTouch_DoesNotStrike()
    {
        var s = Make();
        s.Update(0.5f, 0.5f);
        Assert.False(s.Update(0.1f, 0.5f));
    }

    [Fact]
    public void StartingInside_DoesNotStrike()
    {
        var s = Make();
        Assert.False(s.Update(0.1f, 3f));
    }

    [Fact]
    public void SecondStrike_NeedsTheHandToPullBackFirst()
    {
        var s = Make();
        s.Update(0.5f, 2f);
        Assert.True(s.Update(0.1f, 2f));
        Assert.False(s.Update(0.12f, 2f));
        Assert.False(s.Update(0.2f, 2f));
        s.Update(0.35f, 0f);
        Assert.True(s.Update(0.1f, 2f));
    }

    [Fact]
    public void Count_TracksStrikesUntilReset()
    {
        var s = Make();
        s.Update(0.5f, 2f);
        s.Update(0.1f, 2f);
        s.Update(0.5f, 2f);
        s.Update(0.1f, 2f);
        Assert.Equal(2, s.Count);
        s.Reset();
        Assert.Equal(0, s.Count);
    }
}
