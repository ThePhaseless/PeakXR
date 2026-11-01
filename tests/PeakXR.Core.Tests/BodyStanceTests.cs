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
    public void LeanBeyond_TheLeanBendsNoFurtherThanItsLimit()
    {
        // vr.396: a head walking away owed 0.41 m before the body caught up and the spine bent 20.7 degrees for it.
        var share = new Vector3(0.3f, -0.05f, 0.4f);
        var skip = BodyStance.LeanBeyond(share, 0.35f);
        var bent = share - skip;
        Assert.True(MathF.Abs(new Vector2(bent.X, bent.Z).Length() - 0.35f) < Tol);
        Assert.Equal(0f, skip.Y);
        Assert.True(Vector3.Dot(skip, share) > 0f);
        Assert.Equal(Vector3.Zero, BodyStance.LeanBeyond(new Vector3(0.2f, 0f, 0.1f), 0.35f));
    }

    [Fact]
    public void LeanLimit_WhileWalking_EasesDownAndBack()
    {
        // vr.396: with the lean solved onto the eyes, the 0.35 m limit bends about 40 degrees while the walk steps.
        // A held lean bent for 0.3 m when the walk starts: it eases down, never snaps.
        float limit = 0.35f;
        for (int i = 0; i < 10; i++)
            limit = BodyStance.LeanLimit(limit, 0.35f, true, 0.3f, 0.02f);
        Assert.True(limit < 0.3f && limit > BodyStance.WalkLeanLimit, $"jumped to {limit:0.000}");
        for (int i = 0; i < 100; i++)
            limit = BodyStance.LeanLimit(limit, 0.35f, true, 0.3f, 0.02f);
        Assert.Equal(BodyStance.WalkLeanLimit, limit);
        for (int i = 0; i < 100; i++)
            limit = BodyStance.LeanLimit(limit, 0.35f, false, 0.3f, 0.02f);
        Assert.Equal(0.35f, limit);
        // A lower setting stays the limit while walking.
        Assert.Equal(0.06f, BodyStance.LeanLimit(0.06f, 0.06f, true, 0.06f, 0.02f));
        Assert.Equal(0.35f, BodyStance.LeanLimit(float.NaN, 0.35f, false, 0f, 0.02f));
    }

    [Fact]
    public void LeanLimit_AWalkStartingUpright_NeverBendsPastTheWalkLimit()
    {
        // vr.396 (run 15): the head walked away in the room and the lean took the full 0.35 m (28.6 degrees) before the
        // first push; the cap only eased in after. Walking, the lean may shrink but never grow past what it bends now.
        float limit = 0.35f;
        float bent = 0f;
        for (float share = 0f; share < 0.6f; share += 0.01f)
        {
            limit = BodyStance.LeanLimit(limit, 0.35f, true, bent, 1f / 72f);
            bent = MathF.Min(share, limit);
            Assert.True(
                bent <= BodyStance.WalkLeanLimit + 1e-6f,
                $"share {share:0.00}: bent {bent:0.000}"
            );
        }
        // Stood still again, the lean eases back to the full limit.
        for (int i = 0; i < 200; i++)
            limit = BodyStance.LeanLimit(limit, 0.35f, false, bent, 1f / 72f);
        Assert.Equal(0.35f, limit);
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
            r = w.Update(debt, Vector2.Zero, 0f, canWalk: true, Dt);
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
    public void TimeUnderStart_DoesNotCountTowardTheDelay()
    {
        // Run 11, vr.45: the debt sat at 0.04 m for 4 s, the lean then took it to 0.06 m and the walk started at once,
        // 0.36 s before the lean's hold began. The delay runs from when the debt is owed.
        var w = new StanceWalk();
        Hold(w, new Vector2(-0.06f, 0f), 0.1f);
        Hold(w, new Vector2(0.04f, 0f), 4f);
        Assert.Equal(Vector2.Zero, Hold(w, new Vector2(0.06f, 0f), w.Delay - 0.1f));
        Assert.NotEqual(Vector2.Zero, Hold(w, new Vector2(0.06f, 0f), 0.2f));
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
                w.Update(new Vector2(0.2f * k, 0f), Vector2.Zero, 0f, canWalk: true, Dt)
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
            Assert.Equal(Vector2.Zero, w.Update(d, Vector2.Zero, 0f, canWalk: true, Dt));
        }
    }

    [Fact]
    public void FarDebt_WalksAtOnce()
    {
        var w = new StanceWalk();
        var far = w.Update(new Vector2(0f, w.AtOnce + 0.5f), Vector2.Zero, 0f, canWalk: true, Dt);
        Assert.True(w.Walking);
        Assert.True(far.Y > 0f && MathF.Abs(far.X) < 1e-4f, $"{far}");
        // Eased in at the game's rate: full after 1 / InputSlew seconds.
        far = Hold(w, new Vector2(0f, w.AtOnce + 0.5f), 1f / w.InputSlew);
        Assert.True(Vector2.Distance(new Vector2(0f, 1f), far) < 1e-3f, $"{far}");
        // Ramps down as it closes.
        var near = Hold(w, new Vector2(0f, 0.1f), 0.3f);
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
        // The standing body does not move for input under the gait's 0.125 snap (CharacterAnimations.cs:189-196): its
        // feet hold it (run 9, 0.08-0.11 of the stick for 1.6 s, no headway).
        var w = new StanceWalk();
        var debt = new Vector2(0.08f, 0f);
        var v = Vector2.Zero;
        for (float t = 0f; t < 4f; t += Dt)
        {
            var request = w.Update(debt, v, 0f, canWalk: true, Dt);
            var drive = request.Length() < 0.15f ? Vector2.Zero : request;
            v += (drive * 1.1f - v) * (1f - MathF.Exp(-Dt / 0.12f));
            debt -= v * Dt;
        }
        Assert.True(debt.Length() < 0.05f, $"left {debt.Length():0.000} m off the view");
        Assert.False(w.Blocked);
        Assert.False(w.Walking);
    }

    [Fact]
    public void Push_HoldsForTheGait_ThenSettlesUntilTheBodyIsStill()
    {
        var w = new StanceWalk();
        w.Update(new Vector2(1f, 0f), Vector2.Zero, 0f, canWalk: true, Dt);
        // Already under the view, but the gait has not stepped yet: the push holds its request.
        Assert.NotEqual(
            Vector2.Zero,
            w.Update(new Vector2(0.01f, 0f), Vector2.Zero, 0f, canWalk: true, Dt)
        );
        Hold(w, new Vector2(0.01f, 0f), w.MinPush);
        Assert.Equal(StanceWalkPhase.Settle, w.State);
        // Settling: the request runs down to nothing, and the body's coast still belongs to the walk until it has
        // been still a while.
        var debt = new Vector2(0.01f, 0f);
        for (float t = 0f; t < 1f / w.InputSlew; t += Dt, debt.X -= 0.1f * Dt)
            w.Update(debt, new Vector2(0.1f, 0f), 0f, canWalk: true, Dt);
        Assert.Equal(Vector2.Zero, w.Update(debt, new Vector2(0.1f, 0f), 0f, canWalk: true, Dt));
        Assert.True(w.Walking);
        Hold(w, debt, w.StillSeconds + Dt);
        Assert.False(w.Walking);
    }

    [Fact]
    public void Push_WouldLandPastTheView_EndsBeforeTheGaitMinimum()
    {
        var w = new StanceWalk();
        w.Update(new Vector2(0.4f, 0f), Vector2.Zero, 0f, canWalk: true, Dt);
        // 0.1 m to go at 2 m/s: the coast alone carries it more than Stop past.
        Assert.Equal(
            Vector2.Zero,
            w.Update(new Vector2(0.1f, 0f), new Vector2(2f, 0f), 0f, canWalk: true, Dt)
        );
        Assert.Equal(StanceWalkPhase.Settle, w.State);
    }

    [Fact]
    public void CannotWalk_RequestsNothingAndOwnsNoTravel()
    {
        var w = new StanceWalk();
        w.Update(new Vector2(1f, 0f), Vector2.Zero, 0f, canWalk: true, Dt);
        Assert.Equal(
            Vector2.Zero,
            w.Update(new Vector2(1f, 0f), Vector2.Zero, 0f, canWalk: false, Dt)
        );
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
        // The request runs down as the game's input does, then stays at nothing.
        Assert.Equal(Vector2.Zero, Hold(w, stuck, 1f / w.InputSlew + Dt));
        Assert.False(w.Walking);
        // The head moved on: try again.
        Assert.NotEqual(
            Vector2.Zero,
            w.Update(new Vector2(0.5f, 0.1f), Vector2.Zero, 0f, canWalk: true, Dt)
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
                var request = w.Update(debt, v, 0f, canWalk: true, Dt);
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

    /// <summary>
    /// Walks <paramref name="d0"/> m off with a game-like body: 2 m/s top speed, 0.12 s time constant, the request
    /// reaching it one update late and eased toward at <paramref name="slew"/> a second (Character.cs:1341,
    /// worldMovementInput_Lerp), <paramref name="hz"/> updates a second. Returns the most it went past the view, the
    /// walks, what is left, and whether it gave up.
    /// </summary>
    static (float Past, int Walks, float Left, bool Blocked, bool Walking) WalkBody(
        float d0,
        float hz,
        float slew
    )
    {
        float dt = 1f / hz;
        var w = new StanceWalk { InputSlew = slew };
        var debt = new Vector2(0f, d0);
        var v = Vector2.Zero;
        var late = Vector2.Zero;
        var eased = Vector2.Zero;
        float past = 0f;
        int walks = 0;
        bool was = false;
        for (float t = 0f; t < 6f; t += dt)
        {
            var request = w.Update(debt, v, eased.Length(), canWalk: true, dt);
            if (w.Walking && !was)
                walks++;
            was = w.Walking;
            for (int i = 0; i < 10; i++)
            {
                float h = dt / 10f;
                var step = late - eased;
                float most = slew * h;
                eased += step.Length() > most ? step / step.Length() * most : step;
                v += (eased * 2f - v) * (1f - MathF.Exp(-h / 0.12f));
                debt -= v * h;
            }
            late = request;
            past = MathF.Max(past, -debt.Y);
        }
        return (past, walks, debt.Length(), w.Blocked, w.Walking);
    }

    [Fact]
    public void FastBody_WalksOnce_AndStopsUnderTheView()
    {
        // cf6aa3e vr.45: the walked body reached about 2 m/s and coasted 0.1-0.16 m past the view after the request
        // stopped, and each overshoot walked again after the delay. Input that reaches the body unslowed, 33 updates
        // a second (the rig's frame rate then). A push holds MinPush for the gait, so a small debt may end up to Start
        // past the view, which a settled walk leaves to the lean.
        var w = new StanceWalk();
        foreach (float d0 in new[] { 0.08f, 0.3f, 0.6f })
        {
            var (past, walks, left, _, walking) = WalkBody(d0, 33f, float.PositiveInfinity);
            Assert.False(walking, $"{d0}: still walking");
            Assert.True(past <= w.Start, $"{d0}: coasted {past:0.000} m past the view");
            Assert.Equal(1, walks);
            Assert.True(left < w.Start, $"{d0}: left {left:0.000} m");
        }
    }

    [Fact]
    public void EasedInput_WalksOnce_AndStopsUnderTheView()
    {
        // The game eases its move input toward the stick at movementTurnSpeed, 2 a second by default
        // (CharacterMovement.cs:50, Character.cs:1336-1341), so a full walk pushes on for 0.5 s after the request ends:
        // a 0.15 s coast left 0.16 m past the view for a 0.6 m debt. It stays inside the start distance, which a
        // settled walk leaves to the lean (a push holds MinPush for the gait).
        var w = new StanceWalk();
        foreach (float hz in new[] { 33f, 72f, 90f })
        foreach (float d0 in new[] { 0.08f, 0.3f, 0.6f, 1f })
        {
            var (past, walks, left, blocked, walking) = WalkBody(d0, hz, 2f);
            Assert.False(walking, $"{hz} Hz, {d0}: still walking");
            Assert.True(past <= w.Start, $"{hz} Hz, {d0}: coasted {past:0.000} m past the view");
            Assert.Equal(1, walks);
            Assert.True(left < w.Start, $"{hz} Hz, {d0}: left {left:0.000} m");
            Assert.False(blocked);
        }
    }

    [Fact]
    public void BodyRunningPastTheDebt_GetsNoInputBack()
    {
        // The body ran 0.05 m past the view and still runs on at 1 m/s: input back would brake it on the spot for peers.
        var w = new StanceWalk();
        w.Update(new Vector2(0f, 0.5f), Vector2.Zero, 0f, canWalk: true, Dt);
        Assert.Equal(
            Vector2.Zero,
            w.Update(new Vector2(0f, -0.05f), new Vector2(0f, 1f), 0f, canWalk: true, Dt)
        );
        // Moving across the walk is not running against it.
        var across = new StanceWalk();
        across.Update(new Vector2(0.5f, 0f), Vector2.Zero, 0f, canWalk: true, Dt);
        Assert.True(
            across.Update(new Vector2(0.3f, 0f), new Vector2(0f, 1f), 0f, canWalk: true, Dt).X > 0f
        );
    }

    /// <summary>The body overshot to <paramref name="debt"/> and has stopped: once still the walk pushes back and keeps
    /// pushing, without giving up, while the body is walked back toward the view.</summary>
    static bool WalksBack(StanceWalk w, Vector2 debt)
    {
        var r = Hold(w, debt, w.StillSeconds + 2f * Dt);
        if (!(r.X < 0f) || w.Blocked)
            return false;
        for (float t = 0f; t < w.MinPush; t += Dt)
        {
            r = w.Update(debt, new Vector2(-0.05f, 0f), 0f, canWalk: true, Dt);
            if (!(r.X < 0f) || w.Blocked)
                return false;
            debt.X += 0.05f * Dt;
        }
        return true;
    }

    [Fact]
    public void OvershotWalk_WalksBack_WithoutGivingUp()
    {
        // The body coasted 0.2 m past the view over 1 s (its travel, so the view stood still), then stopped: the same
        // walk walks it back, its headway counted from where it turned, not from the closest the overshoot came.
        var w = new StanceWalk();
        w.Update(new Vector2(0.5f, 0f), Vector2.Zero, 0f, canWalk: true, Dt);
        w.Update(Vector2.Zero, new Vector2(1f, 0f), 0f, canWalk: true, Dt);
        Overshoot(w);
        Assert.True(WalksBack(w, new Vector2(-0.2f, 0f)), "no walk back once still");
    }

    /// <summary>The body coasts on 0.2 m past the view over 1 s at 0.2 m/s, the view still.</summary>
    static void Overshoot(StanceWalk w)
    {
        for (float t = Dt; t <= 1f + 1e-4f; t += Dt)
            w.Update(new Vector2(-0.2f * t, 0f), new Vector2(0.2f, 0f), 0f, canWalk: true, Dt);
    }

    [Fact]
    public void RigTurnedAroundMidWalk_KeepsWalking_AndStillWalksBackOnce()
    {
        // The walk is fed in the world frame (level), so a 180-degree snap or smooth turn of the tracking space mid-walk
        // changes none of its inputs. Fed in tracking space, the turn flipped the debt against the walk's heading: it
        // read as an overshoot, stopped asking and used up its one walk back, so the real overshoot later stalled.
        // (Verified red by negating the debt and velocity after the turn, as the tracking-space feed did.)
        var w = new StanceWalk();
        var debt = new Vector2(0.6f, 0f);
        for (float t = 0f; t < 2f && debt.X > 0.3f; t += Dt)
        {
            var request = w.Update(debt, Vector2.Zero, 0f, canWalk: true, Dt);
            debt.X -= request.X * Dt;
        }
        // The rig turns 180 degrees here: the world-frame inputs carry on unchanged.
        var turned = w.Update(debt, new Vector2(1f, 0f), 0f, canWalk: true, Dt);
        Assert.True(turned.X > 0f, "walk stopped at the turn");
        // Then the body coasts 0.2 m past the view for 1 s and is walked back slowly, as in OvershotWalk_WalksBack.
        w.Update(Vector2.Zero, new Vector2(1f, 0f), 0f, canWalk: true, Dt);
        Overshoot(w);
        Assert.True(WalksBack(w, new Vector2(-0.2f, 0f)), "no walk back once still");
    }

    [Fact]
    public void WalkThatKeepsOvershooting_GivesUp()
    {
        // A body that keeps swinging past the view (a coast the walk misjudges) stops being pushed instead of
        // shuffling back and forth for peers: the walk gives up or ends, and no new one starts on the swing.
        var w = new StanceWalk();
        w.Update(new Vector2(0.5f, 0f), Vector2.Zero, 0f, canWalk: true, Dt);
        int pushes = 1;
        var was = w.State;
        for (float t = 0f; t < 15f; t += Dt)
        {
            float x = 0.06f * MathF.Cos(MathF.PI * 2f * t / 1.2f);
            float vx = 0.06f * MathF.PI * 2f / 1.2f * MathF.Sin(MathF.PI * 2f * t / 1.2f);
            w.Update(new Vector2(x, 0f), new Vector2(vx, 0f), 0f, canWalk: true, Dt);
            if (w.State == StanceWalkPhase.Push && was != StanceWalkPhase.Push)
                pushes++;
            was = w.State;
        }
        Assert.False(w.Walking);
        Assert.True(pushes <= w.MaxPushes, $"{pushes} pushes");
    }

    /// <summary>
    /// Walks <paramref name="d0"/> m off with the game-like body of <see cref="WalkBody"/> standing on planted feet.
    /// The gait reads the raw request damped over 0.125 s and snapped to 0 under 0.125 (CharacterAnimations.cs:185-196);
    /// the force reads the eased input (Character.cs:1341). The feet follow the body while that gait input has been over
    /// 0.15 for <paramref name="stepTime"/>, and otherwise stay planted and pull the body back over them at 1.5 m/s per
    /// metre (run 11, vr.301 t994.4-995.3: 0.11 m off came back at 0.1 m/s, falling). A request cut to 0 stops the gait
    /// while the eased force still carries the body on, so it springs back by what it coasted (run 11: about 0.1 m
    /// after a push; a follow-up push of 0.1 m is nearly all coast and springs back whole). Returns the walks, what is
    /// left, whether it gave up, the most the request switched on in one second, the pushes, and whether it still walks
    /// at the end. The step time and that 0.2 of the stick steps the feet are assumptions (#10).
    /// </summary>
    static (
        int Walks,
        float Left,
        bool Blocked,
        int MostOnsInASecond,
        int Pushes,
        bool Walking
    ) WalkPlanted(float d0, float hz, float stepTime = 0.25f)
    {
        float dt = 1f / hz;
        var w = new StanceWalk();
        var head = new Vector2(0f, d0);
        var p = Vector2.Zero;
        var feet = Vector2.Zero;
        var v = Vector2.Zero;
        var late = Vector2.Zero;
        var eased = Vector2.Zero;
        float pushed = 0f;
        float gait = 0f;
        int walks = 0;
        bool was = false;
        bool on = false;
        var ons = new System.Collections.Generic.List<float>();
        int most = 0;
        for (float t = 0f; t < 8f; t += dt)
        {
            var request = w.Update(head - p, v, eased.Length(), canWalk: true, dt);
            if (w.Walking && !was)
                walks++;
            was = w.Walking;
            bool now = request != Vector2.Zero;
            if (now && !on)
            {
                ons.Add(t);
                ons.RemoveAll(x => x < t - 1f);
                most = Math.Max(most, ons.Count);
            }
            on = now;
            for (int i = 0; i < 10; i++)
            {
                float h = dt / 10f;
                gait += (late.Length() - gait) * (1f - MathF.Exp(-h / 0.125f));
                if (gait < 0.125f && late.Length() < 0.125f)
                    gait = 0f;
                pushed = gait > 0.15f ? pushed + h : 0f;
                bool stepping = pushed >= stepTime;
                if (stepping)
                    feet = p;
                var step = late - eased;
                float max = 2f * h;
                eased += step.Length() > max ? step / step.Length() * max : step;
                var target = eased * 2f + (stepping ? Vector2.Zero : (feet - p) * 1.5f);
                v += (target - v) * (1f - MathF.Exp(-h / 0.12f));
                p += v * h;
            }
            late = request;
        }
        return (walks, (head - p).Length(), w.Blocked, most, w.Pushes, w.Walking);
    }

    [Fact]
    public void BodyOnPlantedFeet_IsWalkedUnderTheView_InOneWalkOfSteadyPushes()
    {
        // Run 10: the request ended after a frame or two and then flipped on and off every frame, the gait never
        // stepped, and the body sprang back; the walk had ended, so the next one waited another delay (vr.45b +180:
        // 0.259 m still owed at the settle's end).
        // Run 11: a walk's first push from a 0.15-0.38 m debt kept 0.04-0.45 m, but sprang back about 0.1 m, and the
        // pushes after it on the 0.07-0.2 m left mostly sprang back whole. One eased push: legs and body stop
        // together, and what it leaves stays under Again for the lean.
        var w = new StanceWalk();
        foreach (float hz in new[] { 33f, 72f })
        foreach (float d0 in new[] { 0.06f, 0.08f, 0.12f, 0.26f, 0.6f })
        {
            var (walks, left, blocked, ons, pushes, walking) = WalkPlanted(d0, hz);
            Assert.True(left <= w.Again, $"{hz} Hz, {d0}: left {left:0.000} m");
            Assert.Equal(1, walks);
            Assert.Equal(1, pushes);
            Assert.False(blocked, $"{hz} Hz, {d0}: gave up");
            Assert.True(
                ons <= 2,
                $"{hz} Hz, {d0}: the request switched on {ons} times in a second"
            );
            Assert.False(walking, $"{hz} Hz, {d0}: still walking");
        }
    }

    [Theory]
    [InlineData(0.3f)]
    [InlineData(0.35f)]
    public void SlowerGait_StillWalksUnderTheView_OrGivesUpQuietly(float stepTime)
    {
        // The feet's step time is a guess (WalkPlanted). A gait slower than MinPush springs every push back; the walk
        // must then end, not twitch on for peers, and never stop in the middle of a walk.
        var w = new StanceWalk();
        foreach (float hz in new[] { 72f, 90f })
        foreach (float d0 in new[] { 0.06f, 0.12f, 0.26f, 0.6f })
        {
            var (_, left, blocked, ons, pushes, walking) = WalkPlanted(d0, hz, stepTime);
            Assert.False(walking, $"{stepTime} s, {hz} Hz, {d0}: still walking");
            Assert.True(ons <= 2, $"{stepTime} s, {hz} Hz, {d0}: {ons} pushes in a second");
            Assert.True(pushes <= w.MaxPushes, $"{stepTime} s, {hz} Hz, {d0}: {pushes} pushes");
            Assert.True(
                blocked || left <= w.Again,
                $"{stepTime} s, {hz} Hz, {d0}: left {left:0.000} m"
            );
        }
    }

    [Fact]
    public void NoisyBodySpeed_StillEndsTheWalk()
    {
        // Run 10 t289.4-291.1: the whole-body speed swung 0.06-0.26 m/s with the debt under the stop distance, and the
        // walk never ended (vr.45: a walk "already running" at a lean's start; vr.302: the setup's walk through the glance).
        var w = new StanceWalk();
        w.Update(new Vector2(0.5f, 0f), Vector2.Zero, 0f, canWalk: true, Dt);
        float sign = 1f;
        for (float t = 0f; t < 3f; t += Dt, sign = -sign)
            w.Update(new Vector2(0.01f, 0f), new Vector2(0.2f * sign, 0f), 0f, canWalk: true, Dt);
        Assert.False(w.Walking);
        Assert.False(w.Blocked);
    }

    [Fact]
    public void CoastingBody_EndsTheRequestEarly_AndTheWalkOnceStill()
    {
        var w = new StanceWalk();
        // 0.15 m to go at 2 m/s: the coast alone carries it more than Start past.
        w.Update(new Vector2(0.5f, 0f), Vector2.Zero, 0f, canWalk: true, Dt);
        Assert.Equal(
            Vector2.Zero,
            w.Update(new Vector2(0.15f, 0f), new Vector2(2f, 0f), 0f, canWalk: true, Dt)
        );
        // Stopped short, past Again: once still, the same walk pushes again, with no new delay.
        Assert.Equal(Vector2.Zero, Hold(w, new Vector2(0.17f, 0f), w.StillSeconds - 2f * Dt));
        Assert.NotEqual(Vector2.Zero, Hold(w, new Vector2(0.17f, 0f), 3f * Dt));
        Assert.Equal(2, w.Pushes);
    }

    [Fact]
    public void ALeftoverUnderAgain_IsLeftToTheLean_UntilTheDebtMoves()
    {
        // Run 11: follow-up pushes on 0.07-0.2 m mostly sprang back whole. What a settled walk leaves under Again stays
        // with the lean, and a body creeping back over its feet afterwards starts no new walk.
        var w = new StanceWalk();
        w.Update(new Vector2(0.5f, 0f), Vector2.Zero, 0f, canWalk: true, Dt);
        w.Update(new Vector2(0.15f, 0f), new Vector2(2f, 0f), 0f, canWalk: true, Dt);
        Hold(w, new Vector2(0.12f, 0f), w.SettleMax + Dt);
        Assert.False(w.Walking);
        Assert.True(w.Rested);
        Assert.Equal(Vector2.Zero, Hold(w, new Vector2(0.15f, 0f), w.Delay + 1f));
        Assert.False(w.Walking);
        // The head moves on: a new debt, walked after its delay.
        Assert.Equal(Vector2.Zero, Hold(w, new Vector2(0.12f, 0.2f), w.Delay - 0.1f));
        Assert.NotEqual(Vector2.Zero, Hold(w, new Vector2(0.12f, 0.2f), 0.2f));
    }

    [Fact]
    public void ARestedLeftover_StaysRested_ThroughAMomentTheBodyCannotWalk()
    {
        // A push, the stick touched or a mantle stops the walk for a moment: the leftover is still the lean's.
        var w = new StanceWalk();
        w.Update(new Vector2(0.5f, 0f), Vector2.Zero, 0f, canWalk: true, Dt);
        w.Update(new Vector2(0.15f, 0f), new Vector2(2f, 0f), 0f, canWalk: true, Dt);
        Hold(w, new Vector2(0.12f, 0f), w.SettleMax + Dt);
        Assert.True(w.Rested);
        w.Update(new Vector2(0.12f, 0f), Vector2.Zero, 0f, canWalk: false, Dt);
        Assert.True(w.Rested);
        Assert.Equal(Vector2.Zero, Hold(w, new Vector2(0.12f, 0f), w.Delay + 1f));
    }

    [Fact]
    public void ARestedLeftoverWhoseDebtMoves_WaitsAFreshDelay()
    {
        // Run 20, vr.273 t231.07: the look was coming back up, the debt left the spot it rested on while the older held
        // spot still read as held, and a push fired at once on a moving debt and ended 0.09 s later.
        var w = new StanceWalk();
        w.Update(new Vector2(0.5f, 0f), Vector2.Zero, 0f, canWalk: true, Dt);
        // The push ends at 0.10 m, and the body springs back to rest at 0.13 m, within Start of it.
        w.Update(new Vector2(0.1f, 0f), new Vector2(1.3f, 0f), 0f, canWalk: true, Dt);
        Hold(w, new Vector2(0.13f, 0f), w.SettleMax + Dt);
        Assert.True(w.Rested);
        Assert.Equal(Vector2.Zero, Hold(w, new Vector2(0.13f, 0f), 1f));
        // 0.075 m is Start from the rested spot, and less from the held one.
        Assert.Equal(Vector2.Zero, Hold(w, new Vector2(0.075f, 0f), w.Delay - 0.1f));
        Assert.NotEqual(Vector2.Zero, Hold(w, new Vector2(0.075f, 0f), 0.2f));
    }

    [Fact]
    public void ATurnBeforeTheWalk_LeavesTheFeetLeadWhole()
    {
        // Run 20, vr.300: a stand read before the turn, in world axes, said the feet were 0.15 m ahead when the push
        // began; the lead stayed clamped to 0, pushes stopped on the debt and sprang straight back, on the same debt.
        static float PushAfter(Vector2 creep)
        {
            var w = new StanceWalk();
            var debt = new Vector2(0.3f, 0f);
            var stand = new Vector2(0f, -0.04f);
            for (int i = 0; i < 30; i++)
                w.Update(Vector2.Zero, Vector2.Zero, 0f, canWalk: true, Dt, stand);
            // The turn: the debt arrives and the feet read differently, the body creeping too fast to count as still.
            for (float t = 0f; t < w.Delay + 3f * Dt; t += Dt)
                w.Update(debt, new Vector2(0.1f, 0f), 0f, canWalk: true, Dt, stand + creep);
            Assert.Equal(StanceWalkPhase.Push, w.State);
            float travel = 0f;
            for (int i = 0; i < 500 && w.State == StanceWalkPhase.Push; i++)
            {
                // The gait steps them: they trail 0.12 m at most.
                var feet = stand + creep - new Vector2(MathF.Min(travel, 0.12f), 0f);
                w.Update(debt, new Vector2(0.5f, 0f), 0f, canWalk: true, Dt, feet);
                debt.X -= 0.5f * Dt;
                travel += 0.5f * Dt;
            }
            return debt.X;
        }
        Assert.Equal(PushAfter(Vector2.Zero), PushAfter(new Vector2(0.15f, 0f)), 3);
    }

    /// <summary>The walk's push ends (the body walks along +x at 0.5 m/s, paying the debt), then the body springs
    /// <paramref name="back"/> of its travel back over 0.3 s while the view moves <paramref name="viewMoves"/> m along
    /// +x, then stands still until the settle is judged.</summary>
    static void PushAndSettle(StanceWalk w, ref Vector2 debt, float back, float viewMoves)
    {
        float travel = 0f;
        for (int i = 0; i < 500 && w.State == StanceWalkPhase.Push; i++)
        {
            w.Update(debt, new Vector2(0.5f, 0f), 0f, canWalk: true, Dt);
            debt.X -= 0.5f * Dt;
            travel += 0.5f * Dt;
        }
        int n = (int)(0.3f / Dt);
        for (int i = 0; i < n && w.State == StanceWalkPhase.Settle; i++)
        {
            w.Update(debt, new Vector2(-back * travel / 0.3f, 0f), 0f, canWalk: true, Dt);
            debt.X += (back * travel + viewMoves) / n;
        }
        for (int i = 0; i < 500 && w.State == StanceWalkPhase.Settle; i++)
            w.Update(debt, Vector2.Zero, 0f, canWalk: true, Dt);
    }

    /// <summary>Holds the debt with the feet still under the body, then pushes the body along +x at 0.5 m/s, its
    /// feet planted (<paramref name="planted"/>) or stepping along under it; the debt when the push ends.</summary>
    static float PushOnFeet(bool planted, bool seen)
    {
        var w = new StanceWalk();
        var debt = new Vector2(0.3f, 0f);
        var stand = new Vector2(0f, -0.04f);
        for (float t = 0f; t < w.Delay + 3f * Dt; t += Dt)
            w.Update(debt, Vector2.Zero, 0f, canWalk: true, Dt, seen ? stand : default);
        Assert.Equal(StanceWalkPhase.Push, w.State);
        float travel = 0f;
        for (int i = 0; i < 500 && w.State == StanceWalkPhase.Push; i++)
        {
            var feet = stand - (planted ? new Vector2(travel, 0f) : Vector2.Zero);
            w.Update(debt, new Vector2(0.5f, 0f), 0f, canWalk: true, Dt, seen ? feet : default);
            debt.X -= 0.5f * Dt;
            travel += 0.5f * Dt;
        }
        Assert.Equal(StanceWalkPhase.Settle, w.State);
        return debt.X;
    }

    [Fact]
    public void FeetLeftBehind_ThePushWalksOnPastTheView_ByAtMostFeetLead()
    {
        // Run 15: follow-up and backward pushes closed to 0.04-0.07 m and sprang back 0.12-0.25 m, each time to the same
        // spot (vr.273: the body back at -373.27, -373.23, -373.23 after three pushes): the feet, which never stepped,
        // and the body settles over them. Walking on while they trail gives the gait time to step them.
        var w = new StanceWalk();
        float coast = 0.5f * (w.CoastSeconds + Dt);
        float stepping = PushOnFeet(planted: false, seen: true);
        Assert.True(stepping > coast - w.Stop - 0.01f, $"feet stepping: ended at {stepping:0.000}");
        Assert.Equal(stepping, PushOnFeet(planted: false, seen: false), 3);
        float behind = PushOnFeet(planted: true, seen: true);
        Assert.True(behind < 0f, $"feet behind: ended at {behind:0.000}, before the view");
        Assert.True(behind > coast - w.FeetLead - 0.01f, $"feet behind: ran on to {behind:0.000}");
    }

    [Fact]
    public void BodyThatSpringsBack_WhileTheHeadDrifts_GivesUp()
    {
        // Every push springs all the way back, while the head drifts 0.035 m toward the body each time: the debt
        // shrinks, so judged by the debt the walk shuffled on for peers. Judged by the body's own headway it gives up.
        var w = new StanceWalk();
        var debt = new Vector2(0.3f, 0f);
        Hold(w, debt, w.Delay + 3f * Dt);
        Assert.Equal(StanceWalkPhase.Push, w.State);
        PushAndSettle(w, ref debt, back: 1f, viewMoves: -0.035f);
        Assert.Equal(StanceWalkPhase.Push, w.State);
        PushAndSettle(w, ref debt, back: 1f, viewMoves: -0.035f);
        Assert.True(w.Blocked);
        Assert.False(w.Walking);
    }

    [Fact]
    public void HeadThatMovesOn_DuringASettle_EndsTheWalk_AndWaitsAFreshDelay()
    {
        // A settle cannot tell a spring-back from the head moving: once the view has moved more than Start since the
        // push ended, the head is doing something new, and the walk waits for it to hold still again.
        var w = new StanceWalk();
        var debt = new Vector2(0.3f, 0f);
        Hold(w, debt, w.Delay + 3f * Dt);
        for (int i = 0; i < 500 && w.State == StanceWalkPhase.Push; i++)
        {
            w.Update(debt, new Vector2(0.5f, 0f), 0f, canWalk: true, Dt);
            debt.X -= 0.5f * Dt;
        }
        Assert.Equal(StanceWalkPhase.Settle, w.State);
        // The head steps 0.1 m aside, the body still.
        for (float t = 0f; t < 0.2f; t += Dt)
        {
            debt.Y += 0.1f * Dt / 0.2f;
            w.Update(debt, Vector2.Zero, 0f, canWalk: true, Dt);
        }
        Assert.False(w.Walking);
        Assert.False(w.Blocked);
        Assert.Equal(Vector2.Zero, Hold(w, debt, w.Delay - 0.3f));
        Assert.NotEqual(Vector2.Zero, Hold(w, debt, 0.5f));
    }

    [Fact]
    public void HeadThatStepsFar_DuringASettle_IsWalkedAtOnce()
    {
        var w = new StanceWalk();
        w.Update(new Vector2(0.5f, 0f), Vector2.Zero, 0f, canWalk: true, Dt);
        w.Update(new Vector2(0.05f, 0f), new Vector2(2f, 0f), 0f, canWalk: true, Dt);
        Assert.Equal(StanceWalkPhase.Settle, w.State);
        // Still coasting, the head steps past the lean limit: no wait for the body to be still.
        var r = w.Update(new Vector2(0f, w.AtOnce + 0.2f), new Vector2(0.5f, 0f), 0f, true, Dt);
        Assert.True(r.Y > 0f, $"{r}");
        Assert.Equal(2, w.Pushes);
    }

    [Fact]
    public void Push_LandingJustPastTheView_HoldsForTheGait()
    {
        // 0.06 m to go at 0.8 m/s: the coast lands 0.037 m past, inside Start. Ending there gave small debts a push too
        // short to step (0.25 s), so the body slid over planted feet and sprang back, a leg twitch for peers.
        var w = new StanceWalk();
        Hold(w, new Vector2(0.1f, 0f), w.Delay + 3f * Dt);
        Assert.NotEqual(
            Vector2.Zero,
            w.Update(new Vector2(0.06f, 0f), new Vector2(0.8f, 0f), 0f, canWalk: true, Dt)
        );
        Assert.Equal(StanceWalkPhase.Push, w.State);
    }

    [Fact]
    public void Pushes_AreCappedPerWalk()
    {
        // Each push makes headway (the body walks and stays), but the head keeps moving away by as much, short of
        // Start each time: the walk stops after MaxPushes instead of shuffling on.
        var w = new StanceWalk();
        var debt = new Vector2(0.3f, 0f);
        Hold(w, debt, w.Delay + 3f * Dt);
        for (int i = 0; i < 20 && w.Walking; i++)
            PushAndSettle(w, ref debt, back: 0f, viewMoves: 0.045f);
        Assert.False(w.Walking);
        Assert.True(w.Pushes <= w.MaxPushes, $"{w.Pushes} pushes");
    }

    [Fact]
    public void Trace_ReportsThePushLength_AndTheSpringBack()
    {
        var w = new StanceWalk();
        var debt = new Vector2(0.3f, 0f);
        Hold(w, debt, w.Delay + 3f * Dt);
        PushAndSettle(w, ref debt, back: 0.5f, viewMoves: 0f);
        Assert.InRange(w.PushSeconds, w.MinPush, 1f);
        Assert.True(w.SpringBack > 0.05f, $"spring-back {w.SpringBack:0.000}");
        Assert.InRange(w.Headway, 0.03f, 0.2f);
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
            Assert.Equal(Vector2.Zero, walk.Update(debt, Vector2.Zero, 0f, canWalk: true, Dt));
            if (own.Update(walk.Walking, 0.15f, new Vector2(30f, 0f), outside: false, Dt))
                follow.Absorb(step, Dt);
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
        for (float t = 0f; t < own.StillSeconds - Dt; t += Dt)
            Assert.True(own.Update(false, 0.02f, look, false, Dt));
        Assert.False(own.Update(false, 0.02f, look, false, Dt));
        // A later drift is not the walk's.
        Assert.False(own.Update(false, 0.2f, look, false, Dt));
    }

    [Fact]
    public void ACoastThroughAStandstill_IsStillTheWalks()
    {
        // Run 10 vr.45 t837.25-837.85, t841.59-841.86: the walked body sprang back toward its planted feet, its speed
        // passing through zero at the turn and creeping under 0.05 m/s at the end; one slow update dropped the coast,
        // and the view followed the rest of the body's travel with no walk running.
        var own = new OwnMotion();
        var look = Vector2.Zero;
        Assert.True(own.Update(true, 0.4f, look, false, Dt));
        Assert.True(own.Update(false, 0.02f, look, false, Dt));
        Assert.True(own.Update(false, 0.15f, look, false, Dt));
        Assert.True(own.Update(false, 0.04f, look, false, Dt));
    }

    [Fact]
    public void ACoast_EndsWithAnOutsideForce()
    {
        // A walk's coast that met a push: once the push's OutsideSeconds are over, its tail is the push's, not the
        // walk's, so it moves the view (the room anchor would otherwise take it and the walk walk it back).
        var own = new OwnMotion();
        var look = Vector2.Zero;
        for (float t = 0f; t < 0.3f; t += Dt)
            own.Update(true, 0.3f, look, false, Dt);
        Assert.False(own.Update(false, 0.3f, look, outside: true, Dt));
        Assert.False(own.Update(false, 0.3f, look, outside: false, Dt));
    }

    [Fact]
    public void ACoast_EndsASecondAfterTheWalk()
    {
        // A body that keeps moving long after its walk is being moved by something else.
        var own = new OwnMotion();
        var look = Vector2.Zero;
        own.Update(true, 0.3f, look, false, Dt);
        for (float t = 0f; t < own.MaxCoastSeconds - 2f * Dt; t += Dt)
            Assert.True(own.Update(false, 0.3f, look, false, Dt));
        for (float t = 0f; t < 3f * Dt; t += Dt)
            own.Update(false, 0.3f, look, false, Dt);
        Assert.False(own.Update(false, 0.3f, look, false, Dt));
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
        // A small look change: under SettleTurnDegrees the pose window is all it gets.
        var own = new OwnMotion();
        var look = Vector2.Zero;
        own.Update(false, 0f, look, false, Dt);
        for (float t = 0f; t < 0.3f; t += Dt)
        {
            look.X = (look.X + 40f * Dt + 360f) % 360f;
            Assert.True(own.Update(false, 0.1f, look, false, Dt));
        }
        for (float t = 0f; t < own.PoseSeconds - 0.05f; t += Dt)
            Assert.True(own.Update(false, 0.1f, look, false, Dt));
        for (float t = 0f; t < 0.1f; t += Dt)
            own.Update(false, 0.1f, look, false, Dt);
        Assert.False(own.Update(false, 0.1f, look, false, Dt));
    }

    /// <summary>An OwnMotion that has just seen the look turn 90 degrees, past its pose window.</summary>
    static OwnMotion AfterATurn(float speed)
    {
        var own = new OwnMotion();
        var look = Vector2.Zero;
        own.Update(false, 0f, look, false, Dt);
        for (float t = 0f; t < 1f; t += Dt)
        {
            look.X += 90f * Dt;
            Assert.True(own.Update(false, speed, look, false, Dt));
        }
        for (float t = 0f; t < own.PoseSeconds + 0.1f; t += Dt)
            Assert.True(own.Update(false, speed, look, false, Dt));
        return own;
    }

    [Fact]
    public void ACreepAfterATurn_IsKept_UntilTheBodyIsStill()
    {
        // Run 20, vr.45b t203.4-205.1: 0.13 m at 0.06-0.14 m/s after the pose window, and vr.273 t225-227: 0.03 m/s.
        var own = AfterATurn(0.1f);
        var look = new Vector2(90f, 0f);
        for (float t = 0f; t < 1.5f; t += Dt)
            Assert.True(own.Update(false, 0.03f, look, false, Dt));
        // A standing body reads 0.002-0.008 m/s.
        for (float t = 0f; t < own.StillSeconds - 2f * Dt; t += Dt)
            Assert.True(own.Update(false, 0.005f, look, false, Dt));
        for (float t = 0f; t < 3f * Dt; t += Dt)
            own.Update(false, 0.005f, look, false, Dt);
        Assert.False(own.Update(false, 0.005f, look, false, Dt));
        // Done: what moves the body next is not the turn's.
        Assert.False(own.Update(false, 0.1f, look, false, Dt));
    }

    [Fact]
    public void ACreepAfterATurn_EndsAtItsLongest()
    {
        var own = AfterATurn(0.05f);
        var look = new Vector2(90f, 0f);
        for (float t = 0f; t < own.SettleMaxSeconds; t += Dt)
            own.Update(false, 0.05f, look, false, Dt);
        Assert.False(own.Update(false, 0.05f, look, false, Dt));
    }

    [Fact]
    public void AFastBodyAfterATurn_IsNotTheTurns()
    {
        // A body moving faster than a pose settles is something else's: it goes in the view.
        var own = AfterATurn(0.1f);
        Assert.False(own.Update(false, own.SettleMaxSpeed + 0.1f, new Vector2(90f, 0f), false, Dt));
        Assert.False(own.Update(false, 0.1f, new Vector2(90f, 0f), false, Dt));
    }

    [Fact]
    public void AnOutsideForceAfterATurn_EndsTheCreep()
    {
        // vr.304: an outside force's creep carries the view, even right after a turn.
        var own = AfterATurn(0.1f);
        var look = new Vector2(90f, 0f);
        Assert.False(own.Update(false, 0.1f, look, outside: true, Dt));
        Assert.False(own.Update(false, 0.1f, look, outside: false, Dt));
    }

    [Fact]
    public void SmallLookWobbles_DoNotAddUpToACreep()
    {
        // Separate small looks are each under SettleTurnDegrees: a creep that small stays inside the view hold.
        var own = new OwnMotion();
        var look = Vector2.Zero;
        own.Update(false, 0f, look, false, Dt);
        for (int i = 0; i < 5; i++)
        {
            for (float t = 0f; t < 0.1f; t += Dt)
            {
                look.X += 60f * Dt;
                own.Update(false, 0.05f, look, false, Dt);
            }
            for (float t = 0f; t < own.PoseSeconds + 0.1f; t += Dt)
                own.Update(false, 0.05f, look, false, Dt);
            Assert.False(own.Update(false, 0.05f, look, false, Dt));
        }
    }
}
