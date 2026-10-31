namespace PeakXR.Core.Tests;

public class ViewHeightTests
{
    const float Dt = 1f / 72f;

    /// <summary>Runs <paramref name="seconds"/> of frames and returns the output's range from <paramref name="from"/>.</summary>
    static (float Min, float Max, float Last) Run(
        ViewHeight h,
        Func<float, float> body,
        Func<float, float> ground,
        Func<float, bool> open,
        float seconds,
        float from = 0f
    )
    {
        float min = float.MaxValue,
            max = float.MinValue,
            last = 0f;
        for (float t = 0f; t < seconds; t += Dt)
        {
            last = h.Update(body(t), ground(t), open(t), Dt);
            if (t < from)
                continue;
            min = MathF.Min(min, last);
            max = MathF.Max(max, last);
        }
        return (min, max, last);
    }

    [Fact]
    public void OwnMotionOnLevelGround_KeepsTheHeight()
    {
        // Run 14, vr.45: stance walks and leans set the hip 2.31-2.41 m on the same ground; the view rode it.
        var h = new ViewHeight();
        var r = Run(
            h,
            t =>
                t < 2f ? 1.70f
                : t < 4f ? 1.74f
                : t < 6f ? 1.65f
                : 1.78f,
            _ => 0.3f,
            _ => true,
            8f,
            1f
        );
        Assert.True(r.Max - r.Min < 0.002f, $"view height moved {r.Max - r.Min:0.000} m");
        Assert.True(h.Holding);
    }

    [Fact]
    public void GroundRising_LiftsTheView()
    {
        // A stance walk up a step: the ground under the body rises 0.2 m and the view goes with it.
        var h = new ViewHeight();
        var r = Run(h, t => t < 2f ? 1.7f : 1.9f, t => t < 2f ? 0f : 0.2f, _ => true, 4f);
        TestUtil.Near(1.9f, r.Last, 1e-3f);
    }

    [Fact]
    public void ClosedGate_FollowsTheBody()
    {
        // A jump, a crouch, stick walking or a push: the view releases onto the body within a fraction of a second.
        var h = new ViewHeight();
        Run(h, _ => 1.7f, _ => 0f, _ => true, 1f);
        var r = Run(h, t => t < 0.5f ? 1.7f : 1.6f, _ => 0f, t => t < 0.5f, 2f);
        TestUtil.Near(1.6f, r.Last, 1e-3f);
    }

    [Fact]
    public void BodyFarOffTheHeldHeight_CarriesIt()
    {
        // The ground ray missed what the body stands on (a ledge's lip): the view stays within the deadband of it.
        var h = new ViewHeight();
        var r = Run(h, t => t < 1f ? 1.7f : 2.1f, _ => 0f, _ => true, 3f);
        TestUtil.Near(2.1f - ViewHeight.Deadband, r.Last, 1e-3f);
    }

    [Fact]
    public void NoGround_FollowsTheBody()
    {
        var h = new ViewHeight();
        Run(h, _ => 1.7f, _ => 0f, _ => true, 1f);
        var r = Run(h, _ => 1.6f, _ => float.NaN, _ => true, 1f);
        TestUtil.Near(1.6f, r.Last, 1e-3f);
        Assert.False(h.Holding);
    }

    [Fact]
    public void Engage_WaitsForASteadyStand()
    {
        // Standing up from a crouch the hip still rises after the gate opens; holding at once froze the view low.
        var h = new ViewHeight();
        var r = Run(
            h,
            t => 1.4f + 0.3f * MathF.Min(1f, t / (ViewHeight.EngageSeconds * 0.8f)),
            _ => 0f,
            _ => true,
            2f
        );
        TestUtil.Near(1.7f, r.Last, 0.01f);
    }
}
