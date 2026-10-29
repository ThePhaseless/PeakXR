namespace PeakXR.Core.Tests;

public class BodyStanceTests
{
    // The game turns the body about its mass centre, its eyes 0.236 m ahead of it; a real head turns about the neck,
    // its eyes 0.1 m ahead. Rig facing +Z.
    const float Eyes = 0.236f;
    const float Tol = 1e-4f;
    static readonly Vector3 Rig = Vector3.UnitZ;

    static Vector3 Facing(float yaw)
    {
        var v = YawMath.Rotate(new Vector2(0f, 1f), yaw);
        return new Vector3(v.X, 0f, v.Y);
    }

    /// <summary>The debt of a body at the origin facing <paramref name="yaw"/>, with nothing walked yet.</summary>
    static Vector3 TurnDebt(float yaw, float back = 0f) =>
        BodyStance.Debt(
            Rig * Eyes,
            Facing(yaw) * Eyes,
            Vector3.Zero,
            Rig,
            Facing(yaw),
            BodyStance.NeckToEyes,
            back
        );

    [Fact]
    public void FacingTheRig_OwesNothing()
    {
        Assert.True(TurnDebt(0f).Length() < Tol, $"{TurnDebt(0f)}");
    }

    [Fact]
    public void TurnedAround_WalksForwardTwiceTheEyesPastTheNeck()
    {
        // Turned 180 degrees about the mass centre, the body's eyes end 2 x 0.236 m back while the real eyes swing
        // only 2 x 0.1 m back round the neck: the body walks the difference forward.
        var want = new Vector3(0f, 0f, 2f * (Eyes - BodyStance.NeckToEyes));
        Assert.True(Vector3.Distance(want, TurnDebt(180f)) < Tol, $"{TurnDebt(180f)}");
    }

    [Fact]
    public void Turn_PutsTheBodyEyesUnderTheRealEyes()
    {
        // Body at the origin; real eyes start on the body's eyes at +Z and swing round a neck 0.1 m behind them.
        var neck = Rig * (Eyes - BodyStance.NeckToEyes);
        foreach (float yaw in new[] { 90f, -90f, 135f })
        {
            var view = neck + Facing(yaw) * BodyStance.NeckToEyes;
            var walked = Facing(yaw) * Eyes + TurnDebt(yaw);
            Assert.True(Vector3.Distance(view, walked) < Tol, $"{yaw}: {walked} vs {view}");
        }
    }

    [Fact]
    public void WalkedShift_PaysTheDebt()
    {
        // The view anchor keeps the body's travel as a shift: walking the debt leaves nothing owed.
        var owed = TurnDebt(90f);
        var after = BodyStance.Debt(
            Rig * Eyes + owed,
            Facing(90f) * Eyes + owed,
            -owed,
            Rig,
            Facing(90f),
            BodyStance.NeckToEyes,
            0f
        );
        Assert.True(after.Length() < Tol, $"{after}");
    }

    [Fact]
    public void Debt_IsLevel()
    {
        var d = BodyStance.Debt(
            new Vector3(0f, 1.6f, 0.2f),
            new Vector3(0f, 1.2f, 0.2f),
            new Vector3(0f, 0.3f, 0f),
            Rig,
            Rig,
            BodyStance.NeckToEyes,
            0f
        );
        Assert.True(d.Length() < Tol, $"{d}");
    }

    [Fact]
    public void LookingDown_StepsBackAwayFromTheLook()
    {
        var back = TurnDebt(90f, 0.2f) - TurnDebt(90f);
        Assert.True(Vector3.Distance(new Vector3(-0.2f, 0f, 0f), back) < Tol, $"{back}");
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

    [Fact]
    public void NeckSwing_IsTheHeadsEyesRoundItsNeck()
    {
        const float n = BodyStance.NeckToEyes;
        Assert.True(BodyStance.NeckSwing(0f).Length() < Tol);
        var turned = BodyStance.NeckSwing(180f);
        Assert.True(Vector2.Distance(new Vector2(0f, -2f * n), turned) < Tol, $"{turned}");
        var right = BodyStance.NeckSwing(90f);
        Assert.True(Vector2.Distance(new Vector2(n, -n), right) < Tol, $"{right}");
    }
}

public class StanceWalkTests
{
    const float Dt = 1f / 90f;

