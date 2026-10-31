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
        Assert.True(Vector2.Distance(new Vector2(0f, 1f), far) < 1e-4f, $"{far}");
        Assert.True(w.Walking);
        // Ramps down as it closes.
        var near = w.Update(new Vector2(0f, 0.1f), Vector2.Zero, 0f, canWalk: true, Dt);
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
        // Settling: no input, and the body's coast still belongs to the walk until it has been still a while.
        Assert.Equal(
            Vector2.Zero,
            w.Update(new Vector2(0.01f, 0f), new Vector2(0.3f, 0f), 0f, canWalk: true, Dt)
        );
        Assert.True(w.Walking);
        Hold(w, new Vector2(0.01f, 0f), w.StillSeconds + Dt);
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
        Assert.Equal(Vector2.Zero, w.Update(stuck, Vector2.Zero, 0f, canWalk: true, Dt));
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
        // The body coasted 0.15 m past the view over 1 s (its travel, so the view stood still), then stopped: the same
        // walk walks it back, its headway counted from where it turned, not from the closest the overshoot came.
        var w = new StanceWalk();
        w.Update(new Vector2(0.5f, 0f), Vector2.Zero, 0f, canWalk: true, Dt);
        w.Update(Vector2.Zero, new Vector2(1f, 0f), 0f, canWalk: true, Dt);
        Overshoot(w);
        Assert.True(WalksBack(w, new Vector2(-0.15f, 0f)), "no walk back once still");
    }

    /// <summary>The body coasts on 0.15 m past the view over 1 s at 0.15 m/s, the view still.</summary>
    static void Overshoot(StanceWalk w)
    {
        for (float t = Dt; t <= 1f + 1e-4f; t += Dt)
            w.Update(new Vector2(-0.15f * t, 0f), new Vector2(0.15f, 0f), 0f, canWalk: true, Dt);
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
        // Then the body coasts 0.15 m past the view for 1 s and is walked back slowly, as in OvershotWalk_WalksBack.
        w.Update(Vector2.Zero, new Vector2(1f, 0f), 0f, canWalk: true, Dt);
        Overshoot(w);
        Assert.True(WalksBack(w, new Vector2(-0.15f, 0f)), "no walk back once still");
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
    /// Walks <paramref name="d0"/> m off with the game-like body of <see cref="WalkBody"/> standing on planted feet
    /// (run 10, vr.45 t840.54-841.59: a walk of a few frames slid the body 0.154 m and it came back 0.128 m with no
    /// input). The game's gait steps only for a move input held a while (CharacterAnimations.cs:185-196 damps it over
    /// 0.125 s and snaps it to 0 under 0.125): the feet follow the body once a request over 0.15 has lasted 0.25 s, and
    /// otherwise pull it back toward them with a 1/3 s time constant. Returns the walks, what is left, whether it gave
    /// up, the most the request switched on in one second (a push held for the gait switches on once), the pushes,
    /// and whether it still walks at the end.
    /// Assumption, not measured: the 0.25 s of held input before the feet step (<paramref name="stepTime"/>) and that
    /// 0.2 of the stick (<see cref="StanceWalk.MinInput"/>) steps them at all. The headset run's stance trace (push
    /// length, headway, spring-back) measures both (#10).
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
                pushed = late.Length() > 0.15f ? pushed + h : 0f;
                bool stepping = pushed >= stepTime;
                if (stepping)
                    feet = p;
                var step = late - eased;
                float max = 2f * h;
                eased += step.Length() > max ? step / step.Length() * max : step;
                var target = eased * 2f + (stepping ? Vector2.Zero : (feet - p) * 3f);
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
        var w = new StanceWalk();
        foreach (float hz in new[] { 33f, 72f })
        foreach (float d0 in new[] { 0.06f, 0.08f, 0.12f, 0.26f, 0.6f })
        {
            var (walks, left, blocked, ons, _, walking) = WalkPlanted(d0, hz);
            Assert.True(left < w.Start, $"{hz} Hz, {d0}: left {left:0.000} m");
            Assert.Equal(1, walks);
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
                blocked || left < w.Start,
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
        // Stopped short, past the start distance: once still, the same walk pushes again, with no new delay.
        Assert.Equal(Vector2.Zero, Hold(w, new Vector2(0.15f, 0f), w.StillSeconds - 2f * Dt));
        Assert.NotEqual(Vector2.Zero, Hold(w, new Vector2(0.15f, 0f), 3f * Dt));
        Assert.Equal(2, w.Pushes);
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
