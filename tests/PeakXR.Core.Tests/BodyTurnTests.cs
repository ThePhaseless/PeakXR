namespace PeakXR.Core.Tests;

public class BodyTurnTests
{
    const float Dt = 0.02f;

    /// <summary>vr.302's glance: 70 degrees out in 0.35 s, 0.3 s there, back in 0.35 s.</summary>
    static float Glance(float t)
    {
        float k =
            t < 0.35f ? t / 0.35f
            : t < 0.65f ? 1f
            : t < 1f ? 1f - (t - 0.65f) / 0.35f
            : 0f;
        return 70f * k * k * (3f - 2f * k);
    }

    static float Run(BodyTurn turn, float look, float seconds, bool free = true)
    {
        for (float t = 0f; t < seconds; t += Dt)
            turn.Update(look, free, Dt);
        return turn.Heading;
    }

    [Fact]
    public void TheFirstUpdate_StandsOnTheLook()
    {
        var turn = new BodyTurn();
        Assert.True(float.IsNaN(turn.Heading));
        turn.Update(40f, true, Dt);
        Assert.Equal(40f, turn.Heading);
    }

    [Fact]
    public void AGlance_LeavesTheBodyWhereItStands()
    {
        var turn = new BodyTurn();
        turn.Update(0f, true, Dt);
        for (float t = 0f; t < 1f; t += Dt)
            Assert.Equal(0f, turn.Update(Glance(t), true, Dt));
        Assert.Equal(0f, Run(turn, 0f, 3f));
    }

    [Fact]
    public void AGlance_AfterALongStillStand_StartsNoTurn()
    {
        // Quiet for long, the first degrees of a glance must not read as the head settling: run W, 41 deg/s of body turn
        // armed BodyHold at every glance.
        var turn = new BodyTurn();
        turn.Update(0f, true, Dt);
        Run(turn, 0f, 3f);
        for (float t = 0f; t < 1f; t += Dt)
            Assert.Equal(0f, turn.Update(Glance(t), true, Dt));
    }

    [Fact]
    public void ASmallLook_LeavesTheBodyWhereItStands()
    {
        // A real head never holds still: a body chasing it would never have its feet at rest.
        var turn = new BodyTurn();
        turn.Update(0f, true, Dt);
        Run(turn, 0f, 3f);
        Assert.Equal(0f, Run(turn, turn.QuietDegrees - 1f, 3f));
    }

    [Fact]
    public void AGlanceRepeating_NeverTurnsTheBody()
    {
        // vr.305 repeats the glance through 4 s of wind.
        var turn = new BodyTurn();
        turn.Update(0f, true, Dt);
        for (float t = 0f; t < 6f; t += Dt)
            Assert.Equal(0f, turn.Update(Glance(t % 1f), true, Dt));
    }

    [Fact]
    public void AHeadHeldTurned_BringsTheBodyAfterTheQuietSeconds()
    {
        var turn = new BodyTurn();
        turn.Update(0f, true, Dt);
        Assert.Equal(0f, Run(turn, 60f, turn.QuietSeconds - 2f * Dt));
        float heading = Run(turn, 60f, 0.25f);
        Assert.InRange(heading, 1f, 59f);
        Assert.Equal(60f, Run(turn, 60f, 60f / turn.Rate));
    }

    [Fact]
    public void TheBody_TurnsNoFasterThanItsRate()
    {
        var turn = new BodyTurn();
        turn.Update(0f, true, Dt);
        Run(turn, 60f, turn.QuietSeconds + Dt);
        float before = turn.Heading;
        turn.Update(60f, true, Dt);
        Assert.InRange(turn.Heading - before, 0f, turn.Rate * Dt + 1e-3f);
    }

    [Fact]
    public void ASlowDrift_CountsAsQuiet_AtAnyFrameRate()
    {
        // Measured over a band, not per step: at 25 fps a physics step carries a frame or nothing.
        var turn = new BodyTurn();
        turn.Update(0f, true, Dt);
        float look = 0f;
        for (float t = 0f; t < 4f; t += Dt)
        {
            if ((int)(t / Dt) % 2 == 0)
                look += 3f * 2f * Dt;
            turn.Update(look, true, Dt);
        }
        Assert.NotEqual(0f, turn.Heading);
    }

