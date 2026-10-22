namespace PeakXR.Core.Tests;

/// <summary>The cue vocabulary every refusal shares: reasons, glyphs and their shapes, sounds, haptic feels.</summary>
public class AffordanceVocabularyTests
{
    const int Rate = 44100;

    [Theory]
    [InlineData(DenyReason.OutOfReach, CueGlyph.Slash)]
    [InlineData(DenyReason.NoWall, CueGlyph.Slash)]
    [InlineData(DenyReason.Overhang, CueGlyph.Slash)]
    [InlineData(DenyReason.TooFlat, CueGlyph.Slash)]
    [InlineData(DenyReason.TooSteep, CueGlyph.Slash)]
    [InlineData(DenyReason.NoStamina, CueGlyph.Stamina)]
    [InlineData(DenyReason.NeedsTwoHands, CueGlyph.TwoHands)]
    [InlineData(DenyReason.CantThrow, CueGlyph.Slash)]
    [InlineData(DenyReason.CantUse, CueGlyph.Slash)]
    [InlineData(DenyReason.UsedUp, CueGlyph.Empty)]
    [InlineData(DenyReason.CantHolster, CueGlyph.Bar)]
    [InlineData(DenyReason.PackFull, CueGlyph.Bar)]
    [InlineData(DenyReason.PocketsFull, CueGlyph.Bar)]
    [InlineData(DenyReason.HandsFull, CueGlyph.Bar)]
    [InlineData(DenyReason.WrongAngle, CueGlyph.Slash)]
    [InlineData(DenyReason.KeepHolding, CueGlyph.Wait)]
    [InlineData(DenyReason.TooSoon, CueGlyph.Wait)]
    [InlineData(DenyReason.CantSwitch, CueGlyph.Wait)]
    [InlineData(DenyReason.MovedAway, CueGlyph.Slash)]
    [InlineData(DenyReason.NotWhileClimbing, CueGlyph.Slash)]
    [InlineData(DenyReason.NeedsGround, CueGlyph.Ground)]
    [InlineData(DenyReason.AimLower, CueGlyph.ArrowDown)]
    [InlineData(DenyReason.AimHigher, CueGlyph.ArrowUp)]
    [InlineData(DenyReason.FriendsFar, CueGlyph.People)]
    [InlineData(DenyReason.StuckToHand, CueGlyph.Slash)]
    [InlineData(DenyReason.ItemWouldDrop, CueGlyph.Slash)]
    [InlineData(DenyReason.PackWouldDrop, CueGlyph.Slash)]
    [InlineData(DenyReason.NotHere, CueGlyph.Padlock)]
    [InlineData(DenyReason.Stowed, CueGlyph.Pin)]
    [InlineData(DenyReason.Dropped, CueGlyph.Pin)]
    [InlineData(DenyReason.NotLit, CueGlyph.Empty)]
    [InlineData(DenyReason.Blocked, CueGlyph.Padlock)]
    [InlineData(DenyReason.Locked, CueGlyph.Padlock)]
    [InlineData(DenyReason.Occupied, CueGlyph.Slash)]
    [InlineData(DenyReason.Unanchored, CueGlyph.Wait)]
    [InlineData(DenyReason.Carried, CueGlyph.Slash)]
    [InlineData(DenyReason.NotInTrouble, CueGlyph.Slash)]
    [InlineData(DenyReason.NotNow, CueGlyph.Wait)]
    [InlineData(DenyReason.TooHeavy, CueGlyph.Weight)]
    [InlineData(DenyReason.Spoiled, CueGlyph.Wavy)]
    [InlineData(DenyReason.CantCook, CueGlyph.Slash)]
    [InlineData(DenyReason.NeedsFriend, CueGlyph.People)]
    [InlineData(DenyReason.NoPath, CueGlyph.Slash)]
    [InlineData(DenyReason.CantClone, CueGlyph.Slash)]
    [InlineData(DenyReason.Petrified, CueGlyph.Padlock)]
    [InlineData(DenyReason.FuseLit, CueGlyph.Danger)]
    [InlineData(DenyReason.DangerSoon, CueGlyph.Danger)]
    [InlineData(DenyReason.MicAlwaysOn, CueGlyph.Slash)]
    [InlineData(DenyReason.UseOtherHand, CueGlyph.Bar)]
    [InlineData(DenyReason.Stuck, CueGlyph.Wait)]
    [InlineData(DenyReason.Revived, CueGlyph.Pin)]
    [InlineData(DenyReason.NoRevive, CueGlyph.People)]
    [InlineData(DenyReason.HappenedHere, CueGlyph.Pin)]
    public void Glyph_FollowsTheCueTable(DenyReason reason, CueGlyph glyph) =>
        Assert.Equal(glyph, DenyGlyph.For(reason));

