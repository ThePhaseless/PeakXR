namespace PeakXR.Core.Tests;

public class RopePullTests
{
    static RopePull Make() =>
        new RopePull
        {
            GrabRadius = 0.15f,
            MetresPerTick = 0.25f,
            Gain = 1f,
        };

    [Fact]
    public void GripClosingNearSpool_StartsPull_WithoutScroll()
    {
        var p = Make();
        Assert.Equal(0f, p.Update(true, 0.1f, 0.1f, out int ticks));
        Assert.True(p.Active);
        Assert.Equal(0, ticks);
    }

    [Fact]
    public void GripClosingFarFromSpool_DoesNothing()
    {
        var p = Make();
        p.Update(true, 0.3f, 0.1f, out _);
        Assert.False(p.Active);
        Assert.Equal(0f, p.Update(true, 0.1f, 0.1f, out _));
        Assert.False(p.Active);
    }

    [Fact]
    public void PullingAway_PaysOut_PushingBack_ReelsIn()
    {
        var p = Make();
        p.Update(true, 0.1f, 0.1f, out _);
        float payout = p.Update(true, 0.2f, 0.1f, out _);
        TestUtil.Near(1f / RopePull.MetresPerSecondPerScroll, payout, 1e-4f);
        float reel = p.Update(true, 0.15f, 0.1f, out _);
        TestUtil.Near(-0.5f / RopePull.MetresPerSecondPerScroll, reel, 1e-4f);
    }

    [Fact]
    public void Scroll_IsClampedToTheSpoolsRange()
    {
        var p = Make();
        p.Update(true, 0.1f, 0.01f, out _);
        Assert.Equal(RopePull.MaxScroll, p.Update(true, 1.1f, 0.01f, out _));
        Assert.Equal(RopePull.MinScroll, p.Update(true, 0.1f, 0.01f, out _));
    }

    [Fact]
    public void Ticks_FollowRopeLength_BothWays()
    {
        var p = Make();
        p.Update(true, 0.1f, 0.1f, out _);
        p.Update(true, 0.3f, 0.1f, out int a);
        Assert.Equal(0, a);
        p.Update(true, 0.4f, 0.1f, out int b);
        Assert.Equal(1, b);
        p.Update(true, 0.0f, 0.1f, out int c);
        Assert.Equal(-1, c);
    }

    [Fact]
    public void Gain_ScalesPayout()
    {
        var p = Make();
        p.Gain = 2f;
        p.Update(true, 0.1f, 0.1f, out _);
        float s = p.Update(true, 0.15f, 0.1f, out _);
        TestUtil.Near(1f / RopePull.MetresPerSecondPerScroll, s, 1e-4f);
    }

    [Fact]
    public void ReleasingGrip_EndsPull_AndNeedsAFreshClose()
    {
        var p = Make();
        p.Update(true, 0.1f, 0.1f, out _);
        Assert.Equal(0f, p.Update(false, 0.1f, 0.1f, out _));
        Assert.False(p.Active);
        // Grip held while moving into reach does not start a pull: only a fresh close does.
        p.Update(true, 0.5f, 0.1f, out _);
        p.Update(true, 0.1f, 0.1f, out _);
        Assert.False(p.Active);
    }

    [Fact]
    public void ZeroDeltaTime_GivesNoScroll()
    {
        var p = Make();
        p.Update(true, 0.1f, 0.1f, out _);
        Assert.Equal(0f, p.Update(true, 0.5f, 0f, out _));
    }
}

public class HammerCastTests
{
    const float Cast = 1.5f;
    const float Dt = 0.02f;

    static bool Completes(int strikes, float elapsed) =>
        HammerCast.TickBeforeUpdate(strikes, 3, Cast, Dt, elapsed) + Dt >= Cast;

    [Fact]
    public void NoStrikes_HoldPlacesAtTheGamesTime()
    {
        // External review #7: hammering must never be slower than holding the button.
        TestUtil.Near(0.7f, HammerCast.TickBeforeUpdate(0, 3, Cast, Dt, 0.7f), 1e-5f);
        Assert.False(Completes(0, 1.4f));
        Assert.True(Completes(0, 1.49f));
    }

