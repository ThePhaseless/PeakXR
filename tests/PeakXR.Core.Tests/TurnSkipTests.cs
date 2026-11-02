namespace PeakXR.Core.Tests;

public class TurnSkipTests
{
    const float Dt = 1f / 72f;

    // The game turns the body about its mass centre, its eyes 0.236 m ahead; a real head turns about the neck, its
    // eyes 0.1 m ahead. A turned head therefore leaves a debt of 0.136 m x (facing before - facing now).
    const float Swing = 0.236f - 0.1f;

    static Vector3 Facing(float yaw)
    {
        float r = yaw * MathF.PI / 180f;
        return new Vector3(MathF.Sin(r), 0f, MathF.Cos(r));
    }

    static Vector3 TurnDebt(float yaw) => Swing * (Facing(0f) - Facing(yaw));

    /// <summary>What the lean bends for: the share less the skip.</summary>
    static Vector3 Lean(TurnSkip s, Vector3 share, float yaw, bool walking = false) =>
        share - s.Update(share, yaw, walking, Dt);

    [Fact]
    public void Glance_TheLeanNeverBends()
    {
        // vr.302 (run 13): a 70 degree glance out and back put 0.16 m on the debt and the lean bent 9.5 degrees for it.
        var s = new TurnSkip();
        Lean(s, Vector3.Zero, 0f);
        for (float t = 0f; t < 1.5f; t += Dt)
        {
            float k =
                t < 0.35f ? t / 0.35f
                : t < 0.65f ? 1f
                : t < 1f ? 1f - (t - 0.65f) / 0.35f
                : 0f;
            float yaw = 70f * k;
            var lean = Lean(s, TurnDebt(yaw), yaw);
            Assert.True(lean.Length() < 0.01f, $"t {t:0.00}: lean {lean.Length():0.000} m");
        }
    }

    [Fact]
    public void HeldTurn_IsLeftToTheWalk_ThenTheLeanTakesTheLeftover()
    {
        // vr.45b (run 13): a head held at +90 bent the body 17 degrees before and after the walk. The walk steps under
        // the turn; only what it leaves once it is done is the lean's.
        var s = new TurnSkip();
        Lean(s, Vector3.Zero, 0f);
        for (float t = 0f; t <= 0.6f; t += Dt)
            Lean(s, TurnDebt(150f * t), 150f * t);
        var debt = TurnDebt(90f);
        for (float t = 0f; t < 1.5f; t += Dt)
            Assert.True(Lean(s, debt, 90f).Length() < 0.01f, $"delay t {t:0.00}");
        // The walk steps under most of it; the lean bends for none of it meanwhile.
        for (float t = 0f; t < 0.5f; t += Dt)
        {
            debt *= 0.9f;
            Assert.True(Lean(s, debt, 90f, walking: true).Length() < 0.01f, $"walk t {t:0.00}");
        }
        // It springs back to 0.1 m and rests: now the lean takes it.
        var left = debt + Vector3.Normalize(debt) * 0.08f;
        Vector3 lean = default;
        for (float t = 0f; t < 2.5f; t += Dt)
            lean = Lean(s, left, 90f);
        Assert.True((lean - left).Length() < 0.01f, $"lean {lean} for {left}");
    }

    [Fact]
    public void ABigWalkLeftover_TheLeanTakesOnlyMaxLeanOfIt()
    {
        // vr.45b (run 15): the walk left 0.10-0.14 m under a turned head and the lean, solved onto the eyes, bent
        // 7-12 degrees for it (want 8). The lean takes at most MaxLean of what a turn left; the body stays upright.
        var s = new TurnSkip();
        Lean(s, Vector3.Zero, 0f);
        for (float t = 0f; t <= 0.6f; t += Dt)
            Lean(s, TurnDebt(150f * t), 150f * t);
        var debt = TurnDebt(90f);
        for (float t = 0f; t < 1.5f; t += Dt)
            Lean(s, debt, 90f);
        for (float t = 0f; t < 0.3f; t += Dt)
            Lean(s, debt, 90f, walking: true);
        var left = Vector3.Normalize(debt) * 0.14f;
        Vector3 lean = default;
        for (float t = 0f; t < 3f; t += Dt)
            lean = Lean(s, left, 90f);
        TestUtil.Near(s.MaxLean, lean.Length(), 1e-3f);
        Assert.True(Vector3.Dot(lean, left) > 0f);
        // The head comes back toward the body: the leftover shrinks under MaxLean and the lean takes all of it.
        var closer = Vector3.Normalize(debt) * 0.06f;
        for (float t = 0f; t < 2f; t += Dt)
            lean = Lean(s, closer, 90f);
        TestUtil.Near(closer, lean, 1e-3f);
    }

