namespace PeakXR.Core.Tests;

public class GuideTests
{
    public static TheoryData<GuideTopic> Topics()
    {
        var data = new TheoryData<GuideTopic>();
        for (int i = 0; i < (int)GuideTopic.Count; i++)
            data.Add((GuideTopic)i);
        return data;
    }

    [Theory]
    [MemberData(nameof(Topics))]
    public void EveryTopic_HasAShortNamedLoopingDemo(GuideTopic topic)
    {
        var demo = GuideTable.Get(topic);
        Assert.Equal(topic, demo.Topic);
        Assert.False(string.IsNullOrEmpty(demo.Name));
        Assert.InRange(demo.Name.Length, 1, 24);
        Assert.InRange(demo.Caption.Length, 1, 60);
        Assert.InRange(demo.Loop, 1.5f, 5f);
        Assert.True(demo.Left != null || demo.Right != null, "a demo shows at least one hand");
    }

    [Fact]
    public void Recenter_ShownOnBothSticks_TheChordEveryControllerHas()
    {
        var demo = GuideTable.Get(GuideTopic.PauseRecenter);
        Assert.Contains(demo.Left.Keys, k => k.Button == GuideButton.StickClick);
        Assert.Contains(demo.Right.Keys, k => k.Button == GuideButton.StickClick);
        Assert.Contains("both sticks", demo.Caption, System.StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Topics))]
    public void EveryTrack_IsSortedAndInsideTheLoop(GuideTopic topic)
    {
        var demo = GuideTable.Get(topic);
        foreach (var track in new[] { demo.Body, demo.Left, demo.Right })
            AssertTrack(track, demo.Loop);
        foreach (var prop in demo.Props)
            AssertTrack(prop.Track, demo.Loop);
        foreach (var sound in demo.Sounds)
            Assert.InRange(sound.Time, 0f, demo.Loop);
    }

    static void AssertTrack(GuideTrack track, float loop)
    {
        if (track == null)
            return;
        Assert.NotEmpty(track.Keys);
        for (int i = 0; i < track.Keys.Length; i++)
        {
            Assert.InRange(track.Keys[i].Time, 0f, loop);
            if (i > 0)
                Assert.True(track.Keys[i].Time >= track.Keys[i - 1].Time, "keys out of order");
        }
    }

    [Fact]
    public void Topics_TheTutorialMustCover_AreThere()
    {
        // The VR mechanics PeakXR adds; the list in docs/controls.md "VR Guide".
        foreach (
            var t in new[]
            {
                GuideTopic.Grab,
                GuideTopic.TwoHand,
                GuideTopic.Throw,
                GuideTopic.Eat,
                GuideTopic.Holster,
                GuideTopic.Backpack,
                GuideTopic.Climb,
                GuideTopic.Rope,
                GuideTopic.Piton,
                GuideTopic.Cook,
                GuideTopic.Light,
                GuideTopic.Lid,
                GuideTopic.Bugle,
                GuideTopic.Bandage,
                GuideTopic.Parasol,
                GuideTopic.Turn,
                GuideTopic.Move,
                GuideTopic.Crouch,
                GuideTopic.Emote,
                GuideTopic.PauseRecenter,
                GuideTopic.Laser,
                GuideTopic.Wrist,
                GuideTopic.Refused,
                GuideTopic.Ping,
                GuideTopic.Mic,
                GuideTopic.Mute,
                GuideTopic.Spectate,
            }
        )
            Assert.Equal(t, GuideTable.Get(t).Topic);
    }

    [Theory]
    [InlineData(GuideTopic.Ping, CueSound.Notice)]
    [InlineData(GuideTopic.Mic, CueSound.Transmit)]
    [InlineData(GuideTopic.Mute, CueSound.Cancel)]
    [InlineData(GuideTopic.Spectate, CueSound.Notice)]
    public void CommsDemos_PlayTheCueTheGameUses(GuideTopic topic, CueSound sound)
    {
        var demo = GuideTable.Get(topic);
        Assert.Contains(demo.Sounds, s => s.Sound == sound);
    }

    [Fact]
    public void MicDemo_ClicksOnOpenAndClose_AtTheHand()
    {
        var demo = GuideTable.Get(GuideTopic.Mic);
        Assert.Equal(2, Array.FindAll(demo.Sounds, s => s.Sound == CueSound.Transmit).Length);
        Assert.All(demo.Sounds, s => Assert.NotEqual(GuideAnchor.Free, s.Anchor));
    }

    [Fact]
    public void PingAndSpectateDemos_UseTheirControls()
    {
        bool Pressed(GuideTrack track, System.Func<GuideKey, bool> test) =>
            track != null && Array.Exists(track.Keys, k => test(k));
        var ping = GuideTable.Get(GuideTopic.Ping);
        Assert.True(
            Pressed(ping.Right, k => k.Grip > 0.5f && k.Trigger > 0.5f),
            "finger gun squeeze"
        );
        var spectate = GuideTable.Get(GuideTopic.Spectate);
        Assert.True(Pressed(spectate.Right, k => k.Stick.Y > 0.5f), "stick flick up");
        Assert.True(Pressed(spectate.Right, k => k.Stick.Y < -0.5f), "stick flick down");
    }

    [Fact]
    public void Sample_EasesBetweenKeys_AndHoldsAtTheEnds()
    {
        var track = new GuideTrack(
            GuideTable.K(1f, new Vector3(0f, 0f, 0f), grip: 0f),
            GuideTable.K(2f, new Vector3(1f, 0f, 0f), grip: 1f, button: GuideButton.Upper)
        );
        TestUtil.Near(Vector3.Zero, track.Sample(0f).Pos);
        TestUtil.Near(new Vector3(0.5f, 0f, 0f), track.Sample(1.5f).Pos);
        // Smoothstep: a quarter of the way in time is less than a quarter of the way in space.
        Assert.True(track.Sample(1.25f).Pos.X < 0.25f);
        TestUtil.Near(1f, track.Sample(3f).Grip);
        // A button belongs to the key that starts the span: held only from its own key on.
        Assert.Equal(GuideButton.None, track.Sample(1.9f).Button);
        Assert.Equal(GuideButton.Upper, track.Sample(2f).Button);
    }

    [Fact]
    public void AnchoredProp_FollowsTheHand_ThenFliesFree()
    {
        var hand = new GuideTrack(
            GuideTable.K(0f, new Vector3(0f, 0f, 0f)),
            GuideTable.K(1f, new Vector3(0f, 0f, 1f))
        );
        var prop = new GuideProp(
            GuideShape.Ball,
            0.1f,
            new GuideTrack(
                GuideTable.K(0f, Vector3.Zero, anchor: GuideAnchor.Right),
                GuideTable.K(1f, Vector3.Zero, anchor: GuideAnchor.Right),
                GuideTable.K(2f, new Vector3(0f, 0f, 3f))
            )
        );
        var demo = new GuideDemo(
            GuideTopic.Throw,
            "Throw",
            "Swing and open",
            2f,
            null,
            null,
            hand,
            new[] { prop },
            Array.Empty<GuideSound>()
        );
        TestUtil.Near(new Vector3(0f, 0f, 0.5f), demo.PropPosition(0, 0.5f));
        TestUtil.Near(new Vector3(0f, 0f, 2f), demo.PropPosition(0, 1.5f));
        TestUtil.Near(new Vector3(0f, 0f, 3f), demo.PropPosition(0, 2f));
    }

    [Fact]
    public void Hands_MoveWithTheBody_AndTurnWithIt()
    {
        var body = new GuideTrack(GuideTable.K(0f, new Vector3(0f, 1f, 0f), yaw: 90f));
        var hand = new GuideTrack(GuideTable.K(0f, new Vector3(0f, 0f, 1f)));
        var demo = new GuideDemo(
            GuideTopic.Turn,
            "Turn",
            "Flick",
            2f,
            body,
            null,
            hand,
            Array.Empty<GuideProp>(),
            Array.Empty<GuideSound>()
        );
        // Yaw turns clockwise seen from above, like Unity's Y rotation: forward becomes right.
        TestUtil.Near(new Vector3(1f, 1f, 0f), demo.HandPosition(false, 0f), 1e-3f);
    }

    [Theory]
    [InlineData(0.4f, 0.6f, 0.5f, true)]
    [InlineData(0.5f, 0.6f, 0.5f, false)]
    [InlineData(0.4f, 0.5f, 0.5f, true)]
    [InlineData(2.9f, 0.1f, 0.05f, true)] // wrapped past the loop end
    [InlineData(2.9f, 0.1f, 2.95f, true)]
    [InlineData(2.9f, 0.1f, 1f, false)]
    [InlineData(0.4f, 0.4f, 0.4f, false)]
    public void Crossed_FiresOncePerLoop(float prev, float now, float at, bool expected) =>
        Assert.Equal(expected, GuideDemo.Crossed(prev, now, at));

    [Fact]
    public void Prop_IsVisibleOnlyInsideItsWindow()
    {
        var always = new GuideProp(
            GuideShape.Ball,
            0.1f,
            new GuideTrack(GuideTable.K(0f, Vector3.Zero))
        );
        Assert.True(always.VisibleAt(0f) && always.VisibleAt(9f));
        var window = new GuideProp(
            GuideShape.Target,
            0.1f,
            new GuideTrack(GuideTable.K(0f, Vector3.Zero)),
            from: 1f,
            to: 2f
        );
        Assert.False(window.VisibleAt(0.5f));
        Assert.True(window.VisibleAt(1f));
        Assert.False(window.VisibleAt(2f));
    }

    /// <summary>Review row 12: the mechanics added since the guide was written, and the co-op verbs and tool gestures.</summary>
    public static TheoryData<GuideTopic, CueSound> NewTopics() =>
        new()
        {
            { GuideTopic.Socket, CueSound.WouldWork },
            { GuideTopic.ReleaseDrop, CueSound.Notice },
            { GuideTopic.EdgeRing, CueSound.Refused },
            { GuideTopic.FirstLabel, CueSound.Refused },
            { GuideTopic.RopePump, CueSound.WouldWork },
            { GuideTopic.Chest, CueSound.Refused },
            { GuideTopic.GroundPickup, CueSound.WouldWork },
            { GuideTopic.HelpUp, CueSound.WouldWork },
            { GuideTopic.Carry, CueSound.WouldWork },
            { GuideTopic.Feed, CueSound.WouldWork },
            { GuideTopic.Struggle, CueSound.WouldWork },
            { GuideTopic.Hammer, CueSound.WouldWork },
            { GuideTopic.Glider, CueSound.WouldWork },
            { GuideTopic.GiveUp, CueSound.Notice },
        };

    [Theory]
    [MemberData(nameof(NewTopics))]
    public void NewTopics_AreThere_AndPlayTheirCue(GuideTopic topic, CueSound sound)
    {
        var demo = GuideTable.Get(topic);
        Assert.Equal(topic, demo.Topic);
        Assert.Contains(demo.Sounds, s => s.Sound == sound);
    }

    [Fact]
    public void EatDemo_TheTriggerAloneIsRefused_TheMouthEats()
    {
        var eat = GuideTable.Get(GuideTopic.Eat);
        Assert.Contains(eat.Sounds, s => s.Sound == CueSound.Refused);
        Assert.Contains(eat.Sounds, s => s.Sound == CueSound.WouldWork);
        Assert.Contains(eat.Right.Keys, k => k.Trigger > 0.5f);
    }

    [Fact]
    public void ChestDemo_OneHandRefused_ThenBothHandsGrip()
    {
        var chest = GuideTable.Get(GuideTopic.Chest);
        Assert.Contains(chest.Left.Keys, k => k.Grip > 0.5f);
        Assert.Contains(chest.Right.Keys, k => k.Grip > 0.5f);
    }

    [Fact]
    public void GiveUpDemo_BothGripsAndBothTriggers()
    {
        var giveUp = GuideTable.Get(GuideTopic.GiveUp);
        Assert.Contains(giveUp.Left.Keys, k => k.Grip > 0.5f && k.Trigger > 0.5f);
        Assert.Contains(giveUp.Right.Keys, k => k.Grip > 0.5f && k.Trigger > 0.5f);
    }
}
