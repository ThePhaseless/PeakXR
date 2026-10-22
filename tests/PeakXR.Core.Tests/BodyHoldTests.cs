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
        var a = h.Update(Start + new Vector2(0.01f, 0f), Vector2.Zero, true, true, Dt);
        Assert.True(a.X < 0f && MathF.Abs(a.Y) < 1e-6f, $"accel {a}");
        TestUtil.Near(-h.Stiffness * 0.01f, a.X, 1e-4f);
    }

    [Fact]
    public void Leaning_DampsTheCreepSpeed()
    {
        var h = new BodyHold();
        h.Update(Start, Vector2.Zero, true, true, Dt);
        var a = h.Update(Start, new Vector2(0f, 0.03f), true, true, Dt);
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
        // Past MaxOffset it is a real push or a slide: let go for the rest of this lean, never re-anchor and pull
        // again (run 32: re-anchoring under a steady push made a sawtooth the body rocked on).
        var h = new BodyHold();
        h.Update(Start, Vector2.Zero, true, true, Dt);
        var far = Start + new Vector2(h.MaxOffset + 0.05f, 0f);
        Assert.Equal(Vector2.Zero, h.Update(far, Vector2.Zero, true, true, Dt));
        for (int i = 0; i < 100; i++)
            Assert.Equal(
                Vector2.Zero,
                h.Update(far + new Vector2(0.02f, 0f), Vector2.Zero, true, true, Dt)
            );
        Assert.False(h.Holding);
    }

    /// <summary>
    /// The standing body with the tracked head held off the avatar's eyes (after a warp or turn without a recenter),
    /// at 50 Hz: vanilla drag (0.85 per 1/60 s, CharacterMovement.cs:48), the lean's contact creep pulling the body
    /// toward a displaced rest point (0.2 of the lean, 20/s^2, built up over 0.3 s; run 28's 0.06 m at a 0.3 m lean,
    /// run 32's 1.3 m/s^2), and room-scale absorbing travel toward the head, which shrinks the lean (want) itself.
    /// Returns the body's speed per tick.
    /// </summary>
    static List<float> OffCentreHead(float head, bool absorbed, float seconds)
    {
        var h = new BodyHold();
        var speeds = new List<float>();
        float x = 0f,
            v = 0f,
            f = 0f;
        float drag = MathF.Log(0.85f) * 60f;
        for (float t = 0f; t < seconds; t += Dt)
        {
            float want = absorbed
                ? head - Math.Clamp(x, MathF.Min(0f, head), MathF.Max(0f, head))
                : head;
            bool leaning = MathF.Abs(want) >= 0.017f;
            var a = h.Update(Start + new Vector2(x, 0f), new Vector2(v, 0f), leaning, true, Dt);
            f += (20f * (0.2f * want - x) - f) * (Dt / 0.3f);
            v += (f + a.X) * Dt;
            v *= MathF.Exp(drag * Dt);
            x += v * Dt;
            speeds.Add(MathF.Abs(v));
        }
        return speeds;
    }

    [Theory]
    [InlineData(0.05f, true)]
    [InlineData(0.15f, true)]
    [InlineData(-0.2f, true)]
    [InlineData(0.3f, true)]
    [InlineData(0.15f, false)]
    [InlineData(0.3f, false)]
    public void OffCentreHead_HeldStill_TheBodyComesToRest(float head, bool absorbed)
    {
        // Run 32 vr.26-vr.143: a standing body rocked at 0.05-0.12 m/s while the head sat off the eyes.
        var speeds = OffCentreHead(head, absorbed, 20f);
        float late = speeds.Skip(7 * 50).Take(3 * 50).Max();
        Assert.True(late < 0.02f, $"still moving {late:0.000} m/s after 7-10 s");
        // No lurch when the per-lean budget runs out, however long the head stays off.
        float after = speeds.Skip(2 * 50).Max();
        Assert.True(after < 0.02f, $"moved {after:0.000} m/s between 2 and 20 s");
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

    /// <summary>Like <see cref="Creep"/> but with the lean ending at once; returns x per tick.</summary>
    static List<float> AfterLean(BodyHold h, float push, float seconds, bool start = true)
    {
        var x = new List<float>();
        if (start)
            h.Update(Start, Vector2.Zero, true, true, Dt);
        float pos = 0f,
            vel = 0f;
        for (float t = 0f; t < seconds; t += Dt)
        {
            var a = h.Update(Start + new Vector2(pos, 0f), new Vector2(vel, 0f), false, true, Dt);
            vel += (push + a.X) * Dt;
            pos += vel * Dt;
            x.Add(pos);
        }
        return x;
    }

    [Fact]
    public void CreepThatOutlastsTheLean_HeldForABoundedTime()
    {
        // Run 32: released 1 s after standing up, the body slid another 0.026 m: the feet the lean planted were not
        // under the hip yet. The hold rides that out while its push is busy, but never past MaxAfterLean, so a
        // standing VR player is not anchored against the world indefinitely.
        var h = new BodyHold();
        var x = AfterLean(h, 1.3f, h.MaxAfterLean * 0.9f);
        Assert.True(h.Holding);
        Assert.True(MathF.Abs(x[^1]) < 0.012f, $"crept {x[^1]:0.000} m after the lean");
        AfterLean(h, 1.3f, h.MaxAfterLean * 0.2f, start: false);
        Assert.False(h.Holding);
    }

    [Fact]
    public void AnImpulse_SuspendsTheHold_AndItReanchorsAfter()
    {
        // Knockback, a shove, a bounce: the body moves as for a flat player, and the hold starts again from there.
        var h = new BodyHold();
        h.Update(Start, Vector2.Zero, true, true, Dt);
        var kicked = new Vector2(1.5f, 0f);
        Assert.Equal(Vector2.Zero, h.Update(Start, kicked, true, true, Dt));
        float pos = 0f;
        for (float t = 0f; t < h.Cooldown * 0.9f; t += Dt)
        {
            pos += 0.3f * Dt;
            Assert.Equal(
                Vector2.Zero,
                h.Update(Start + new Vector2(pos, 0f), new Vector2(0.01f, 0f), true, true, Dt)
            );
        }
        for (float t = 0f; t < h.Cooldown; t += Dt)
            h.Update(Start + new Vector2(pos, 0f), Vector2.Zero, true, true, Dt);
        Assert.True(h.Holding);
        TestUtil.Near(Start.X + pos, h.Anchor.X, 1e-4f);
    }

    [Fact]
    public void ASteadyOutsideForce_MovesTheBodyLikeAFlatPlayers()
    {
        // Wind or a tornado pull (5 m/s^2) on a leaning player: the hold must not hold them back.
        var held = Creep(new BodyHold(), 5f, 0f, 1.5f);
        float free = 0.5f * 5f * 1.5f * 1.5f;
        Assert.True(held[^1] > 0.85f * free, $"moved {held[^1]:0.00} m of {free:0.00} m");
    }

    [Fact]
    public void CorrectiveImpulse_PerLean_IsBudgeted()
    {
        // A push the lean cannot explain (just under the cap, held for ten seconds) gets no more than Budget.
        var h = new BodyHold();
        float spent = 0f,
            pos = 0f,
            vel = 0f;
        for (float t = 0f; t < 10f; t += Dt)
        {
            var a = h.Update(Start + new Vector2(pos, 0f), new Vector2(vel, 0f), true, true, Dt);
            spent += a.Length() * Dt;
            vel += (1.9f + a.X) * Dt;
            pos += vel * Dt;
        }
        Assert.True(spent <= h.Budget + 0.05f, $"spent {spent:0.00} m/s of {h.Budget:0.00}");
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
