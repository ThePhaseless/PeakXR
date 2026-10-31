namespace PeakXR.Core.Tests;

public class BodyStanceTests
{
    // The game turns the body about its mass centre, its eyes 0.236 m ahead of it; a real head turns about the neck,
    // its eyes 0.1 m ahead.
    const float Eyes = 0.236f;
    const float Neck = 0.1f;
    const float Tol = 1e-4f;

    static Vector3 Facing(float yaw)
    {
        var v = YawMath.Rotate(new Vector2(0f, 1f), yaw);
        return new Vector3(v.X, 0f, v.Y);
    }

    /// <summary>What the lean bends for: the debt plus its step back, less what <see cref="BodyStance.LeanSkip"/>
    /// leaves out (rig.camera adds the skip to the view offset the lean ignores).</summary>
    static Vector3 LeanPart(Vector3 debt, Vector3 forward, float back) =>
        debt + forward * back - BodyStance.LeanSkip(debt, forward, back);

    [Fact]
    public void Debt_IsTheViewLessTheBodysEyes_Level()
    {
        var d = BodyStance.Debt(
            new Vector3(0.3f, 1.6f, 0.2f),
            new Vector3(0.1f, 1.2f, 0.5f),
            Vector3.UnitZ,
            0f
        );
        Assert.True(Vector3.Distance(new Vector3(0.2f, 0f, -0.3f), d) < Tol, $"{d}");
    }

    [Fact]
    public void AnyTurn_WalkingTheDebt_PutsTheBodysEyesUnderTheView()
    {
        // Whatever moved the head off the body (a room turn about the neck, a snap turn pivoting on the eye, a step in
        // the room), the debt is measured from where the view is, so walking it ends with the eyes on the view.
        var mass = Vector3.Zero;
        var neck = Facing(0f) * (Eyes - Neck);
        foreach (float yaw in new[] { 45f, 90f, 180f, -90f })
        {
            var cases = new[]
            {
                neck + Facing(yaw) * Neck, // room head turn: the real eyes swing round the neck
                Facing(0f) * Eyes, // snap turn: the view held where the eyes were
                Facing(0f) * Eyes + new Vector3(0.4f, 0f, -0.3f), // a step in the room
            };
            foreach (var view in cases)
            {
                var bodyEye = mass + Facing(yaw) * Eyes;
                var walked = bodyEye + BodyStance.Debt(view, bodyEye, Facing(yaw), 0f);
                Assert.True(
                    Vector2.Distance(new Vector2(view.X, view.Z), new Vector2(walked.X, walked.Z))
                        < Tol,
                    $"{yaw}: {walked} vs {view}"
                );
            }
        }
    }

    [Fact]
    public void LookingDown_StepsBackAwayFromTheFacing()
    {
        var f = Facing(90f);
        var d = BodyStance.Debt(f * Eyes, f * Eyes, f, 0.2f);
        Assert.True(Vector3.Distance(new Vector3(-0.2f, 0f, 0f), d) < Tol, $"{d}");
    }

    [Fact]
    public void LeanSkip_LevelLook_TheLeanBendsForTheWholeDebt()
    {
        var f = Facing(30f);
        var debt = new Vector3(0.2f, 0f, -0.1f);
        Assert.True(BodyStance.LeanSkip(debt, f, 0f).Length() < Tol);
        Assert.True(Vector3.Distance(debt, LeanPart(debt, f, 0f)) < Tol);
    }

    [Fact]
    public void LeanSkip_LookingDown_TheBodyNeverBendsForItsStepBack()
    {
        // Before the step (the whole step owed), halfway, and stepped back: no bend at any point.
        var f = Facing(-60f);
        const float back = 0.28f;
        foreach (float owed in new[] { back, back * 0.5f, 0f })
        {
            var part = LeanPart(-f * owed, f, back);
            Assert.True(part.Length() < Tol, $"owed {owed}: bends {part}");
        }
    }

    [Fact]
    public void LeanSkip_LookingDown_KeepsTheRestOfTheDebtForTheLean()
    {
        var f = Facing(0f);
        const float back = 0.28f;
        var side = new Vector3(0.15f, 0f, 0f);
        // Sideways stays the lean's while the step back is still owed.
        var part = LeanPart(side - f * back, f, back);
        Assert.True(Vector3.Distance(side, part) < Tol, $"{part}");
        // A body ahead of its stepped-back place bends toward that place.
        var ahead = LeanPart(f * 0.1f, f, back);
        Assert.True(Vector3.Distance(f * 0.1f, ahead) < Tol, $"{ahead}");
    }

