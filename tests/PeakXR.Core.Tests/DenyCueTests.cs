namespace PeakXR.Core.Tests;

public class DenyCueTests
{
    static readonly Vector3 Eye = new(0f, 1.6f, 0f);

    [Fact]
    public void Fade_NotHeld_MatchesTheOneShotFade()
    {
        for (float t = -0.1f; t < DenyFade.Duration + 0.2f; t += 0.05f)
            Assert.Equal(DenyFade.Alpha(t), DenyFade.Alpha(t, 0f), 4);
    }

    [Fact]
    public void Fade_StaysFullWhileTheAttemptIsHeld_ThenFadesFromRelease()
    {
        Assert.Equal(1f, DenyFade.Alpha(5f, 5f), 4);
        Assert.Equal(1f, DenyFade.Alpha(10f, 10f), 4);
        Assert.Equal(0.5f, DenyFade.Alpha(10f + DenyFade.Out * 0.5f, 10f), 3);
        Assert.Equal(0f, DenyFade.Alpha(10f + DenyFade.Out + 0.01f, 10f));
        // A short hold changes nothing: the normal hold already covers it.
        Assert.Equal(DenyFade.Alpha(1.3f), DenyFade.Alpha(1.3f, 0.2f), 4);
        // Still ramps in, never a flash.
        Assert.Equal(DenyFade.Alpha(0.05f), DenyFade.Alpha(0.05f, 0.05f), 4);
    }

    [Fact]
    public void Glyphs_EveryReasonHasAShape_PinOnlyForNotices()
    {
        for (var r = DenyReason.None + 1; r < DenyReason.Count; r++)
        {
            var g = DenyGlyph.For(r);
            bool notice = r is DenyReason.Stowed or DenyReason.Dropped;
            Assert.True(notice == (g == CueGlyph.Pin), $"{r}: {g}");
            Assert.True(Enum.IsDefined(g), r.ToString());
        }
        Assert.Equal(CueGlyph.Slash, DenyGlyph.For(DenyReason.NoWall));
        Assert.Equal(CueGlyph.Slash, DenyGlyph.For(DenyReason.NeedsTwoHands));
        Assert.Equal(CueGlyph.Slash, DenyGlyph.For(DenyReason.CantUse));
    }

    [Fact]
    public void Sizes_DenialIsReadable_HoverStaysSmaller()
    {
        Assert.True(CueLayout.DenyDegrees >= CueLayout.MinDegrees);
        Assert.True(CueLayout.HoverDegrees < CueLayout.DenyDegrees);
        float d = 2.5f;
        float r = SpatialMath.SpanMeters(d, CueLayout.DenyDegrees) * 0.5f;
        Assert.Equal(CueLayout.DenyDegrees, CueLayout.AngularDegrees(r, d), 3);
        Assert.Equal(0f, CueLayout.AngularDegrees(0.1f, 0f));
    }

    [Fact]
    public void AtHand_NearSpotsRideTheHand_FarSpotsStay()
    {
        var hand = new Vector3(0.2f, 1.2f, 0.35f);
        Assert.True(CueLayout.AtHand(hand + new Vector3(0f, 0.05f, 0.1f), hand));
        Assert.False(CueLayout.AtHand(hand + new Vector3(0f, 0f, 2f), hand));
    }

    [Theory]
    [InlineData(0.2f, 1.2f, 0.35f)]
    [InlineData(0f, 1.45f, 0.15f)] // hand right under the eye
    [InlineData(-0.3f, 0.9f, 0.1f)]
    public void HandAnchor_ClearsTheHandAndKeepsAComfortableFocus(float x, float y, float z)
    {
        var hand = new Vector3(x, y, z);
        var a = CueLayout.HandAnchor(hand, Eye);
        Assert.True(a.Y > hand.Y + 0.04f, $"anchor {a} not above hand {hand}");
        Assert.True(Vector3.Distance(a, hand) < 0.25f, $"anchor {a} strays from hand {hand}");
        Assert.True(Vector3.Distance(a, Eye) >= CueLayout.MinEyeDistance - 1e-4f);
    }

    [Fact]
    public void Readable_NeedsSizeAndToBeNearTheAction()
    {
        var action = new Vector3(0f, 1.3f, 0.4f);
        float d = Vector3.Distance(Eye, action);
        float big = SpatialMath.SpanMeters(d, CueLayout.DenyDegrees) * 0.5f;
        float tiny = SpatialMath.SpanMeters(d, 1.8f) * 0.5f;

        Assert.True(CueLayout.Readable(Eye, action, action, big, out var why), why);
        Assert.False(CueLayout.Readable(Eye, action, action, tiny, out why));
        Assert.Contains("small", why);
        // Drawn at a spot far from where the player looks for it.
        var off = Eye + new Vector3(1f, 0f, 0.1f);
        Assert.False(CueLayout.Readable(Eye, action, off, big, out why));
        Assert.Contains("off", why);
    }

    [Fact]
    public void Tint_FullStrengthIsRed_ZeroIsTheOriginal()
    {
        var white = Vector4.One;
        var full = DenyTintMath.Tint(white, 1f);
        Assert.True(full.X > 0.9f && full.Y < 0.4f && full.Z < 0.4f, full.ToString());
        Assert.Equal(white, DenyTintMath.Tint(white, 0f));
        Assert.Equal(white, DenyTintMath.Tint(white, -1f));
        Assert.Equal(full, DenyTintMath.Tint(white, 3f));
        // The base (a cooked item's darker tint) is kept, only pulled to red; alpha untouched.
        var cooked = new Vector4(0.6f, 0.5f, 0.4f, 1f);
        var t = DenyTintMath.Tint(cooked, 1f);
        Assert.Equal(cooked.X, t.X, 3);
        Assert.Equal(1f, t.W);
        var half = DenyTintMath.Tint(white, 0.5f);
        Assert.InRange(half.Y, full.Y, 1f);
    }

    [Fact]
    public void Tint_IsTinted_TellsTheRedFromTheOriginal()
    {
        var b = new Vector4(0.8f, 0.8f, 0.8f, 1f);
        Assert.True(DenyTintMath.IsTinted(b, DenyTintMath.Tint(b, 1f)));
        Assert.False(DenyTintMath.IsTinted(b, b));
        Assert.False(DenyTintMath.IsTinted(b, DenyTintMath.Tint(b, 0.2f)));
    }
}