    [Fact]
    public void NewReasons_KeepTheOldValues()
    {
        // Appended only: the self-test reports and saved logs name reasons by value.
        Assert.Equal(30, (int)DenyReason.Blocked);
        Assert.True(DenyReason.TooSteep > DenyReason.Blocked);
        Assert.Equal(DenyReason.DangerSoon + 1, DenyReason.MicAlwaysOn);
        Assert.Equal(DenyReason.HappenedHere + 1, DenyReason.Count);
    }

    [Fact]
    public void SpotSet_HoldsUpToFourInOrder_AndDropsTheRest()
    {
        var s = default(SpotSet);
        Assert.Equal(0, s.Count);
        for (int i = 0; i < 6; i++)
            s = s.And(new Vector3(i, 0f, 0f));
        Assert.Equal(SpotSet.Capacity, s.Count);
        Assert.Equal(4, SpotSet.Capacity);
        for (int i = 0; i < s.Count; i++)
            Assert.Equal(i, s[i].X);
        Assert.Equal(Vector3.Zero, s[7]);
    }

    [Theory]
    [InlineData(DenyReason.TooSteep)]
    [InlineData(DenyReason.TooHeavy)]
    public void SurfacesAndWeight_Slip(DenyReason reason) =>
        Assert.Same(DenyHaptics.Slip, DenyHaptics.For(reason));

    [Fact]
    public void EndStop_IsOneHardThunk()
    {
        Assert.InRange(CueHaptics.EndStop.Amplitude, 0.7f, 0.9f);
        Assert.InRange(CueHaptics.EndStop.Seconds, 0.015f, 0.025f);
        foreach (var p in DenyHaptics.Deny)
            Assert.True(
                CueHaptics.EndStop.Amplitude > p.Amplitude,
                "an end stop is felt harder than a refusal"
            );
    }

    [Fact]
    public void Danger_PulsesSpeedUpAndStrengthen()
    {
        Assert.True(DangerPulse.Interval(1f) < DangerPulse.Interval(0f) / 2f);
        Assert.True(DangerPulse.Amplitude(1f) > DangerPulse.Amplitude(0f));
        var d = new DangerPulse();
        Assert.True(d.TryTick(10f, 0f, out _));
        Assert.False(d.TryTick(10f + DangerPulse.Interval(0f) * 0.9f, 0f, out _));
        // Rising urgency shortens the wait already running.
        Assert.True(d.TryTick(10f + DangerPulse.Interval(1f) + 0.001f, 1f, out var hot));
        Assert.Equal(DangerPulse.Amplitude(1f), hot.Amplitude);
    }

    [Fact]
    public void EveryReason_HasADrawableGlyph()
    {
        for (var r = DenyReason.None + 1; r < DenyReason.Count; r++)
        {
            var g = DenyGlyph.For(r);
            Assert.True(Enum.IsDefined(g), $"{r}: glyph {g} undefined");
            // Wait is drawn by the runtime's sweeping arc; every other glyph needs a stroke of its own.
            int points = GlyphShape.Stroke(g, 0).Length;
            Assert.True(
                g == CueGlyph.Wait || (points >= 2 && points <= GlyphShape.MaxPoints),
                $"{r}: glyph {g} has no stroke to draw"
            );
        }
    }

    [Fact]
    public void EveryGlyph_ButWait_HasAStroke_InsideTheRing()
    {
        foreach (var g in Enum.GetValues<CueGlyph>())
        {
            var a = GlyphShape.Stroke(g, 0);
            var b = GlyphShape.Stroke(g, 1);
            Assert.True(GlyphShape.MaxPoints >= a.Length && GlyphShape.MaxPoints >= b.Length);
            if (g == CueGlyph.Wait)
            {
                // The runtime sweeps its arc; no fixed stroke.
                Assert.Empty(a);
                continue;
            }
            Assert.True(a.Length >= 2, g.ToString());
            Assert.True(b.Length == 0 || b.Length >= 2, g.ToString());
            if (g == CueGlyph.Pin)
                continue;
            foreach (var p in a)
                Assert.True(p.Length() <= 0.85f, $"{g} leaves the ring at {p}");
            foreach (var p in b)
                Assert.True(p.Length() <= 0.85f, $"{g} leaves the ring at {p}");
        }
    }

    [Fact]
    public void Glyphs_AreDifferentShapes()
    {
        var all = Enum.GetValues<CueGlyph>();
        for (int i = 0; i < all.Length; i++)
        for (int j = i + 1; j < all.Length; j++)
        {
            if (all[i] == CueGlyph.Wait || all[j] == CueGlyph.Wait)
                continue;
            bool same =
                Same(GlyphShape.Stroke(all[i], 0), GlyphShape.Stroke(all[j], 0))
                && Same(GlyphShape.Stroke(all[i], 1), GlyphShape.Stroke(all[j], 1));
            Assert.False(same, $"{all[i]} and {all[j]} draw the same");
        }
    }

