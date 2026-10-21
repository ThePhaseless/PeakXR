namespace PeakXR.Core.Tests;

public class SelfTestChecksTests
{
    static readonly Vector3 Head = new(0f, 1.6f, 0f);
    static readonly Vector3 Forward = new(0f, 0f, 1f);

    [Fact]
    public void PanelStraightAhead_IsInFront()
    {
        Assert.True(
            SelfTestChecks.InFront(
                Head,
                Forward,
                Head + new Vector3(0f, 0f, 1.5f),
                out var d,
                out var a
            )
        );
        TestUtil.Near(1.5f, d);
        TestUtil.Near(0f, a);
    }

    [Fact]
    public void PanelBehindTheHead_IsNotInFront()
    {
        Assert.False(
            SelfTestChecks.InFront(
                Head,
                Forward,
                Head - new Vector3(0f, 0f, 1.5f),
                out _,
                out var a
            )
        );
        TestUtil.Near(180f, a);
    }

    [Fact]
    public void PanelTooCloseOrTooFar_IsNotInFront()
    {
        Assert.False(
            SelfTestChecks.InFront(Head, Forward, Head + new Vector3(0f, 0f, 0.2f), out _, out _)
        );
        Assert.False(
            SelfTestChecks.InFront(Head, Forward, Head + new Vector3(0f, 0f, 5f), out _, out _)
        );
    }

    [Fact]
    public void PanelOffToTheSide_FailsPastSixtyDegrees()
    {
        // 45 degrees right: in front; 70 degrees: not.
        Assert.True(
            SelfTestChecks.InFront(Head, Forward, Head + new Vector3(1f, 0f, 1f), out _, out _)
        );
        var side = new Vector3(
            MathF.Sin(70f * MathF.PI / 180f),
            0f,
            MathF.Cos(70f * MathF.PI / 180f)
        );
        Assert.False(SelfTestChecks.InFront(Head, Forward, Head + side * 2f, out _, out var a));
        TestUtil.Near(70f, a, 1e-3f);
    }

    [Fact]
    public void PulseWatch_RecordsRiseSettleAndPeak()
    {
        var w = new PulseWatch();
        w.Sample(0f, 0f);
        w.Sample(0.4f, 0.1f);
        w.Sample(0.2f, 0.5f);
        w.Sample(0f, 1.1f);

        Assert.True(w.Rose);
        Assert.True(w.Settled);
        TestUtil.Near(0.4f, w.Peak);
        TestUtil.Near(1f, w.SettleSeconds);
    }

    [Fact]
    public void PulseWatch_NeverRising_IsNotSettled()
    {
        var w = new PulseWatch();
        w.Sample(0f, 0f);
        w.Sample(0f, 1f);

        Assert.False(w.Rose);
        Assert.False(w.Settled);
    }

    [Fact]
    public void PulseWatch_RisingAgain_WaitsForTheLastSettle()
    {
        var w = new PulseWatch();
        w.Sample(0.5f, 0f);
        w.Sample(0f, 0.5f);
        w.Sample(0.3f, 0.7f);
        Assert.False(w.Settled);
        w.Sample(0f, 2f);

        Assert.True(w.Settled);
        TestUtil.Near(2f, w.SettleSeconds);
    }

    [Fact]
    public void AngleOff_MeasuresFromTheViewDirection()
    {
        var eye = new Vector3(0f, 1.6f, 0f);
        // Looking 60 degrees down, a point 60 degrees down ahead sits on the view axis.
        var down60 = new Vector3(0f, -MathF.Sin(MathF.PI / 3f), MathF.Cos(MathF.PI / 3f));
        TestUtil.Near(0f, SelfTestChecks.AngleOff(eye, down60, eye + down60 * 0.7f), 1e-3f);
        // A point straight below is 30 degrees off it; one behind is far off.
        TestUtil.Near(
            30f,
            SelfTestChecks.AngleOff(eye, down60, eye + new Vector3(0f, -0.7f, 0f)),
            1e-3f
        );
        Assert.True(SelfTestChecks.AngleOff(eye, down60, eye + new Vector3(0f, 0f, -1f)) > 90f);
        // Degenerate input reads as out of view.
        TestUtil.Near(180f, SelfTestChecks.AngleOff(eye, down60, eye));
    }

    [Fact]
    public void WaitBudget_ScalesWithTheGameDuration()
    {
        // A slow item gets proportionally more time than a fast one, never less than the game's own duration.
        float fast = SelfTestChecks.WaitBudget(1f);
        float slow = SelfTestChecks.WaitBudget(6f);
        Assert.True(fast > 1f);
        Assert.True(slow > 6f);
        Assert.True(slow - fast > 5f);
        // The scripted lead-in adds on top; a broken (negative or NaN) duration counts as instant.
        TestUtil.Near(SelfTestChecks.WaitBudget(1f) + 2f, SelfTestChecks.WaitBudget(1f, 2f));
        TestUtil.Near(SelfTestChecks.WaitBudget(0f), SelfTestChecks.WaitBudget(-3f));
        TestUtil.Near(SelfTestChecks.WaitBudget(0f), SelfTestChecks.WaitBudget(float.NaN));
    }

    [Fact]
    public void FallSeconds_FreeFallThenTerminalSpeed()
    {
        // Under terminal speed it is free fall: 4.9 m at 9.8 m/s2 takes 1 s.
        TestUtil.Near(1f, SelfTestChecks.FallSeconds(4.9f, 9.8f, 50f), 1e-3f);
        // 430 m capped at 50 m/s: ~5.1 s to reach it (127.6 m), then ~6.05 s at 50 m/s.
        float t = SelfTestChecks.FallSeconds(430f, 9.8f, 50f);
        TestUtil.Near(50f / 9.8f + (430f - 50f * 50f / (2f * 9.8f)) / 50f, t, 1e-3f);
        Assert.True(t > MathF.Sqrt(2f * 430f / 9.8f));
        TestUtil.Near(0f, SelfTestChecks.FallSeconds(-1f, 9.8f, 50f));
    }
}
