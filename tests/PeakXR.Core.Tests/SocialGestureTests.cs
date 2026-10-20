namespace PeakXR.Core.Tests;

public class StruggleGestureTests
{
    static readonly Vector3 Up = new(0f, 2f, 0f);
    static readonly Vector3 Down = new(0f, -2f, 0f);

    [Fact]
    public void Shaking_CountsEachReversal()
    {
        var g = new StruggleGesture();
        int strokes = 0;
        for (int i = 0; i < 6; i++)
            strokes += g.Update(i % 2 == 0 ? Up : Down, true, Vector3.Zero, false, 0.2f);
        // The first fast motion only sets the reference direction.
        Assert.Equal(5, strokes);
    }

    [Fact]
    public void SteadyFastMotion_IsNotAStruggle()
    {
        var g = new StruggleGesture();
        int strokes = 0;
        for (int i = 0; i < 20; i++)
            strokes += g.Update(Up, true, Up, true, 0.05f);
        Assert.Equal(0, strokes);
    }

    [Fact]
    public void SlowShaking_IsIgnored()
    {
        var g = new StruggleGesture();
        int strokes = 0;
        for (int i = 0; i < 10; i++)
            strokes += g.Update(
                i % 2 == 0 ? Up * 0.3f : Down * 0.3f,
                true,
                Vector3.Zero,
                false,
                0.2f
            );
        Assert.Equal(0, strokes);
    }

    [Fact]
    public void Strokes_AreRateLimited()
    {
        var g = new StruggleGesture { MinInterval = 0.12f };
        int strokes = 0;
        for (int i = 0; i < 10; i++)
            strokes += g.Update(i % 2 == 0 ? Up : Down, true, Vector3.Zero, false, 0.01f);
        Assert.Equal(0, strokes);
    }

    [Fact]
    public void BothHands_CountSeparately()
    {
        var g = new StruggleGesture();
        g.Update(Up, true, Down, true, 0.2f);
        Assert.Equal(2, g.Update(Down, true, Up, true, 0.2f));
    }

    [Fact]
    public void LosingTracking_ResetsTheHand()
    {
        var g = new StruggleGesture();
        g.Update(Up, true, Vector3.Zero, false, 0.2f);
        g.Update(Vector3.Zero, false, Vector3.Zero, false, 0.2f);
        Assert.Equal(0, g.Update(Down, true, Vector3.Zero, false, 0.2f));
    }
}

public class EmoteArmsTests
{
    [Theory]
    [InlineData(EmoteArmsMode.AlwaysAnimated, "A_Scout_Emote_Wave", true)]
    [InlineData(EmoteArmsMode.AlwaysTracked, "A_Scout_Emote_BackFlip", false)]
    [InlineData(EmoteArmsMode.TrackedUnlessFullBody, "A_Scout_Emote_BackFlip", true)]
    [InlineData(EmoteArmsMode.TrackedUnlessFullBody, "a_scout_emote_backflip", true)]
    [InlineData(EmoteArmsMode.TrackedUnlessFullBody, "A_Scout_Emote_Wave", false)]
    [InlineData(EmoteArmsMode.TrackedUnlessFullBody, "", false)]
    public void SuspendArms(EmoteArmsMode mode, string emote, bool expected)
    {
        Assert.Equal(expected, EmoteArms.SuspendArms(mode, emote, EmoteArms.DefaultFullBody));
    }

    [Fact]
    public void List_TrimsEntries()
    {
        const string list = " A_Dance , A_Scout_Emote_BackFlip,";
        Assert.True(EmoteArms.SuspendArms(EmoteArmsMode.TrackedUnlessFullBody, "A_Dance", list));
        Assert.True(
            EmoteArms.SuspendArms(
                EmoteArmsMode.TrackedUnlessFullBody,
                "A_Scout_Emote_BackFlip",
                list
            )
        );
        Assert.False(EmoteArms.SuspendArms(EmoteArmsMode.TrackedUnlessFullBody, "A_Danc", list));
    }

    [Fact]
    public void ParseMode_FallsBack()
    {
        Assert.Equal(EmoteArmsMode.AlwaysTracked, EmoteArms.ParseMode("AlwaysTracked"));
        Assert.Equal(EmoteArmsMode.TrackedUnlessFullBody, EmoteArms.ParseMode("bogus"));
        Assert.Equal(EmoteArmsMode.TrackedUnlessFullBody, EmoteArms.ParseMode("7"));
    }
}