    [Fact]
    public void StepBack_StartsPastTheThresholdAndGrowsWithPitch()
    {
        Assert.Equal(0f, BodyStance.StepBack(0f));
        Assert.Equal(0f, BodyStance.StepBack(BodyStance.StepBackFromPitch));
        Assert.Equal(0f, BodyStance.StepBack(30f));
        float mid = BodyStance.StepBack(-50f);
        Assert.True(mid > 0f && mid < BodyStance.MaxStepBack, $"{mid}");
        // vr.273 looks down 60 degrees and wants the body at least 0.2 m back.
        Assert.True(BodyStance.StepBack(-60f) >= 0.25f, $"{BodyStance.StepBack(-60f)}");
        Assert.True(MathF.Abs(BodyStance.MaxStepBack - BodyStance.StepBack(-85f)) < Tol);
        float last = 0f;
        for (float p = -30f; p >= -85f; p -= 1f)
        {
            float b = BodyStance.StepBack(p);
            Assert.True(b >= last, $"step back shrank at {p}");
            last = b;
        }
    }
}

public class StanceWalkTests
{
    const float Dt = 1f / 90f;

    /// <summary>Holds <paramref name="debt"/> for <paramref name="seconds"/>; the last request.</summary>
    static Vector2 Hold(StanceWalk w, Vector2 debt, float seconds)
    {
        var r = Vector2.Zero;
        for (float t = 0f; t < seconds; t += Dt)
            r = w.Update(debt, Vector2.Zero, canWalk: true, Dt);
        return r;
    }

    [Fact]
    public void SmallDebt_StaysPut()
    {
        var w = new StanceWalk();
        Assert.Equal(Vector2.Zero, Hold(w, new Vector2(0.04f, 0f), 3f));
        Assert.False(w.Walking);
    }

    [Fact]
    public void HeldDebt_WaitsOutTheDelay_ThenWalks()
    {
        var w = new StanceWalk();
        var debt = new Vector2(0.2f, 0f);
        Assert.Equal(Vector2.Zero, Hold(w, debt, w.Delay - 0.1f));
        Assert.False(w.Walking);
        Assert.NotEqual(Vector2.Zero, Hold(w, debt, 0.2f));
        Assert.True(w.Walking);
    }

    [Fact]
    public void Glance_ReturnsBeforeTheBodyMoves()
    {
        // A look aside and back (the debt swings out to a turned head's 0.2 m and returns within a second): no step,
        // so peers never see the scout shuffle for a glance (#50).
        var w = new StanceWalk();
        for (float t = 0f; t < 1.2f; t += Dt)
        {
            float k = MathF.Sin(MathF.PI * t / 1.2f);
            Assert.Equal(
                Vector2.Zero,
                w.Update(new Vector2(0.2f * k, 0f), Vector2.Zero, canWalk: true, Dt)
            );
        }
        Assert.Equal(Vector2.Zero, Hold(w, Vector2.Zero, 1f));
        Assert.False(w.Walking);
    }

    [Fact]
    public void MovingDebt_KeepsWaiting()
    {
        // A slow look round: the owed step keeps changing direction, so the body waits for the head to settle.
        var w = new StanceWalk();
        for (float t = 0f; t < 4f; t += Dt)
        {
            var d = YawMath.Rotate(new Vector2(0f, 0.25f), 90f * t);
            Assert.Equal(Vector2.Zero, w.Update(d, Vector2.Zero, canWalk: true, Dt));
        }
    }

    [Fact]
    public void FarDebt_WalksAtOnce()
    {
        var w = new StanceWalk();
        var far = w.Update(new Vector2(0f, w.AtOnce + 0.5f), Vector2.Zero, canWalk: true, Dt);
        Assert.True(Vector2.Distance(new Vector2(0f, 1f), far) < 1e-4f, $"{far}");
        Assert.True(w.Walking);
        // Ramps down as it closes.
        var near = w.Update(new Vector2(0f, 0.1f), Vector2.Zero, canWalk: true, Dt);
        Assert.True(MathF.Abs(near.X) < 1e-4f, $"{near}");
        Assert.InRange(near.Y, 0.05f, 0.9f);
    }

    [Fact]
    public void SmallHeldDebt_AsksForEnoughToMoveTheBody()
    {
        // Run 9: a 0.05 m debt asked for 0.1 of the stick, which never moved the standing body, and stalled.
        var w = new StanceWalk();
        var r = Hold(w, new Vector2(0.06f, 0f), w.Delay + 0.1f);
        Assert.True(r.Length() >= w.MinInput - 1e-4f, $"{r.Length():0.00}");
    }