    [Fact]
    public void ALookMovingAgain_StopsTheBodyWhereItIs()
    {
        var turn = new BodyTurn();
        turn.Update(0f, true, Dt);
        float heading = Run(turn, 60f, turn.QuietSeconds + 0.2f);
        Assert.True(heading > 0f);
        Assert.Equal(heading, turn.Update(40f, true, Dt));
    }

    [Fact]
    public void PastTheNeck_TheBodyTurnsAtOnce_UntilBackWithinIt()
    {
        var turn = new BodyTurn();
        turn.Update(0f, true, Dt);
        Assert.Equal(turn.Rate * Dt, turn.Update(180f - 1f, true, Dt), 3);
        float heading = Run(turn, 179f, 0.9f);
        Assert.Equal(179f - heading, turn.MaxOff, turn.Rate * Dt);
    }

    [Fact]
    public void TheShortWayRound_AcrossTheWrap()
    {
        var turn = new BodyTurn();
        turn.Update(170f, true, Dt);
        Run(turn, -170f, turn.QuietSeconds + 1f);
        Assert.Equal(0f, BodySlotLayout.DeltaAngle(turn.Heading, -170f), 3);
    }

    [Fact]
    public void NotFree_CatchesUpFast()
    {
        // The stick or a walk moves the body along its look, and the air has no feet to load.
        var turn = new BodyTurn();
        turn.Update(0f, true, Dt);
        Assert.Equal(0f, Run(turn, 70f, 0.1f));
        Assert.Equal(70f, Run(turn, 70f, 70f / turn.CatchUpRate + 2f * Dt, free: false));
    }

    [Fact]
    public void ARigTurn_TurnsTheBodyWithIt_AtOnce()
    {
        // A snap or smooth turn is the player turning the whole body, with the hands: a body left behind has its arms
        // swung across it, and creeps 0.1 m over its feet (vr.300).
        var turn = new BodyTurn();
        turn.Update(0f, true, Dt);
        Assert.Equal(45f, turn.Update(45f, true, Dt, 45f));
        Assert.Equal(45f, Run(turn, 45f, 2f));
    }

    [Fact]
    public void ARigTurn_UnderATurnedHead_KeepsTheHeadTurned()
    {
        var turn = new BodyTurn();
        turn.Update(0f, true, Dt);
        turn.Update(60f, true, Dt);
        Assert.Equal(45f, turn.Update(105f, true, Dt, 45f));
    }

    [Fact]
    public void ARigTurnsLaggingLook_LeavesNoGapOnceSettled()
    {
        // The look follows the rig a physics step late, so the body reads a gap under the band for that step. Left there,
        // smooth 180's drawn eyes read 0.076-0.097 m off the view (run Y).
        var turn = new BodyTurn();
        turn.Update(0f, true, Dt);
        Run(turn, 0f, 3f);
        turn.Update(0f, true, Dt, 45f);
        turn.Update(45f, true, Dt);
        Assert.Equal(45f, Run(turn, 45f, turn.QuietSeconds + 1f));
    }

    [Fact]
    public void Reset_StandsOnTheNextLook()
    {
        var turn = new BodyTurn();
        turn.Update(0f, true, Dt);
        turn.Reset();
        Assert.True(float.IsNaN(turn.Heading));
        turn.Update(90f, true, Dt);
        Assert.Equal(90f, turn.Heading);
    }

    [Fact]
    public void AZeroStep_ChangesNothing()
    {
        var turn = new BodyTurn();
        turn.Update(0f, true, Dt);
        Assert.Equal(0f, turn.Update(179f, false, 0f));
    }

    [Fact]
    public void NotFree_EndsOnTheLook_NotOnTheSpotTheHeadRested()
    {
        // The stick walks along the look, inside the band too: a body left on the rest spot walks up to 5 degrees sideways.
        var turn = new BodyTurn();
        turn.Update(0f, true, Dt);
        Run(turn, 0f, turn.QuietSeconds + Dt);
        Assert.Equal(4f, turn.Update(4f, false, Dt));
    }

