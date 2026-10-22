using System.Collections.Generic;
using System.Linq;

namespace PeakXR.Core.Tests;

public class LeanDeadbandTests
{
    static readonly Vector3 Up = Vector3.UnitY;

    [Fact]
    public void SmallOffset_NoLean()
    {
        // MP run 9: a still head read as a want of +-0.009 m from the hip's own sway.
        TestUtil.Near(Vector3.Zero, BodyLean.Deadband(new Vector3(0.009f, 0f, -0.005f), Up));
    }

    [Fact]
    public void RealLean_KeepsItsDirection_LosesOnlyTheBand()
    {
        var want = new Vector3(0f, -0.05f, 0.3f);
        var d = BodyLean.Deadband(want, Up);
        TestUtil.Near(-0.05f, d.Y, 1e-6f);
        TestUtil.Near(0.3f - BodyLean.WantDeadband, d.Z, 1e-5f);
    }

    [Fact]
    public void Continuous_AtTheBandEdge()
    {
        float e = BodyLean.WantDeadband;
        Assert.True(BodyLean.Deadband(new Vector3(e + 1e-4f, 0f, 0f), Up).X < 2e-4f);
    }

    /// <summary>
    /// MP run 9's sway, modelled: the hip on its stance spring (1 Hz) with light damping, and the lean pushing it
    /// through the feet in proportion to a want that reads the hip's own velocity (the physics step and the rendered
    /// frame see it 0.06 s apart), the loop that kept a still body swaying at 0.04-0.15 m/s. Returns hip speeds.
    /// </summary>
    static List<float> Sway(float band, float seconds)
    {
        const float dt = 1f / 50f;
        float x = 0.006f,
            v = 0.1f;
        var speeds = new List<float>();
        for (float t = 0f; t < seconds; t += dt)
        {
            var want = new Vector3(0.06f * v, 0f, 0f);
            var used = band > 0f ? BodyLean.Deadband(want, Up) : want;
            float a = -40f * x - 2f * v + 50f * used.X;
            v += a * dt;
            x += v * dt;
            speeds.Add(MathF.Abs(v));
        }
        return speeds;
    }

    [Fact]
    public void StaticHead_TheSwayDies()
    {
        // Without the band the model keeps swaying (the bug); with it a still head gives a still body.
        Assert.True(
            Sway(0f, 10f).Skip(400).Max() > 0.05f,
            "the model should sway without the band"
        );
        float late = Sway(BodyLean.WantDeadband, 10f).Skip(400).Max();
        Assert.True(late < 0.01f, $"hip still at {late:0.000} m/s after 8 s");
    }
}
