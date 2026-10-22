using System.Numerics;

namespace PeakXR.Core.Tests;

public class StillWindowTests
{
    const float Dt = 1f / 90f;

    static StillWindow Window() => new(0.03f, 2f, 1f);

    /// <summary>Feeds <paramref name="seconds"/> of hip positions; the time the window first reported still, or -1.</summary>
    static float Run(
        StillWindow w,
        float seconds,
        System.Func<float, Vector2> hip,
        bool bodyOk = true
    )
    {
        for (float t = 0f; t <= seconds; t += Dt)
        {
            if (w.Update(hip(t), bodyOk, t))
                return t;
        }
        return -1f;
    }

    [Fact]
    public void Peak3IdleSway_IsStill()
    {
        // PEAK 3.0 (run 45): ±2.2 cm at about 1 Hz, up to 0.1 m/s, no net travel.
        float at = Run(
            Window(),
            8f,
            t => new Vector2(0.022f * MathF.Sin(t * 2f * MathF.PI), 0.01f * MathF.Cos(t * 6f))
        );
        Assert.InRange(at, 3f, 3.2f);
    }

    [Fact]
    public void SlightlyOffBeatSway_IsStill() =>
        Assert.True(
            Run(Window(), 8f, t => new Vector2(0.022f * MathF.Sin(t * 2f * MathF.PI / 1.2f), 0f))
                > 0f
        );

    [Fact]
    public void Creep3CmPerSecond_IsNotStill() =>
        Assert.Equal(-1f, Run(Window(), 10f, t => new Vector2(0.03f * t, 0f)));

    [Fact]
    public void Walk_IsNotStill() =>
        Assert.Equal(-1f, Run(Window(), 10f, t => new Vector2(0.5f * t, 0f)));

    [Fact]
    public void LeanPushThatStops_IsStillOnlyAfterAFullQuietWindow()
    {
        // Standing 1 s, pushed 0.1 m over the next 0.5 s, then held: the window restarts after the push.
        float at = Run(Window(), 8f, t => new Vector2(Math.Clamp(t - 1f, 0f, 0.5f) * 0.2f, 0f));
        Assert.True(at >= 4f, $"still at {at}");
    }

    [Fact]
    public void BodyBusy_RestartsTheWindow()
    {
        var w = Window();
        Assert.Equal(-1f, Run(w, 4f, _ => Vector2.Zero, bodyOk: false));
        Assert.Equal(0f, w.StillFor);
    }
}