    [Fact]
    public void FreeAgain_AfterACatchUp_StaysWhereItCaughtUp()
    {
        // A rest spot from before the catch-up would turn a standing body back to it, for no move of the head.
        var turn = new BodyTurn();
        turn.Update(0f, true, Dt);
        Run(turn, 0f, turn.QuietSeconds + Dt);
        Run(turn, 4f, 2f * Dt, free: false);
        Assert.Equal(4f, Run(turn, 4f, 1f));
    }

    [Fact]
    public void AStillHead_JitteringAcrossTheWrap_IsQuiet()
    {
        // +179 to -179 is 2 degrees, not 358: read as a move, the head never comes to rest and the body stops 80 short.
        var turn = new BodyTurn();
        turn.Update(0f, true, Dt);
        bool plus = false;
        for (float t = 0f; t < 3f; t += Dt, plus = !plus)
            turn.Update(plus ? 179f : -179f, true, Dt);
        Assert.InRange(MathF.Abs(BodySlotLayout.DeltaAngle(turn.Heading, 180f)), 0f, 1.1f);
    }

    [Theory]
    [InlineData(0.01f)]
    [InlineData(0.04f)]
    public void TheQuietClock_ReadsSeconds_AtAnyStep(float dt)
    {
        var turn = new BodyTurn();
        turn.Update(0f, true, dt);
        for (float t = 0f; t < turn.QuietSeconds - 2f * dt; t += dt)
            Assert.Equal(0f, turn.Update(60f, true, dt));
        for (float t = 0f; t < 0.2f; t += dt)
            turn.Update(60f, true, dt);
        Assert.InRange(turn.Heading, 1f, 59f);
    }

    [Fact]
    public void AStickStart_UnderATurnedHead_FacesTheLookWithinAStride()
    {
        // The game walks along the look: a stick pressed mid glance must not walk the body sideways for long.
        var turn = new BodyTurn();
        turn.Update(0f, true, Dt);
        Run(turn, 70f, 0.5f);
        Assert.Equal(0f, turn.Heading);
        Assert.Equal(70f, Run(turn, 70f, 0.11f, free: false));
    }

    [Fact]
    public void TheRigsYaw_TurnsTheBodyOnce_AndNowBetweenSteps()
    {
        // The heading changes on a physics step and the pose reads it on every frame: read late, a 45 degree snap reads as
        // a 17 degree lean (run U2). Counted twice, the body ends past the look.
        var turn = new BodyTurn();
        turn.Update(0f, true, Dt, turn.TurnRig(10f));
        Assert.Equal(40f, turn.HeadingAt(50f));
        turn.Update(40f, true, Dt, turn.TurnRig(50f));
        Assert.Equal(40f, turn.Heading);
        Assert.Equal(40f, turn.HeadingAt(50f));
        Assert.Equal(25f, turn.HeadingAt(35f));
    }

    [Fact]
    public void TheRigsYaw_KeepsCounting_ThroughAReset()
    {
        // Climbing leaves the heading unset while the rig still turns: the first step back must not take that for a turn.
        var turn = new BodyTurn();
        turn.Update(0f, true, Dt, turn.TurnRig(0f));
        turn.Reset();
        Assert.True(float.IsNaN(turn.HeadingAt(90f)));
        turn.TurnRig(90f);
        turn.Update(90f, true, Dt, turn.TurnRig(90f));
        Assert.Equal(90f, turn.HeadingAt(90f));
    }

    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(0f, 6f, 0f)]
    [InlineData(0f, -6f, 0f)]
    [InlineData(0f, 70f, 64f)]
    [InlineData(0f, -70f, -64f)]
    [InlineData(170f, -170f, 14f)]
    public void TheHead_TakesTheLook_PastTheEyeSamplesBand(float heading, float look, float neck)
    {
        Assert.Equal(neck, BodyTurn.HeadOnNeck(heading, look), 3);
    }

    [Fact]
    public void WithoutAHeading_TheHeadStaysOnTheBody()
    {
        Assert.Equal(0f, BodyTurn.HeadOnNeck(float.NaN, 70f));
    }
}