    [Fact]
    public void ASpringBackAfterATurnsWalk_TheLeanStillTakesOnlyMaxLean()
    {
        // Run 21: a push closed the debt under MaxLean, the settle sprang back to 0.104 m (vr.45b, 8.5 degrees) and 0.186 m
        // (vr.300, 15.1 degrees), with nothing kept out of the lean.
        var s = new TurnSkip();
        Lean(s, Vector3.Zero, 0f);
        for (float t = 0f; t <= 0.6f; t += Dt)
            Lean(s, TurnDebt(150f * t), 150f * t);
        var debt = TurnDebt(90f);
        var along = Vector3.Normalize(debt);
        for (float t = 0f; t < 1.5f; t += Dt)
            Lean(s, debt, 90f);
        for (float t = 0f; t < 0.3f; t += Dt)
            Lean(s, along * 0.05f, 90f, walking: true);
        for (float t = 0f; t < 1f; t += Dt)
            Lean(s, along * MathF.Min(0.18f, 0.05f + 0.2f * t), 90f, walking: true);
        Vector3 lean = default;
        for (float t = 0f; t < 3f; t += Dt)
            lean = Lean(s, along * 0.18f, 90f);
        TestUtil.Near(s.MaxLean, lean.Length(), 1e-3f);
        // Back within MaxLean, and it is all the lean's again.
        for (float t = 0f; t < 3f; t += Dt)
            lean = Lean(s, along * 0.06f, 90f);
        TestUtil.Near(along * 0.06f, lean, 1e-3f);
    }

    [Fact]
    public void AWalkWithNoTurnBeforeIt_KeepsNothingFromTheLean()
    {
        var s = new TurnSkip();
        Lean(s, Vector3.Zero, 0f);
        var share = new Vector3(0f, 0f, 0.18f);
        for (float t = 0f; t < 1f; t += Dt)
            Lean(s, share, 0f, walking: true);
        TestUtil.Near(share, Lean(s, share, 0f), 1e-3f);
    }

    [Fact]
    public void AStaleSkip_NeverBendsTheLeanAwayFromTheView()
    {
        // vr.300 (run 13): after a snap turn the walk closed the debt to 0.015 m, but the turn's shift stayed at 0.155 m
        // in the lean's skip, so the lean bent 0.14 m away from the view (6.8 degrees) and the trim chased that.
        var s = new TurnSkip();
        Lean(s, Vector3.Zero, 0f);
        Lean(s, new Vector3(-0.09f, 0f, -0.12f), 45f);
        foreach (
            var share in new[]
            {
                new Vector3(-0.003f, 0f, -0.015f),
                new Vector3(0.05f, 0f, 0f),
                new Vector3(0f, 0f, 0.1f),
                Vector3.Zero,
            }
        )
        {
            var lean = Lean(s, share, 45f, walking: true);
            Assert.True(Vector3.Dot(lean, share) >= -1e-6f, $"{share}: lean {lean} points away");
            Assert.True(lean.Length() <= share.Length() + 1e-6f, $"{share}: lean {lean} past it");
        }
    }

    [Fact]
    public void ALeanWithNoTurn_IsAllTheLeans()
    {
        // vr.45: a head leaning 0.3 m forward with the look still bends for all of it, at once.
        var s = new TurnSkip();
        Lean(s, Vector3.Zero, 0f);
        for (float t = 0f; t <= 1f; t += Dt)
        {
            var share = new Vector3(0f, 0f, 0.3f * t);
            // Idle sway of the look stays under the turning rate.
            float yaw = 2f * MathF.Sin(t * 6f);
            Assert.True((Lean(s, share, yaw) - share).Length() < 1e-4f, $"t {t:0.00}");
        }
    }

