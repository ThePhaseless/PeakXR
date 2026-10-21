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
}