    [Fact]
    public void WalkedBody_ThatIgnoresSmallInput_StillEndsUnderTheView()
    {
        // The standing body does not move for input under 0.25 (run 9, 0.1 of the stick for 1.6 s, no headway).
        var w = new StanceWalk();
        var debt = new Vector2(0.08f, 0f);
        var v = Vector2.Zero;
        for (float t = 0f; t < 4f; t += Dt)
        {
            var request = w.Update(debt, v, canWalk: true, Dt);
            var drive = request.Length() < 0.25f ? Vector2.Zero : request;
            v += (drive * 1.1f - v) * (1f - MathF.Exp(-Dt / 0.12f));
            debt -= v * Dt;
        }
        Assert.True(debt.Length() < 0.05f, $"left {debt.Length():0.000} m off the view");
        Assert.False(w.Blocked);
    }

    [Fact]
    public void Walk_StopsOnlyInsideTheStopDistance()
    {
        var w = new StanceWalk();
        w.Update(new Vector2(1f, 0f), Vector2.Zero, canWalk: true, Dt);
        // Inside the start but outside the stop distance: a started walk carries on.
        Assert.NotEqual(
            Vector2.Zero,
            w.Update(new Vector2(0.045f, 0f), Vector2.Zero, canWalk: true, Dt)
        );
        Assert.Equal(
            Vector2.Zero,
            w.Update(new Vector2(0.01f, 0f), Vector2.Zero, canWalk: true, Dt)
        );
        // Settling: the body's coast still belongs to the walk.
        Assert.True(w.Walking);
        Hold(w, new Vector2(0.01f, 0f), w.SettleSeconds + Dt);
        Assert.False(w.Walking);
    }

    [Fact]
    public void CannotWalk_RequestsNothingAndOwnsNoTravel()
    {
        var w = new StanceWalk();
        w.Update(new Vector2(1f, 0f), Vector2.Zero, canWalk: true, Dt);
        Assert.Equal(Vector2.Zero, w.Update(new Vector2(1f, 0f), Vector2.Zero, canWalk: false, Dt));
        // Stick walking moves the view with the body: none of that travel is the stance walk's.
        Assert.False(w.Walking);
    }

    [Fact]
    public void BlockedWalk_GivesUpUntilTheDebtChanges()
    {
        var w = new StanceWalk();
        var stuck = new Vector2(0.5f, 0f);
        Hold(w, stuck, w.StallSeconds + 0.1f);
        Assert.True(w.Blocked);
        Assert.Equal(Vector2.Zero, w.Update(stuck, Vector2.Zero, canWalk: true, Dt));
        Assert.False(w.Walking);
        // The head moved on: try again.
        Assert.NotEqual(
            Vector2.Zero,
            w.Update(new Vector2(0.5f, 0.1f), Vector2.Zero, canWalk: true, Dt)
        );
        Assert.False(w.Blocked);
    }

    [Fact]
    public void WalkedBody_EndsUnderTheView()
    {
        // A body that walks after its request like the game's driven ragdoll (speed eases toward input x 1.1 m/s
        // with a 0.12 s time constant: movementForce 10 under drag 0.85 a 50 Hz step), its travel paid off the debt.
        foreach (var start in new[] { new Vector2(0.19f, -0.19f), new Vector2(0f, -0.6f) })
        {
            var w = new StanceWalk();
            var debt = start;
            var v = Vector2.Zero;
            float maxAfter = 0f;
            for (float t = 0f; t < 5f; t += Dt)
            {
                var request = w.Update(debt, v, canWalk: true, Dt);
                v += (request * 1.1f - v) * (1f - MathF.Exp(-Dt / 0.12f));
                debt -= v * Dt;
                if (t > 4f)
                    maxAfter = MathF.Max(maxAfter, debt.Length());
            }
            Assert.True(maxAfter < 0.05f, $"{start}: left {maxAfter:0.000} m off the view");
            Assert.False(w.Walking);
            Assert.False(w.Blocked);
        }
    }

