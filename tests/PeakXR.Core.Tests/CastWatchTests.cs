namespace PeakXR.Core.Tests;

public class CastWatchTests
{
    [Fact]
    public void AimedButNothingBuilt_Fails()
    {
        var w = new CastWatch();
        w.Begin();
        w.Aimed();
        Assert.Equal(DenyReason.NoPath, w.End(DenyReason.NoPath));
    }

    [Fact]
    public void AimedAndBuilt_IsQuiet()
    {
        var w = new CastWatch();
        w.Begin();
        w.Aimed();
        w.Built();
        Assert.Equal(DenyReason.None, w.End(DenyReason.NoPath));
    }

    [Fact]
    public void NeverAimed_IsQuiet()
    {
        // The game returns before any try when its aim check fails; that refusal shows while aiming.
        var w = new CastWatch();
        w.Begin();
        Assert.Equal(DenyReason.None, w.End(DenyReason.NoPath));
    }

    [Fact]
    public void SignalsOutsideACast_AreIgnored()
    {
        // WillAttach also runs every frame from Update; only the calls inside the cast count.
        var w = new CastWatch();
        w.Aimed();
        w.Begin();
        Assert.Equal(DenyReason.None, w.End(DenyReason.NoPath));
        w.Aimed();
        Assert.Equal(DenyReason.None, w.End(DenyReason.NoPath));
    }

    [Fact]
    public void Begin_ClearsTheLastCast()
    {
        var w = new CastWatch();
        w.Begin();
        w.Aimed();
        w.Built();
        w.End(DenyReason.NoPath);
        w.Begin();
        w.Aimed();
        Assert.True(w.Open);
        Assert.Equal(DenyReason.NoPath, w.End(DenyReason.NoPath));
        Assert.False(w.Open);
    }
}