    [Fact]
    public void AnUnwalkedTurn_GoesToTheLean_AfterAWhile()
    {
        // Under the walk's start distance no walk comes; the lean takes it once the turn has long held.
        var s = new TurnSkip();
        Lean(s, Vector3.Zero, 0f);
        Lean(s, new Vector3(0.04f, 0f, 0f), 30f);
        Assert.True(Lean(s, new Vector3(0.04f, 0f, 0f), 30f).Length() < 0.005f);
        Vector3 lean = default;
        for (float t = 0f; t < s.ReleaseAfter + 2f; t += Dt)
            lean = Lean(s, new Vector3(0.04f, 0f, 0f), 30f);
        Assert.True(lean.X > 0.035f, $"lean {lean}");
    }

    [Fact]
    public void Reset_Forgets()
    {
        var s = new TurnSkip();
        Lean(s, Vector3.Zero, 0f);
        Lean(s, new Vector3(0.2f, 0f, 0f), 90f);
        s.Reset();
        var share = new Vector3(0.2f, 0f, 0f);
        Lean(s, share, 90f);
        Assert.Equal(share, Lean(s, share, 90f));
    }

    // A look returning from below -35 degrees: the body stands behind a view that no longer wants it there, and the debt's
    // target moved with no turn to see (run 23, vr.273: 17.8-25.2 degrees looking level).
    static readonly float DeepBack = BodyStance.StepBack(-60f);
    static readonly Vector3 Forward = new(0f, 0f, 1f);

    static Vector3 LeanBack(TurnSkip s, Vector3 share, float back, bool walking = false) =>
        share - s.Update(share, 0f, walking, Dt, back);

    /// <summary>The look from -60 to level over 0.6 s, the body not stepping; asserts the lean stays out.</summary>
    static TurnSkip GiveUpDeepLook()
    {
        var s = new TurnSkip();
        LeanBack(s, Vector3.Zero, DeepBack);
        for (float t = 0f; t <= 0.6f; t += Dt)
        {
            float back = BodyStance.StepBack(-60f * (1f - t / 0.6f));
            var lean = LeanBack(s, Forward * (DeepBack - back), back);
            Assert.True(lean.Length() < 0.02f, $"look t {t:0.00}: lean {lean.Length():0.000} m");
        }
        return s;
    }

    [Fact]
    public void AStepBackGivenUp_StaysOutOfTheLean_AndAWalkClosesIt()
    {
        var s = GiveUpDeepLook();
        for (float t = 0f; t < 1.5f; t += Dt)
        {
            var lean = LeanBack(s, Forward * DeepBack, 0f);
            Assert.True(lean.Length() < 0.02f, $"wait t {t:0.00}: lean {lean.Length():0.000} m");
        }
        for (float t = 0f; t < 0.6f; t += Dt)
        {
            var debt = Forward * (DeepBack + (0.05f - DeepBack) * t / 0.6f);
            var lean = LeanBack(s, debt, 0f, walking: true);
            Assert.True(
                lean.Length() <= s.MaxLean + 0.005f,
                $"walk t {t:0.00}: lean {lean.Length():0.000} m"
            );
        }
        for (float t = 0f; t < 3f; t += Dt)
            LeanBack(s, Forward * 0.05f, 0f);
        Assert.True(s.Skip.Length() < 0.005f, $"skip {s.Skip.Length():0.000} m");
    }

    [Fact]
    public void AStepBackGivenUp_WithNoWalk_LeansAtMostMaxLean()
    {
        var s = GiveUpDeepLook();
        var lean = Vector3.Zero;
        for (float t = 0f; t < 6f; t += Dt)
            lean = LeanBack(s, Forward * DeepBack, 0f);
        Assert.InRange(lean.Length(), s.MaxLean - 0.01f, s.MaxLean + 0.005f);
    }

