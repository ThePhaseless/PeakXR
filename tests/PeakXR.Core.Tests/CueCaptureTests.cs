namespace PeakXR.Core.Tests;

public class CueCaptureTests
{
    static readonly Vector3 Spot = new(0f, 1.2f, 0.5f);

    /// <summary>A full refusal on <paramref name="left"/> at <paramref name="t"/>: reason, ring, sound, pulse.</summary>
    static void Refuse(CueCapture c, bool left, DenyReason reason, float t)
    {
        c.Reason(left, reason, notice: false, t);
        c.Ring(left, reason, DenyGlyph.For(reason), Spot, CueLayout.DenyDegrees, tint: false, t);
        c.Sound(CueSound.Refused, Spot, played: true, t);
        c.Pulse(left, 0.6f, 0.05f, delivered: true, t + 0.01f);
    }

    [Fact]
    public void Fresh_NothingFired_AndQuiet()
    {
        var c = new CueCapture();
        Assert.False(c.Fired(true, DenyReason.NoWall, 0f, out _));
        Assert.True(c.Quiet(true, 0f, out _));
        Assert.True(c.Quiet(false, 0f, out _));
    }

    [Fact]
    public void AllChannelsAfterSince_Fired()
    {
        var c = new CueCapture();
        Refuse(c, true, DenyReason.NoWall, 10f);
        Assert.True(c.Fired(true, DenyReason.NoWall, 9.5f, out var why), why);
        Assert.Contains("ring", why);
    }

    [Theory]
    [InlineData("reason")]
    [InlineData("ring")]
    [InlineData("sound")]
    [InlineData("haptic")]
    public void OneChannelMissing_NotFired_AndNamesIt(string missing)
    {
        var c = new CueCapture();
        float t = 10f;
        if (missing != "reason")
            c.Reason(false, DenyReason.CantUse, false, t);
        if (missing != "ring")
            c.Ring(false, DenyReason.CantUse, CueGlyph.Slash, Spot, 5f, false, t);
        if (missing != "sound")
            c.Sound(CueSound.Refused, Spot, true, t);
        if (missing != "haptic")
            c.Pulse(false, 0.5f, 0.04f, true, t);
        Assert.False(c.Fired(false, DenyReason.CantUse, 9f, out var why));
        Assert.Contains("no " + missing, why);
    }

    [Fact]
    public void ChannelsBeforeSince_DoNotCount()
    {
        var c = new CueCapture();
        Refuse(c, true, DenyReason.NoWall, 5f);
        Assert.False(c.Fired(true, DenyReason.NoWall, 6f, out _));
        Assert.True(c.Quiet(true, 6f, out _));
    }

    [Fact]
    public void OtherHandOrOtherReason_NotFired()
    {
        var c = new CueCapture();
        Refuse(c, true, DenyReason.NoWall, 10f);
        Assert.False(c.Fired(false, DenyReason.NoWall, 9f, out _));
        Assert.False(c.Fired(true, DenyReason.NoStamina, 9f, out var why));
        Assert.Contains("NoWall", why);
    }

    [Fact]
    public void WouldWorkNoteAfterwards_KeepsTheRefusedSound()
    {
        var c = new CueCapture();
        Refuse(c, true, DenyReason.OutOfReach, 10f);
        c.Sound(CueSound.WouldWork, Vector3.Zero, true, 10.15f);
        Assert.True(c.Fired(true, DenyReason.OutOfReach, 9f, out var why), why);
    }

    [Fact]
    public void Notice_NeedsTheNoticeSound()
    {
        var c = new CueCapture();
        c.Reason(true, DenyReason.Stowed, notice: true, 10f);
        c.Ring(true, DenyReason.Stowed, CueGlyph.Pin, Spot, 5f, false, 10f);
        c.Pulse(true, 0.25f, 0.04f, true, 10f);
        c.Sound(CueSound.Refused, Spot, true, 10f);
        Assert.False(c.Fired(true, DenyReason.Stowed, 9f, out var why));
        Assert.Contains("no sound", why);
        c.Sound(CueSound.Notice, Spot, true, 10f);
        Assert.True(c.Fired(true, DenyReason.Stowed, 9f, out why), why);
    }

    [Fact]
    public void UnplayedSound_TooSmallRing_UndeliveredPulse_Fail()
    {
        var c = new CueCapture();
        Refuse(c, true, DenyReason.NoWall, 10f);
        c.Sound(CueSound.Refused, Spot, played: false, 10f);
        Assert.False(c.Fired(true, DenyReason.NoWall, 9f, out var why));
        Assert.Contains("not played", why);

        Refuse(c, true, DenyReason.NoWall, 11f);
        c.Ring(
            true,
            DenyReason.NoWall,
            CueGlyph.Slash,
            Spot,
            CueLayout.MinDegrees - 1f,
            false,
            11f
        );
        Assert.False(c.Fired(true, DenyReason.NoWall, 10.5f, out why));
        Assert.Contains("too small", why);

        Refuse(c, true, DenyReason.NoWall, 12f);
        c.Pulse(true, 0.6f, 0.05f, delivered: false, 12f);
        Assert.False(c.Fired(true, DenyReason.NoWall, 11.5f, out why));
        Assert.Contains("not delivered", why);
    }

    [Theory]
    [InlineData("reason")]
    [InlineData("ring")]
    [InlineData("haptic")]
    public void Quiet_FailsOnAnyChannelOfThatHand(string channel)
    {
        var c = new CueCapture();
        switch (channel)
        {
            case "reason":
                c.Reason(true, DenyReason.CantUse, false, 10f);
                break;
            case "ring":
                c.Ring(true, DenyReason.CantUse, CueGlyph.Slash, Spot, 5f, false, 10f);
                break;
            default:
                c.Pulse(true, 0.5f, 0.04f, true, 10f);
                break;
        }
        Assert.False(c.Quiet(true, 9f, out var why));
        Assert.Contains(channel, why);
    }

    [Fact]
    public void Quiet_IgnoresTheOtherHandAndSounds()
    {
        var c = new CueCapture();
        // Sounds have no hand; a hover cue elsewhere plays one.
        c.Sound(CueSound.Refused, Spot, true, 10f);
        c.Reason(false, DenyReason.CantUse, false, 10f);
        c.Ring(false, DenyReason.CantUse, CueGlyph.Slash, Spot, 5f, false, 10f);
        c.Pulse(false, 0.5f, 0.04f, true, 10f);
        Assert.True(c.Quiet(true, 9f, out _));
    }

    [Fact]
    public void Clear_ForgetsEverything()
    {
        var c = new CueCapture();
        Refuse(c, true, DenyReason.NoWall, 10f);
        c.Clear();
        Assert.True(c.Quiet(true, float.MinValue, out _));
    }
}
