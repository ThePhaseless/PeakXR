namespace PeakXR.Core.Tests;

public class AffordanceCueTests
{
    const int Rate = 44100;

    [Theory]
    [InlineData(CueSound.Refused)]
    [InlineData(CueSound.WouldWork)]
    [InlineData(CueSound.Notice)]
    public void Tones_AreShortQuietAndClickFree(CueSound sound)
    {
        var s = CueTones.Make(sound, Rate);
        Assert.InRange(s.Length, Rate / 20, Rate * 3 / 10);
        float peak = 0f;
        foreach (var v in s)
            peak = Math.Max(peak, Math.Abs(v));
        Assert.True(peak <= CueTones.Peak + 1e-4f);
        Assert.True(peak > 0.05f);
        Assert.True(Math.Abs(s[0]) < 0.01f && Math.Abs(s[^1]) < 0.01f, "edges must be silent");
    }

    [Fact]
    public void Refused_Falls_WouldWork_Rises()
    {
        Assert.True(
            Pitch(CueTones.Make(CueSound.Refused, Rate), 0)
                > Pitch(CueTones.Make(CueSound.Refused, Rate), 1)
        );
        Assert.True(
            Pitch(CueTones.Make(CueSound.WouldWork, Rate), 0)
                < Pitch(CueTones.Make(CueSound.WouldWork, Rate), 1)
        );
    }

    [Fact]
    public void Glyphs_DistinguishCategories()
    {
        Assert.Equal(CueGlyph.Slash, DenyGlyph.For(DenyReason.OutOfReach));
        Assert.Equal(CueGlyph.Bar, DenyGlyph.For(DenyReason.PocketsFull));
        Assert.Equal(CueGlyph.Wait, DenyGlyph.For(DenyReason.KeepHolding));
        Assert.Equal(CueGlyph.Pin, DenyGlyph.For(DenyReason.Dropped));
    }

    [Fact]
    public void Motion_IsSlowAndSmall()
    {
        Assert.Equal(0f, CueMotion.Travel(0f), 4);
        Assert.Equal(0.5f, CueMotion.Travel(CueMotion.TravelSeconds * 1.5f), 4);
        for (float t = 0f; t < 3f; t += 0.01f)
            Assert.InRange(CueMotion.Breathe(t), 0.919f, 1.081f);
        Assert.InRange(CueMotion.Sweep(0.75f), 179f, 181f);
    }

    /// <summary>Zero crossings per sample in one half of the clip: a proxy for its pitch.</summary>
    static float Pitch(float[] s, int half)
    {
        int from = half * s.Length / 2;
        int to = from + s.Length / 2;
        int crossings = 0;
        for (int i = from + 1; i < to; i++)
        {
            if ((s[i - 1] < 0f) != (s[i] < 0f) && Math.Abs(s[i]) > 1e-4f)
                crossings++;
        }
        return crossings;
    }
}
