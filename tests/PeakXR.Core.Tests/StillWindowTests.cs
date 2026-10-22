using System.Numerics;

namespace PeakXR.Core.Tests;

public class StillWindowTests
{
    const float Dt = 1f / 90f;

    /// <summary>Feeds <paramref name="seconds"/> of hip positions; true when the window reported still.</summary>
    static bool Run(
        StillWindow w,
        float seconds,
        System.Func<float, Vector2> hip,
        bool bodyOk = true
    )
    {
        for (float t = 0f; t <= seconds; t += Dt)
        {
            if (w.Update(hip(t), bodyOk, t))
                return true;
        }
        return false;
    }

    [Fact]
    public void IdleSway_AroundAPoint_IsStill()
    {
        // PEAK 3.0's idle sway: ±6 mm at up to 0.1 m/s, no net travel.
        var w = new StillWindow(0.03f, 1f);
        Assert.True(Run(w, 3f, t => new Vector2(0.006f * MathF.Sin(t * 16f), 0f)));
    }

    [Fact]
    public void SlowWalk_IsNotStill()
    {
        var w = new StillWindow(0.03f, 1f);
        Assert.False(Run(w, 5f, t => new Vector2(0.05f * t, 0f)));
    }

    [Fact]
    public void LeanPushThatStops_IsStillOnlyAfterAFullQuietWindow()
    {
        var w = new StillWindow(0.03f, 1f);
        // Pushed 0.1 m over the first 0.5 s, then held.
        float at = -1f;
        for (float t = 0f; t <= 3f; t += Dt)
        {
            if (w.Update(new Vector2(MathF.Min(t, 0.5f) * 0.2f, 0f), true, t))
            {
                at = t;
                break;
            }
        }
        Assert.True(at >= 1.45f);
    }

    [Fact]
    public void BodyBusy_RestartsTheWindow()
    {
        var w = new StillWindow(0.03f, 1f);
        Assert.False(Run(w, 3f, _ => Vector2.Zero, bodyOk: false));
        Assert.Equal(0f, w.StillFor);
    }
}