    [Fact]
    public void ALeanHeldThroughTheGivingUp_KeepsItsBend()
    {
        // The head stays 0.25 m forward; only the part of the share the step back gave up goes to the skip.
        const float Head = 0.25f;
        float heldBack = BodyStance.StepBack(-45f);
        var s = new TurnSkip();
        for (float t = 0f; t < 0.5f; t += Dt)
            LeanBack(s, Forward * (Head - heldBack), heldBack);
        var lean = Vector3.Zero;
        for (float t = 0f; t <= 0.6f; t += Dt)
        {
            float back = BodyStance.StepBack(-45f + 25f * t / 0.6f);
            lean = LeanBack(s, Forward * (Head - back), back);
        }
        TestUtil.Near(Head - heldBack, lean.Length(), 0.005f);
    }

    [Fact]
    public void ALeanGrowingThroughTheGivingUp_KeepsItsBend()
    {
        // The look rises from -50 to -20 over 0.5 s while the head leans 0.2 m: only what the step back gave up may go
        // into the skip, not the lean that came with it.
        const float Head = 0.2f;
        float deepBack = BodyStance.StepBack(-50f);
        var s = new TurnSkip();
        LeanBack(s, Vector3.Zero, deepBack);
        var lean = Vector3.Zero;
        for (float t = 0f; t < 1.5f; t += Dt)
        {
            float k = MathF.Min(1f, t / 0.5f);
            float back = BodyStance.StepBack(-50f + 30f * k);
            lean = LeanBack(s, Forward * (Head * k + deepBack - back), back);
        }
        TestUtil.Near(Head, lean.Length(), 0.01f);
    }

    [Fact]
    public void ALeanAtLevel_KeepsItsBend_NoBackGivenUp()
    {
        var s = new TurnSkip();
        for (float t = 0f; t < 1f; t += Dt)
        {
            s.Update(Forward * 0.2f * t, 0f, false, Dt);
            Assert.Equal(Vector3.Zero, s.Skip);
        }
    }

    [Fact]
    public void AHeldLookDownsSway_IsNotAGivingUp()
    {
        var s = new TurnSkip();
        var side = new Vector3(1f, 0f, 0f);
        for (float t = 0f; t < 3f; t += Dt)
        {
            float back = BodyStance.StepBack(-60f + 1.5f * MathF.Sin(2f * t));
            s.Update(side * 0.04f * t, 0f, false, Dt, back);
            Assert.Equal(Vector3.Zero, s.Skip);
        }
    }

    [Fact]
    public void ALookGoingDown_IsNotAGivingUp()
    {
        // The step back growing moves no body: a lean made on the way down stays the lean's.
        var s = new TurnSkip();
        var side = new Vector3(1f, 0f, 0f);
        for (float t = 0f; t <= 0.7f; t += Dt)
        {
            s.Update(side * 0.3f * t, 0f, false, Dt, BodyStance.StepBack(-60f * t / 0.7f));
            Assert.Equal(Vector3.Zero, s.Skip);
        }
    }

    [Theory]
    [InlineData(2f, 1f, 0.16f)]
    [InlineData(5f, 1f, 0.12f)]
    [InlineData(5f, 0.75f, 0.12f)]
    [InlineData(5f, 0.5f, 0.085f)]
    [InlineData(5f, 0.25f, 0.085f)]
    [InlineData(10f, 0.5f, 0.065f)]
    public void ANodAtADeepLook_KeepsTheLean(float amp, float hz, float least)
    {
        // A 0.2 m lean held at -50, looking around below: a rise adds to the skip, so a fall must take it back or every
        // nod stands the body further up. The lean cannot pass the nod's lowest share, nor MaxLean once a rise kept more.
        const float Head = 0.2f;
        float held = BodyStance.StepBack(-50f);
        var s = new TurnSkip();
        LeanBack(s, Forward * Head, held);
        for (float t = 0f; t < 20f; t += Dt)
        {
            float back = BodyStance.StepBack(-50f + amp * MathF.Sin(2f * MathF.PI * hz * t));
            var lean = LeanBack(s, Forward * (Head + held - back), back);
            if (t > 2f / hz)
                Assert.True(lean.Length() >= least, $"t {t:0.00}: lean {lean.Length():0.000} m");
        }
    }

