using System.Collections.Generic;
using System.Linq;

namespace PeakXR.Core.Tests;

public class BodyHoldTests
{
    const float Dt = 1f / 50f;
    static readonly Vector2 Start = new(22.7f, -373.1f);

    [Fact]
    public void NotLeaning_NoForce_AnchorFollows()
    {
        var h = new BodyHold();
        Assert.Equal(Vector2.Zero, h.Update(Start, Vector2.Zero, false, true, Dt));
        Assert.Equal(
            Vector2.Zero,
            h.Update(Start + new Vector2(0.05f, 0f), Vector2.Zero, false, true, Dt)
        );
        Assert.False(h.Holding);
    }

    [Fact]
    public void Leaning_PullsTheCreepBack()
    {
        // Run 30: the body crept 0.02-0.03 m sideways during a held lean.
        var h = new BodyHold();
        h.Update(Start, Vector2.Zero, true, true, Dt);
        var a = h.Update(Start + new Vector2(0.02f, 0f), Vector2.Zero, true, true, Dt);
        Assert.True(a.X < 0f && MathF.Abs(a.Y) < 1e-6f, $"accel {a}");
        TestUtil.Near(-h.Stiffness * 0.02f, a.X, 1e-4f);
    }

    [Fact]
    public void Leaning_DampsTheCreepSpeed()
    {
        var h = new BodyHold();
        h.Update(Start, Vector2.Zero, true, true, Dt);
        var a = h.Update(Start, new Vector2(0f, 0.05f), true, true, Dt);
        Assert.True(a.Y < 0f, $"accel {a}");
    }

    [Fact]
    public void ForceIsClamped()
    {
        var h = new BodyHold();
        h.Update(Start, Vector2.Zero, true, true, Dt);
        var a = h.Update(Start + new Vector2(0.09f, 0f), new Vector2(3f, 0f), true, true, Dt);
        Assert.True(a.Length() <= h.MaxAccel + 1e-5f);
    }

    [Fact]
    public void APush_IsLetGo()
    {
        // Past MaxOffset it is a real push or a slide: re-anchor there instead of pulling the body back.
        var h = new BodyHold();
        h.Update(Start, Vector2.Zero, true, true, Dt);
        var far = Start + new Vector2(h.MaxOffset + 0.05f, 0f);
        Assert.Equal(Vector2.Zero, h.Update(far, Vector2.Zero, true, true, Dt));
        TestUtil.Near(far.X, h.Anchor.X);
    }

    [Fact]
    public void NotFree_Releases_AndReanchors()
    {
        // Stick input or the room-scale follow walks the body: never fight it, and hold the new spot after.
        var h = new BodyHold();
        h.Update(Start, Vector2.Zero, true, true, Dt);
        var walked = Start + new Vector2(0f, 0.3f);
        Assert.Equal(Vector2.Zero, h.Update(walked, new Vector2(0f, 1f), true, false, Dt));
        Assert.False(h.Holding);
        h.Update(walked, Vector2.Zero, true, true, Dt);
        TestUtil.Near(walked.Y, h.Anchor.Y);
    }

    [Fact]
    public void KeepsHoldingBriefly_AfterStandingUp()
    {
        // Run 28/30: the body crept 0.07 m while and after standing up from a lean.
        var h = new BodyHold();
        h.Update(Start, Vector2.Zero, true, true, Dt);
        for (float t = 0f; t < h.ReleaseSeconds * 0.9f; t += Dt)
            h.Update(Start, Vector2.Zero, false, true, Dt);
        Assert.True(h.Holding);
        Assert.NotEqual(
            Vector2.Zero,
            h.Update(Start + new Vector2(0.02f, 0f), Vector2.Zero, false, true, Dt)
        );
        for (float t = 0f; t < h.ReleaseSeconds; t += Dt)
            h.Update(Start, Vector2.Zero, false, true, Dt);
        Assert.False(h.Holding);
    }

    /// <summary>A body on <paramref name="h"/> under a steady contact push (m/s^2, x), 50 Hz; returns its x.</summary>
    static List<float> Creep(BodyHold h, float push, float v0, float seconds)
    {
        var x = new List<float>();
        float pos = 0f,
            vel = v0;
        for (float t = 0f; t < seconds; t += Dt)
        {
            var a = h.Update(Start + new Vector2(pos, 0f), new Vector2(vel, 0f), true, true, Dt);
            vel += (push + a.X) * Dt;
            pos += vel * Dt;
            x.Add(pos);
        }
        return x;
    }

    [Fact]
    public void SteadyCreep_HeldWithinACentimetre()
    {
        // Run 32: a back bend's contact creep held the old spring 0.064 m off (about 1.3 m/s^2).
        var x = Creep(new BodyHold(), 1.3f, 0f, 3f);
        Assert.True(x.Max(MathF.Abs) < 0.012f, $"crept {x.Max(MathF.Abs):0.000} m");
    }

    [Fact]
    public void CreepThatOutlastsTheLean_StaysHeld()
    {
        // Run 32: released 1 s after standing up, the body slid another 0.026 m: the feet the lean planted are not
        // under the hip yet, so the contact push goes on. The hold lasts until nothing pushes any more.
        var h = new BodyHold();
        h.Update(Start, Vector2.Zero, true, true, Dt);
        float pos = 0f,
            vel = 0f;
        for (float t = 0f; t < 5f; t += Dt)
        {
            var a = h.Update(Start + new Vector2(pos, 0f), new Vector2(vel, 0f), false, true, Dt);
            vel += (1.3f + a.X) * Dt;
            pos += vel * Dt;
        }
        Assert.True(h.Holding);
        Assert.True(MathF.Abs(pos) < 0.012f, $"crept {pos:0.000} m after the lean");
    }

    [Fact]
    public void ACoastingBody_IsBrakedWhereItStops_NotPulledBack()
    {
        // Run 32: the far lean's walk was still coasting at 1 m/s when the hold began; anchored there, it pulled
        // the body 0.08 m back once it stopped.
        var x = Creep(new BodyHold(), 0f, 1f, 3f);
        float peak = x.Max();
        Assert.True(
            peak - x[^1] < 0.01f,
            $"pulled back {peak - x[^1]:0.000} m from {peak:0.000} m"
        );
    }

    [Fact]
    public void NonFinite_NoForce()
    {
        var h = new BodyHold();
        h.Update(Start, Vector2.Zero, true, true, Dt);
        Assert.Equal(
            Vector2.Zero,
            h.Update(new Vector2(float.NaN, 0f), Vector2.Zero, true, true, Dt)
        );
        var a = h.Update(Start, new Vector2(float.NaN, 0f), true, true, Dt);
        Assert.True(float.IsFinite(a.X) && float.IsFinite(a.Y));
    }
}
