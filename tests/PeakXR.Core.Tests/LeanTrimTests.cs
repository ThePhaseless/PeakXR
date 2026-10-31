namespace PeakXR.Core.Tests;

public class LeanTrimTests
{
    const float Dt = 1f / 72f;

    /// <summary>
    /// The drawn eyes against what the lean asks: they reach only <paramref name="gain"/> of the asked offset (run 12:
    /// the bend moves the animated eye 0.55 of its want and the ragdoll follows part of that), plus a fixed eye-model
    /// error, with a 0.15 s lag. Returns the miss (goal minus drawn eyes) after <paramref name="seconds"/>.
    /// </summary>
    static Vector3 Run(
        LeanTrim trim,
        Func<float, Vector3> want,
        float seconds,
        float gain = 0.4f,
        Vector3 bias = default,
        bool open = true
    )
    {
        var drawn = Vector3.Zero;
        var miss = Vector3.Zero;
        for (float t = 0f; t < seconds; t += Dt)
        {
            var w = want(t);
            var aim = gain * (w + trim.Offset) + bias;
            drawn += (aim - drawn) * (Dt / 0.15f);
            miss = w - drawn;
            trim.Update(w, miss, open, Dt);
        }
        return miss;
    }

    [Theory]
    [InlineData(0.4f)]
    [InlineData(1f)]
    public void HeldLeftover_DrawnEyesReachTheGoal(float gain)
    {
        var trim = new LeanTrim();
        var miss = Run(
            trim,
            _ => new Vector3(0f, 0f, 0.13f),
            2f,
            gain,
            new Vector3(0f, 0f, -0.03f)
        );
        Assert.True(miss.Length() <= trim.Deadband + 0.005f, $"miss {miss.Length():0.000}");
    }

    [Fact]
    public void StillStanding_EyeModelErrorIsTrimmed()
    {
        // Run 10: the renderers sat 0.02-0.045 m behind the model with nothing owed.
        var trim = new LeanTrim();
        var miss = Run(trim, _ => Vector3.Zero, 2.5f, bias: new Vector3(0f, 0f, -0.045f));
        Assert.True(miss.Length() <= trim.Deadband + 0.005f, $"miss {miss.Length():0.000}");
    }

    [Fact]
    public void WalkingHead_IsNotTrimmed()
    {
        // vr.396: the head walks on at 0.6 m/s; the stance walk is the one to close that.
        var trim = new LeanTrim();
        Run(trim, t => new Vector3(0f, 0f, 0.6f * t), 1.5f);
        Assert.Equal(Vector3.Zero, trim.Offset);
    }

    [Fact]
    public void SmallMiss_InsideTheDeadband_BendsNothing()
    {
        var trim = new LeanTrim();
        Run(trim, _ => Vector3.Zero, 2f, bias: new Vector3(0.012f, 0f, 0f));
        Assert.Equal(Vector3.Zero, trim.Offset);
    }

    [Fact]
    public void ClosedGate_LetsTheTrimGo()
    {
        var trim = new LeanTrim();
        Run(trim, _ => new Vector3(0f, 0f, 0.13f), 2f);
        Assert.True(trim.Offset.Length() > 0.05f);
        Run(trim, _ => new Vector3(0f, 0f, 0.13f), 2f, open: false);
        Assert.True(trim.Offset.Length() < 0.005f, $"offset {trim.Offset.Length():0.000}");
    }

    [Fact]
    public void Trim_IsCapped_AndLevel()
    {
        var trim = new LeanTrim();
        // A body that cannot bend at all (gain 0): the trim runs to its cap and no further.
        Run(trim, _ => new Vector3(0f, 0.1f, 0.5f), 10f, gain: 0f);
        Assert.True(trim.Offset.Length() <= trim.Max + 1e-4f);
        Assert.Equal(0f, trim.Offset.Y);
    }

    [Fact]
    public void AGateThatReopens_WaitsItsHoldAgain()
    {
        // Run-12 review: a closed gate's zero want read as held, so a reopening trim integrated at once.
        var trim = new LeanTrim();
        for (int i = 0; i < 72; i++)
            trim.Update(default, default, false, Dt);
        trim.Update(Vector3.Zero, new Vector3(0.1f, 0f, 0f), true, Dt);
        Assert.Equal(Vector3.Zero, trim.Offset);
    }
}