    [Theory]
    [InlineData(5f, 1f)]
    [InlineData(5f, 0.5f)]
    public void ANodOverATurnsLeftover_KeepsItOutOfTheLean(float amp, float hz)
    {
        // The step back moves along the look, the leftover lies across it: a nod takes back none of it (run 15: 7-12 deg).
        var side = new Vector3(1f, 0f, 0f);
        float held = BodyStance.StepBack(-50f);
        var s = new TurnSkip();
        s.Update(Vector3.Zero, 0f, false, Dt, held);
        for (float t = 0f; t <= 0.6f; t += Dt)
            s.Update(
                side * 0.136f * MathF.Sin(150f * t * MathF.PI / 180f),
                150f * t,
                false,
                Dt,
                held
            );
        for (float t = 0f; t < 1.5f; t += Dt)
            s.Update(side * 0.136f, 90f, false, Dt, held);
        for (float t = 0f; t < 0.3f; t += Dt)
            s.Update(side * 0.136f, 90f, true, Dt, held);
        var share = side * 0.14f;
        for (float t = 0f; t < 3f; t += Dt)
            s.Update(share, 90f, false, Dt, held);
        for (float t = 0f; t < 20f; t += Dt)
        {
            float back = BodyStance.StepBack(-50f + amp * MathF.Sin(2f * MathF.PI * hz * t));
            var lean = share - s.Update(share, 90f, false, Dt, back);
            Assert.True(
                lean.Length() <= s.MaxLean + 0.01f,
                $"t {t:0.00}: lean {lean.Length():0.000} m"
            );
        }
    }

    [Fact]
    public void ASwayingLookAfterAGivingUp_LeansAtMostMaxLean()
    {
        // Back to -40 with no walk: the sway's falls must not eat what was kept past MaxLean.
        float deep = BodyStance.StepBack(-60f);
        var s = new TurnSkip();
        LeanBack(s, Vector3.Zero, deep);
        for (float t = 0f; t < 8f; t += Dt)
        {
            float k = MathF.Min(1f, t / 0.6f);
            float pitch =
                -60f
                + 20f * k
                + (t > 0.6f ? 3f * MathF.Sin(2f * MathF.PI * 0.3f * (t - 0.6f)) : 0f);
            float back = BodyStance.StepBack(pitch);
            var lean = LeanBack(s, Forward * (deep - back), back);
            Assert.True(
                lean.Length() <= s.MaxLean + 0.02f,
                $"t {t:0.00}: lean {lean.Length():0.000} m"
            );
        }
    }

    [Fact]
    public void ALeanThroughAGivingUp_LongAfterATurn_KeepsItsBend()
    {
        // A turn long gone gives the give-up no licence to take the head's own travel.
        const float Head = 0.2f;
        float deep = BodyStance.StepBack(-50f);
        var s = new TurnSkip();
        s.Update(Vector3.Zero, 0f, false, Dt, deep);
        for (float t = 0f; t <= 0.6f; t += Dt)
            s.Update(Vector3.Zero, 150f * t, false, Dt, deep);
        for (float t = 0f; t < 1f; t += Dt)
            s.Update(Vector3.Zero, 90f, false, Dt, deep);
        var lean = Vector3.Zero;
        for (float t = 0f; t < 1.5f; t += Dt)
        {
            float k = MathF.Min(1f, t / 0.5f);
            float back = BodyStance.StepBack(-50f + 30f * k);
            var share = Forward * (Head * k + deep - back);
            lean = share - s.Update(share, 90f, false, Dt, back);
        }
        TestUtil.Near(Head, lean.Length(), 0.01f);
    }

    [Fact]
    public void AGivingUpInsideATurnsSwing_LeavesTheSwingToTheWalk()
    {
        // The torso is still swinging onto the heading when the look starts up: that travel is the turn's, not the head's.
        float deep = BodyStance.StepBack(-50f);
        var s = new TurnSkip();
        s.Update(Vector3.Zero, 0f, false, Dt, deep);
        for (float t = 0f; t <= 0.6f; t += Dt)
            s.Update(Vector3.Zero, 150f * t, false, Dt, deep);
        for (float t = 0f; t < 0.4f; t += Dt)
        {
            float back = BodyStance.StepBack(-50f + 5f * t / 0.4f);
            var share = Forward * (0.1f * t / 0.4f + deep - back);
            var lean = share - s.Update(share, 90f, false, Dt, back);
            Assert.True(lean.Length() < 0.02f, $"t {t:0.00}: lean {lean.Length():0.000} m");
        }
    }
}
