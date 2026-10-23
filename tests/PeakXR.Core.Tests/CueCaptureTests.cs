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
        c.Feel(true, CueFeel.Contact, 0.3f, true, false, 10f);
        c.Clear();
        Assert.True(c.Quiet(true, float.MinValue, out _));
        Assert.False(c.Felt(true, CueFeel.Contact, float.MinValue, out _));
    }

    [Fact]
    public void MutedPulse_CountsAsTheHapticChannel()
    {
        // Interaction.Haptics off (D9): the pulse was asked for and withheld on purpose; the cue is still complete.
        var c = new CueCapture();
        Refuse(c, true, DenyReason.NoWall, 10f);
        c.Pulse(true, 0.32f, 0.02f, delivered: false, 10.02f, muted: true);
        Assert.True(c.Fired(true, DenyReason.NoWall, 9f, out var why), why);
        Assert.Contains("muted", why);
    }

    [Fact]
    public void Tint_IsCheckedOnlyWhenExpected_AndStaysSeenAfterItFades()
    {
        var c = new CueCapture();
        Refuse(c, false, DenyReason.CantUse, 10f);
        Assert.True(c.Fired(false, DenyReason.CantUse, 9f, tint: false, out _));
        Assert.False(c.Fired(false, DenyReason.CantUse, 9f, tint: true, out var why));
        Assert.Contains("no red preview", why);
        c.Ring(false, DenyReason.CantUse, CueGlyph.Slash, Spot, 5f, tint: true, 10.1f);
        // The preview fades before the ring does: a later untinted frame does not undo what was seen.
        c.Ring(false, DenyReason.CantUse, CueGlyph.Slash, Spot, 5f, tint: false, 10.5f);
        Assert.True(c.Fired(false, DenyReason.CantUse, 9f, tint: true, out why), why);
        Assert.False(c.Fired(false, DenyReason.CantUse, 10.2f, tint: true, out _));
    }

    [Fact]
    public void Feel_IsNotARefusal()
    {
        var c = new CueCapture();
        c.Feel(true, CueFeel.Contact, 0.3f, delivered: true, muted: false, 10f);
        // A contact tick neither completes a refusal's haptic channel nor breaks a "stay quiet" check.
        Assert.True(c.Quiet(true, 9f, out _));
        c.Reason(true, DenyReason.NoWall, false, 10f);
        c.Ring(true, DenyReason.NoWall, CueGlyph.Slash, Spot, 5f, false, 10f);
        c.Sound(CueSound.Refused, Spot, true, 10f);
        Assert.False(c.Fired(true, DenyReason.NoWall, 9f, out var why));
        Assert.Contains("no haptic", why);
    }

    [Theory]
    [InlineData(CueFeel.Progress)]
    [InlineData(CueFeel.Contact)]
    [InlineData(CueFeel.Cancel)]
    [InlineData(CueFeel.EndStop)]
    [InlineData(CueFeel.Danger)]
    public void Felt_NeedsThatFeelOnThatHand(CueFeel feel)
    {
        var c = new CueCapture();
        Assert.False(c.Felt(true, feel, 9f, out var why));
        Assert.Contains("no " + feel, why);
        c.Feel(false, feel, 0.2f, true, false, 10f);
        Assert.False(c.Felt(true, feel, 9f, out _));
        c.Feel(true, feel, 0.2f, true, false, 10f);
        Assert.True(c.Felt(true, feel, 9f, out why), why);
        Assert.False(c.Felt(true, feel, 10.5f, out _));
        c.Feel(true, feel, 0.2f, delivered: false, muted: false, 11f);
        Assert.False(c.Felt(true, feel, 10.5f, out why));
        Assert.Contains("not delivered", why);
        c.Feel(true, feel, 0.2f, delivered: false, muted: true, 12f);
        Assert.True(c.Felt(true, feel, 11.5f, out why), why);
    }

    [Fact]
    public void NewSounds_AreRecorded()
    {
        var c = new CueCapture();
        c.Sound(CueSound.Transmit, Spot, true, 10f);
        c.Sound(CueSound.Cancel, Spot, false, 11f);
        Assert.Equal(10f, c.LastSound(CueSound.Transmit).At);
        Assert.False(c.LastSound(CueSound.Cancel).Played);
    }

    [Fact]
    public void EdgeRing_IsRecordedPerHand_AndCleared()
    {
        var c = new CueCapture();
        Assert.Equal(float.NegativeInfinity, c.Hand(true).EdgeAt);
        c.Edge(true, Spot, 5f, 10f);
        Assert.Equal(10f, c.Hand(true).EdgeAt);
        Assert.Equal(Spot, c.Hand(true).EdgeCenter);
        Assert.Equal(5f, c.Hand(true).EdgeDegrees);
        Assert.Equal(float.NegativeInfinity, c.Hand(false).EdgeAt);
        c.Clear();
        Assert.Equal(float.NegativeInfinity, c.Hand(true).EdgeAt);
    }
}