    [Fact]
    public void SmallDebt_StaysPut()
    {
        var w = new StanceWalk();
        Assert.Equal(Vector2.Zero, w.Update(new Vector2(0.04f, 0f), canWalk: true, Dt));
        Assert.False(w.Walking);
    }

    [Fact]
    public void Debt_WalksAlongItAndRamps()
    {
        var w = new StanceWalk();
        var far = w.Update(new Vector2(0f, 1f), canWalk: true, Dt);
        Assert.True(Vector2.Distance(new Vector2(0f, 1f), far) < 1e-4f, $"{far}");
        var near = w.Update(new Vector2(0f, 0.1f), canWalk: true, Dt);
        Assert.True(MathF.Abs(near.X) < 1e-4f, $"{near}");
        Assert.InRange(near.Y, 0.05f, 0.9f);
        Assert.True(w.Walking);
    }

    [Fact]
    public void Walk_StopsOnlyInsideTheStopDistance()
    {
        var w = new StanceWalk();
        w.Update(new Vector2(0.2f, 0f), canWalk: true, Dt);
        // Inside the start but outside the stop distance: a started walk carries on.
        Assert.NotEqual(Vector2.Zero, w.Update(new Vector2(0.04f, 0f), canWalk: true, Dt));
        Assert.Equal(Vector2.Zero, w.Update(new Vector2(0.01f, 0f), canWalk: true, Dt));
        // Settling: the body's coast still belongs to the walk.
        Assert.True(w.Walking);
        for (float t = 0f; t <= w.SettleSeconds + Dt; t += Dt)
            w.Update(new Vector2(0.01f, 0f), canWalk: true, Dt);
        Assert.False(w.Walking);
    }

    [Fact]
    public void CannotWalk_RequestsNothingAndOwnsNoTravel()
    {
        var w = new StanceWalk();
        w.Update(new Vector2(0.2f, 0f), canWalk: true, Dt);
        Assert.Equal(Vector2.Zero, w.Update(new Vector2(0.2f, 0f), canWalk: false, Dt));
        // Stick walking moves the view with the body: none of that travel is the stance walk's.
        Assert.False(w.Walking);
    }

    [Fact]
    public void BlockedWalk_GivesUpUntilTheDebtChanges()
    {
        var w = new StanceWalk();
        var stuck = new Vector2(0.2f, 0f);
        for (float t = 0f; t < w.StallSeconds + 0.1f; t += Dt)
            w.Update(stuck, canWalk: true, Dt);
        Assert.True(w.Blocked);
        Assert.Equal(Vector2.Zero, w.Update(stuck, canWalk: true, Dt));
        Assert.False(w.Walking);
        // A new turn changes what is owed: try again.
        Assert.NotEqual(Vector2.Zero, w.Update(new Vector2(0.2f, 0.1f), canWalk: true, Dt));
        Assert.False(w.Blocked);
    }

    [Fact]
    public void WalkedBody_EndsUnderTheView()
    {
        // A body that walks after its request like the game's driven ragdoll (speed eases toward input x 1.5 m/s
        // with a 0.12 s time constant, as RoomScaleTests.Walk), its travel paid off the debt.
        var w = new StanceWalk();
        var debt = new Vector2(0.19f, -0.19f);
        var v = Vector2.Zero;
        float maxAfter = 0f;
        for (float t = 0f; t < 4f; t += Dt)
        {
            var request = w.Update(debt, canWalk: true, Dt);
            v += (request * 1.5f - v) * (1f - MathF.Exp(-Dt / 0.12f));
            debt -= v * Dt;
            if (t > 2f)
                maxAfter = MathF.Max(maxAfter, debt.Length());
        }
        Assert.True(maxAfter < 0.05f, $"left {maxAfter:0.000} m off the view");
        Assert.False(w.Walking);
    }
}
