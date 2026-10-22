namespace PeakXR.Core.Tests;

public class ChaseBlockTests
{
    static ChaseBlock Make() => new() { BlockSeconds = 0.2f, BlinkInterval = 2f };

    /// <summary>Runs frames; returns how many asked for a blink.</summary>
    static int Run(ChaseBlock b, bool blocked, bool trailing, float seconds, float dt = 0.01f)
    {
        int blinks = 0;
        for (float s = 0f; s < seconds - 1e-6f; s += dt)
            if (b.Update(dt, blocked, trailing))
                blinks++;
        return blinks;
    }

    [Fact]
    public void ShortBlock_IsIgnored()
    {
        var b = Make();
        Assert.Equal(0, Run(b, true, true, 0.15f));
        Assert.False(b.Blocked);
        Run(b, false, true, 0.1f);
        Assert.False(b.Blocked);
    }

    [Fact]
    public void LongBlock_BlocksAndBlinksOnce()
    {
        var b = Make();
        Assert.Equal(1, Run(b, true, true, 1f));
        Assert.True(b.Blocked);
    }

    [Fact]
    public void AnchorOnTheGoal_NoBlink()
    {
        var b = Make();
        Assert.Equal(0, Run(b, true, false, 1f));
        Assert.True(b.Blocked);
    }

    [Fact]
    public void RepeatedBlocks_BlinkAtMostOncePerInterval()
    {
        var b = Make();
        int blinks = 0;
        for (int i = 0; i < 4; i++)
        {
            blinks += Run(b, true, true, 0.3f);
            blinks += Run(b, false, true, 0.2f);
        }
        Assert.Equal(1, blinks);
        blinks += Run(b, false, true, 1f);
        blinks += Run(b, true, true, 0.3f);
        Assert.Equal(2, blinks);
    }

    [Fact]
    public void Clear_EndsTheBlockAtOnce()
    {
        var b = Make();
        Run(b, true, true, 0.5f);
        b.Update(0.01f, false, true);
        Assert.False(b.Blocked);
    }
}