    [Fact]
    public void PartialStrikes_NeverSlowTheHold()
    {
        for (float e = 0f; e < Cast; e += 0.05f)
            Assert.True(HammerCast.TickBeforeUpdate(2, 3, Cast, Dt, e) >= e - 1e-5f);
    }

    [Fact]
    public void AllStrikes_PlaceSooner_UpToTheCap()
    {
        float earliest = Cast / HammerCast.MaxSpeedup;
        // All strikes in early: the spike goes in at the capped time, sooner than the hold.
        Assert.False(Completes(3, earliest - Dt - 0.01f));
        Assert.True(Completes(3, earliest));
        Assert.True(earliest < Cast);
    }

    [Fact]
    public void Strikes_NeverBeatTheCap()
    {
        for (int strikes = 0; strikes <= 9; strikes++)
        {
            for (float e = 0f; e < Cast; e += 0.01f)
            {
                if (Completes(strikes, e))
                    Assert.True(e * HammerCast.MaxSpeedup + Dt >= Cast - 1e-4f);
            }
        }
    }

    [Fact]
    public void PartialStrikes_DoNotComplete_BeforeTheHoldTime()
    {
        Assert.False(Completes(2, 1.2f));
        // A long frame must not finish it before the last strike either.
        Assert.True(HammerCast.TickBeforeUpdate(2, 3, Cast, 1f, 0.2f) + 1f < Cast);
    }

    [Fact]
    public void NoCastTime_StaysAboveZero() =>
        Assert.True(HammerCast.TickBeforeUpdate(3, 3, 0f, Dt, 0.5f) > 0f);
}

public class GliderSteerTests
{
    [Fact]
    public void ArmsSpreadLevel_SteersPerpendicularToTheArms()
    {
        Assert.True(
            GliderSteer.TryForward(
                new Vector3(-0.6f, 1f, 0f),
                new Vector3(0.6f, 1f, 0f),
                0.3f,
                0f,
                45f,
                out var f
            )
        );
        TestUtil.Near(new Vector3(0f, 0f, 1f), f, 1e-4f);
    }

    [Fact]
    public void RotatingTheArms_RotatesTheHeading()
    {
        // Arms along -Z..+Z: left hand at +Z, right at -Z means facing +X.
        Assert.True(
            GliderSteer.TryForward(
                new Vector3(0f, 1f, 0.6f),
                new Vector3(0f, 1f, -0.6f),
                0.3f,
                0f,
                45f,
                out var f
            )
        );
        TestUtil.Near(new Vector3(1f, 0f, 0f), f, 1e-4f);
    }

    [Fact]
    public void LoweringTheRightHand_TurnsRight()
    {
        float drop = MathF.Tan(20f * UnityQuat.Deg2Rad) * 1.2f;
        Assert.True(
            GliderSteer.TryForward(
                new Vector3(-0.6f, 1f, 0f),
                new Vector3(0.6f, 1f - drop, 0f),
                0.3f,
                1f,
                45f,
                out var f
            )
        );
        float yaw = MathF.Atan2(f.X, f.Z) * UnityQuat.Rad2Deg;
        TestUtil.Near(20f, yaw, 0.05f);
        TestUtil.Near(0f, f.Y);
    }

    [Fact]
    public void Bank_IsClamped()
    {
        Assert.True(
            GliderSteer.TryForward(
                new Vector3(-0.3f, 1.5f, 0f),
                new Vector3(0.3f, 0.5f, 0f),
                0.3f,
                1f,
                30f,
                out var f
            )
        );
        TestUtil.Near(30f, MathF.Atan2(f.X, f.Z) * UnityQuat.Rad2Deg, 0.05f);
    }

    [Fact]
    public void HandsTogether_GivesNoHeading()
    {
        Assert.False(
            GliderSteer.TryForward(
                new Vector3(0f, 1f, 0f),
                new Vector3(0.1f, 1f, 0f),
                0.3f,
                1f,
                45f,
                out _
            )
        );
    }
}
