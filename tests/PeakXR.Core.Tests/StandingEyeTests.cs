namespace PeakXR.Core.Tests;

public class StandingEyeTests
{
    const float Dt = 1f / 72f;

    /// <summary>Feeds <paramref name="height"/> each frame for <paramref name="seconds"/>; true when a measure lands.</summary>
    static bool Feed(
        StandingEye eye,
        ref float t,
        float seconds,
        Func<int, float> height,
        bool steady = true
    )
    {
        bool done = false;
        int frames = (int)MathF.Round(seconds / Dt);
        for (int i = 0; i < frames; i++, t += Dt)
            done |= eye.Update(steady, height(i), t);
        return done;
    }

    [Fact]
    public void Measures_TheMedianAfterSettling()
    {
        var eye = new StandingEye();
        float t = 0f;
        // The first second is the body settling (a get-up, a landing): low and moving, never counted.
        Assert.False(Feed(eye, ref t, StandingEye.SettleSeconds, i => 1.30f + 0.002f * i));
        // Then a steady stand with a little idle sway and one outlier frame.
        Assert.True(
            Feed(
                eye,
                ref t,
                StandingEye.SampleSeconds + 0.1f,
                i => i == 5 ? 1.2f : 1.56f + 0.004f * MathF.Sin(i)
            )
        );
        Assert.True(eye.Measured);
        TestUtil.Near(1.56f, eye.Value, 0.004f);
        Assert.True(eye.Spread < StandingEye.MaxSpread);
    }

    [Fact]
    public void AnUnsteadyFrame_StartsOver()
    {
        var eye = new StandingEye();
        float t = 0f;
        Assert.False(Feed(eye, ref t, 1.5f, _ => 1.56f));
        // A step, a crouch, a slope: the run restarts and needs its full settle and sample time again.
        Assert.False(eye.Update(false, 1.56f, t));
        t += Dt;
        Assert.False(Feed(eye, ref t, 1.5f, _ => 1.56f));
        Assert.True(Feed(eye, ref t, 0.6f, _ => 1.56f));
    }

    [Fact]
    public void AStillMovingBody_IsNotMeasured()
    {
        var eye = new StandingEye();
        float t = 0f;
        // Rising 0.1 m over the sample second: the spread is too wide, so it keeps waiting.
        Assert.False(Feed(eye, ref t, 2.1f, i => 1.40f + 0.1f * MathF.Min(1f, i * Dt - 1f)));
        Assert.False(eye.Measured);
        Assert.False(eye.Update(true, float.NaN, t));
    }

    [Fact]
    public void OneMeasurePerCharacter_UntilReset()
    {
        var eye = new StandingEye();
        float t = 0f;
        Assert.True(Feed(eye, ref t, 2.1f, _ => 1.56f));
        Assert.False(Feed(eye, ref t, 3f, _ => 1.40f));
        TestUtil.Near(1.56f, eye.Value);
        eye.Reset();
        Assert.False(eye.Measured);
        Assert.True(Feed(eye, ref t, 2.1f, _ => 1.40f));
        TestUtil.Near(1.40f, eye.Value);
    }

    [Fact]
    public void Keep_HoldsTheSessionValueWithinTheDeadband()
    {
        // The first measure is taken as is; a later one within 2 cm keeps it (no scale change, no blink).
        TestUtil.Near(1.56f, StandingEye.Keep(float.NaN, 1.56f));
        TestUtil.Near(1.56f, StandingEye.Keep(1.56f, 1.575f));
        TestUtil.Near(1.56f, StandingEye.Keep(1.56f, 1.545f));
        // A real difference is taken.
        TestUtil.Near(1.50f, StandingEye.Keep(1.56f, 1.50f));
    }
}
