namespace PeakXR.Core.Tests;

public class ClimbFeelTests
{
    [Fact]
    public void CleanGrab_IsNoCatch() => Assert.Equal(0f, ClimbFeel.Catch(1f).Amplitude);

    [Fact]
    public void HardCatch_IsStrongerAndLongerThanASoftOne()
    {
        var soft = ClimbFeel.Catch(2f);
        var hard = ClimbFeel.Catch(6f);
        Assert.True(soft.Amplitude > 0f);
        Assert.True(hard.Amplitude > soft.Amplitude);
        Assert.True(hard.Seconds > soft.Seconds);
    }

    [Fact]
    public void Catch_IsCapped()
    {
        var p = ClimbFeel.Catch(100f);
        Assert.True(p.Amplitude <= 1f);
        Assert.True(p.Seconds <= 0.5f);
    }

    static int Ticks(HoldKind kind, float slippy, float seconds)
    {
        var t = new SurfaceTexture();
        int n = 0;
        for (float now = 0f; now < seconds; now += 1f / 90f)
        {
            if (t.TryTick(kind, slippy, now, out _))
                n++;
        }
        return n;
    }

    static float Strength(HoldKind kind, float slippy)
    {
        var t = new SurfaceTexture();
        for (float now = 0f; now < 2f; now += 1f / 90f)
        {
            if (t.TryTick(kind, slippy, now, out var p))
                return p.Amplitude;
        }
        return 0f;
    }

    [Fact]
    public void PlainRock_HasNoTexture() => Assert.Equal(0, Ticks(HoldKind.Normal, 0f, 2f));

    [Fact]
    public void Slippery_IsFasterGritThanEasy() =>
        Assert.True(Ticks(HoldKind.Slippery, 0f, 2f) > Ticks(HoldKind.Easy, 0f, 2f));

    [Fact]
    public void Easy_IsSmoothAndFaint()
    {
        Assert.True(Ticks(HoldKind.Easy, 0f, 2f) > 0);
        Assert.True(Strength(HoldKind.Easy, 0f) < Strength(HoldKind.Slippery, 0f));
    }

    [Fact]
    public void Hazard_PricklesHarderThanGrit() =>
        Assert.True(Strength(HoldKind.Hazard, 0f) > Strength(HoldKind.Slippery, 0f));

    [Fact]
    public void WindSlip_ShuddersOnPlainRock_StrongerWithMoreWind()
    {
        Assert.True(Ticks(HoldKind.Normal, 0.5f, 2f) > 0);
        Assert.True(Strength(HoldKind.Normal, 1f) > Strength(HoldKind.Normal, 0.2f));
    }

    [Fact]
    public void Reset_StartsOver()
    {
        var t = new SurfaceTexture();
        Assert.True(t.TryTick(HoldKind.Slippery, 0f, 5f, out _));
        Assert.False(t.TryTick(HoldKind.Slippery, 0f, 5.001f, out _));
        t.Reset();
        Assert.True(t.TryTick(HoldKind.Slippery, 0f, 5.002f, out _));
    }

    [Theory]
    [InlineData(ClimbSound.Whoosh)]
    [InlineData(ClimbSound.Whirr)]
    public void Tones_AreQuietAndStartAndEndSilent(ClimbSound sound)
    {
        var s = ClimbTones.Make(sound, 44100);
        Assert.True(s.Length > 44100 / 20);
        float peak = 0f;
        foreach (var v in s)
            peak = MathF.Max(peak, MathF.Abs(v));
        Assert.True(peak <= CueTones.Peak + 1e-4f);
        Assert.True(peak > 0.05f);
        Assert.True(MathF.Abs(s[0]) < 1e-3f);
        Assert.True(MathF.Abs(s[^1]) < 1e-2f);
    }

    [Fact]
    public void Tones_AreDeterministic() =>
        Assert.Equal(
            ClimbTones.Make(ClimbSound.Whoosh, 8000),
            ClimbTones.Make(ClimbSound.Whoosh, 8000)
        );
}