    [Fact]
    public void Arrows_PointTheirWay()
    {
        // The head (stroke 1) sits at the top for up and the bottom for down.
        Assert.True(Mean(GlyphShape.Stroke(CueGlyph.ArrowUp, 1)).Y > 0f);
        Assert.True(Mean(GlyphShape.Stroke(CueGlyph.ArrowDown, 1)).Y < 0f);
    }

    [Theory]
    [InlineData(CueSound.Transmit)]
    [InlineData(CueSound.Cancel)]
    [InlineData(CueSound.EndStop)]
    [InlineData(CueSound.Danger)]
    public void NewTones_AreShortQuietAndClickFree(CueSound sound)
    {
        var s = CueTones.Make(sound, Rate);
        Assert.InRange(s.Length, Rate / 25, Rate * 3 / 10);
        float peak = 0f;
        foreach (var v in s)
            peak = Math.Max(peak, Math.Abs(v));
        Assert.True(peak <= CueTones.Peak + 1e-4f);
        Assert.True(peak > 0.05f);
        Assert.True(Math.Abs(s[0]) < 0.01f && Math.Abs(s[^1]) < 0.01f, "edges must be silent");
    }

    [Fact]
    public void EverySound_HasItsOwnTone()
    {
        var all = Enum.GetValues<CueSound>();
        for (int i = 0; i < all.Length; i++)
        for (int j = i + 1; j < all.Length; j++)
        {
            var a = CueTones.Make(all[i], Rate);
            var b = CueTones.Make(all[j], Rate);
            Assert.False(
                a.Length == b.Length && a.AsSpan().SequenceEqual(b),
                $"{all[i]} sounds like {all[j]}"
            );
        }
    }

    [Fact]
    public void Contact_IsOneTick_Cancel_IsSofter()
    {
        Assert.InRange(CueHaptics.Contact.Amplitude, 0.1f, 0.45f);
        Assert.InRange(CueHaptics.Contact.Seconds, 0.01f, 0.03f);
        Assert.True(CueHaptics.Cancel.Amplitude < CueHaptics.Contact.Amplitude);
        // Softer than any pulse of a refusal, so a cancel never reads as "no".
        foreach (var p in DenyHaptics.Deny)
            Assert.True(CueHaptics.Cancel.Amplitude < p.Amplitude);
    }

    [Fact]
    public void Progress_RampsUp_AndTicksAtItsOwnPace()
    {
        var p = new ProgressRamp();
        Assert.True(p.TryTick(10f, 0f, out var first));
        Assert.False(p.TryTick(10f + ProgressRamp.Step * 0.5f, 0.1f, out _));
        Assert.True(p.TryTick(10f + ProgressRamp.Step + 0.001f, 0.9f, out var late));
        Assert.True(late.Amplitude > first.Amplitude);
        Assert.InRange(ProgressRamp.Amplitude(-1f), 0.04f, 0.1f);
        Assert.InRange(ProgressRamp.Amplitude(2f), 0.3f, 0.45f);
        // A pause longer than a step restarts the ramp at once.
        p.Reset();
        Assert.True(p.TryTick(10.01f, 0.5f, out _));
    }

    [Fact]
    public void FlatTint_TurnsADarkBarRed_AndZeroIsTheOriginal()
    {
        var dark = new Vector4(0.1f, 0.1f, 0.1f, 0.6f);
        Assert.Equal(dark, DenyTintMath.Flat(dark, 0f));
        var red = DenyTintMath.Flat(dark, 1f);
        Assert.True(red.X > 0.8f && red.Y < 0.3f && red.Z < 0.3f, red.ToString());
        Assert.True(DenyTintMath.IsFlat(dark, red));
        Assert.False(DenyTintMath.IsFlat(dark, dark));
    }

    [Fact]
    public void StatusAlert_Kick_RaisesTheWristOnce()
    {
        var a = new StatusAlert();
        a.Update(1f, new float[4], 0.05f);
        a.Kick();
        Assert.True(a.Update(1f, new float[4], 0.05f));
        Assert.False(a.Update(1f, new float[4], 0.05f));
        Assert.True(a.Pulse > 0f);
    }

    static bool Same(Vector2[] a, Vector2[] b) => a.AsSpan().SequenceEqual(b);

    static Vector2 Mean(Vector2[] s)
    {
        var sum = Vector2.Zero;
        foreach (var p in s)
            sum += p;
        return sum / s.Length;
    }
}