    [Fact]
    public void FastBody_WalksOnce_AndStopsUnderTheView()
    {
        // cf6aa3e vr.45: the walked body reached about 2 m/s and coasted 0.1-0.16 m past the view after the request
        // stopped, and each overshoot walked again after the delay. The body here: 2 m/s top speed, 0.12 s time
        // constant, the request reaching it one update late, 33 updates a second (the rig's frame rate then).
        const float dt = 1f / 33f;
        foreach (float d0 in new[] { 0.08f, 0.3f, 0.6f })
        {
            var w = new StanceWalk();
            var debt = new Vector2(0f, d0);
            var v = Vector2.Zero;
            var late = Vector2.Zero;
            float past = 0f;
            int walks = 0;
            bool was = false;
            for (float t = 0f; t < 6f; t += dt)
            {
                var request = w.Update(debt, v, canWalk: true, dt);
                if (w.Walking && !was)
                    walks++;
                was = w.Walking;
                for (int i = 0; i < 10; i++)
                {
                    v += (late * 2f - v) * (1f - MathF.Exp(-dt / 10f / 0.12f));
                    debt -= v * dt / 10f;
                }
                late = request;
                past = MathF.Max(past, -debt.Y);
            }
            Assert.True(past <= w.Stop, $"{d0}: coasted {past:0.000} m past the view");
            Assert.Equal(1, walks);
            Assert.True(debt.Length() < w.Start, $"{d0}: left {debt.Length():0.000} m");
        }
    }

    [Fact]
    public void CoastingBody_EndsTheRequestEarly_AndTheWalkOnceStill()
    {
        var w = new StanceWalk();
        // 0.2 m to go at 2 m/s: the coast alone covers it.
        w.Update(new Vector2(0.5f, 0f), Vector2.Zero, canWalk: true, Dt);
        Assert.Equal(
            Vector2.Zero,
            w.Update(new Vector2(0.2f, 0f), new Vector2(2f, 0f), canWalk: true, Dt)
        );
        // Stopped short of the stop distance: the walk carries on.
        Assert.NotEqual(
            Vector2.Zero,
            w.Update(new Vector2(0.1f, 0f), Vector2.Zero, canWalk: true, Dt)
        );
    }
}

public class OwnMotionTests
{
    const float Dt = 1f / 90f;

    [Fact]
    public void DriftWithNoWalk_IsNotAddedToTheDebtTheWalkerPays()
    {
        // A standing body pushed 0.3 m by wind over 2 s, no walk running and the head still: the view goes with it
        // (the room anchor keeps none of it), so the debt (view less body) never grows and nothing walks it back.
        var own = new OwnMotion();
        var follow = new RoomScaleFollow();
        var walk = new StanceWalk();
        var head = Vector2.Zero;
        for (float t = 0f; t < 2f; t += Dt)
        {
            var step = new Vector2(0.15f * Dt, 0f);
            var debt = head - follow.Anchor; // view = body + (head - anchor); body eyes = body
            Assert.Equal(Vector2.Zero, walk.Update(debt, Vector2.Zero, canWalk: true, Dt));
            if (own.Update(walk.Walking, 0.15f, new Vector2(30f, 0f), outside: false, Dt))
                follow.Absorb(step);
        }
        Assert.Equal(Vector2.Zero, head - follow.Anchor);
    }

    [Fact]
    public void AWalkAndItsCoast_AreKept_UntilTheBodyIsStill()
    {
        var own = new OwnMotion();
        var look = new Vector2(10f, 0f);
        for (float t = 0f; t < 0.5f; t += Dt)
            Assert.True(own.Update(true, 0.4f, look, false, Dt));
        // The coast: the walk is over, the body still sliding to a stop.
        Assert.True(own.Update(false, 0.3f, look, false, Dt));
        Assert.True(own.Update(false, 0.08f, look, false, Dt));
        Assert.False(own.Update(false, 0.02f, look, false, Dt));
        // A later drift is not the walk's.
        Assert.False(own.Update(false, 0.2f, look, false, Dt));
    }

    [Fact]
    public void AnOutsideForce_IsNeverKept_EvenInAWalk()
    {
        var own = new OwnMotion();
        Assert.False(own.Update(true, 0.4f, Vector2.Zero, outside: true, Dt));
        Assert.True(own.Update(true, 0.4f, Vector2.Zero, outside: false, Dt));
    }

    [Fact]
    public void TheBodysShiftUnderATurningHead_IsKept_ThenNot()
    {
        var own = new OwnMotion();
        var look = Vector2.Zero;
        own.Update(false, 0f, look, false, Dt);
        for (float t = 0f; t < 1f; t += Dt)
        {
            look.X = (look.X + 90f * Dt) % 360f; // wraps through 360
            Assert.True(own.Update(false, 0.1f, look, false, Dt));
        }
        for (float t = 0f; t < own.PoseSeconds - 0.05f; t += Dt)
            Assert.True(own.Update(false, 0.1f, look, false, Dt));
        for (float t = 0f; t < 0.1f; t += Dt)
            own.Update(false, 0.1f, look, false, Dt);
        Assert.False(own.Update(false, 0.1f, look, false, Dt));
    }
}
