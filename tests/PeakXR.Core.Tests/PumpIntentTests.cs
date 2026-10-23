namespace PeakXR.Core.Tests;

public class PumpIntentTests
{
    const float Dt = 1f / 90f;

    /// <summary>Runs a head motion (position over time, level metres) against a swing velocity; true if the intent
    /// ever showed.</summary>
    static bool Run(Func<float, Vector3> head, Func<float, Vector3> swing, float seconds)
    {
        var intent = new PumpIntent();
        var last = head(0f);
        bool shown = false;
        for (float t = Dt; t < seconds; t += Dt)
        {
            var p = head(t);
            shown |= intent.Update((p - last) / Dt, swing(t), t, Dt);
            last = p;
        }
        return shown;
    }

    static Vector3 Still(float t) => Vector3.Zero;

    static Vector3 Sway(float amplitude, float hz, float t) =>
        new(amplitude * MathF.Sin(2f * MathF.PI * hz * t), 0f, 0f);

    [Fact]
    public void LookingAround_IsNoPump()
    {
        // Turning the head moves the tracked eye a few centimetres about the neck, in every direction.
        Assert.False(
            Run(
                t => new Vector3(0.05f * MathF.Sin(2.3f * t), 0f, 0.04f * MathF.Cos(1.7f * t)),
                Still,
                10f
            )
        );
    }

    [Fact]
    public void LeaningToGrab_IsNoPump()
    {
        // One reach forward and back: two strokes, not a rhythm.
        Assert.False(
            Run(
                t => new Vector3(0.35f * MathF.Sin(MathF.PI * MathF.Min(t, 2f) / 2f), 0f, 0f),
                Still,
                6f
            )
        );
    }

    [Fact]
    public void RhythmicLean_OnAStillRope_IsAPump() =>
        Assert.True(Run(t => Sway(0.25f, 0.5f, t), Still, 6f));

    [Fact]
    public void RhythmicLean_InPhaseWithTheSwing_IsAPump() =>
        // The swing's velocity follows the lean's.
        Assert.True(
            Run(
                t => Sway(0.25f, 0.5f, t),
                t => new Vector3(MathF.Cos(2f * MathF.PI * 0.5f * t), 0f, 0f),
                6f
            )
        );

    [Fact]
    public void RhythmicLean_AgainstTheSwing_IsNoPump() =>
        Assert.False(
            Run(
                t => Sway(0.25f, 0.5f, t),
                t => new Vector3(-MathF.Cos(2f * MathF.PI * 0.5f * t), 0f, 0f),
                6f
            )
        );

    [Fact]
    public void SlowDrift_IsNoPump() =>
        // Strokes too far apart to be a rhythm.
        Assert.False(Run(t => Sway(0.25f, 0.08f, t), Still, 20f));

    [Fact]
    public void Reset_ForgetsTheRhythm()
    {
        var intent = new PumpIntent();
        var last = Vector3.Zero;
        bool shown = false;
        for (float t = Dt; t < 6f && !shown; t += Dt)
        {
            var p = Sway(0.25f, 0.5f, t);
            shown = intent.Update((p - last) / Dt, Vector3.Zero, t, Dt);
            last = p;
        }
        Assert.True(shown);
        intent.Reset();
        Assert.False(intent.Update(Vector3.Zero, Vector3.Zero, 7f, Dt));
    }

    [Fact]
    public void ClearPump_OnAFriendsRope_RefusesOncePerHold()
    {
        var intent = new PumpIntent();
        bool refused = false;
        int refusals = 0;
        var last = Vector3.Zero;
        for (float t = Dt; t < 8f; t += Dt)
        {
            var p = Sway(0.25f, 0.5f, t);
            bool pumping = intent.Update((p - last) / Dt, Vector3.Zero, t, Dt);
            if (RopePump.RefuseOnce(false, pumping ? 1f : 0f, ref refused))
                refusals++;
            last = p;
        }
        Assert.Equal(1, refusals);
    }

    [Fact]
    public void LookingAround_OnAFriendsRope_NeverRefuses()
    {
        var intent = new PumpIntent();
        bool refused = false;
        var last = Vector3.Zero;
        for (float t = Dt; t < 10f; t += Dt)
        {
            var p = new Vector3(0.05f * MathF.Sin(2.3f * t), 0f, 0.04f * MathF.Cos(1.7f * t));
            bool pumping = intent.Update((p - last) / Dt, Vector3.Zero, t, Dt);
            Assert.False(RopePump.RefuseOnce(false, pumping ? 1f : 0f, ref refused));
            last = p;
        }
    }
}
