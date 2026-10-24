namespace PeakXR.Core.Tests;

public class ContactDwellTests
{
    [Fact]
    public void ActiveOnlyAfterDwellInside()
    {
        var c = new ContactDwell
        {
            Enter = 0.1f,
            Exit = 0.2f,
            Dwell = 0.2f,
        };
        Assert.False(c.Update(0.05f, 0.1f));
        Assert.True(c.Update(0.05f, 0.1f));
        Assert.True(c.Active);
    }

    [Fact]
    public void StaysActiveUntilPastExit()
    {
        var c = new ContactDwell
        {
            Enter = 0.1f,
            Exit = 0.2f,
            Dwell = 0f,
        };
        Assert.True(c.Update(0.05f, 0.1f));
        Assert.True(c.Update(0.18f, 0.1f));
        Assert.False(c.Update(0.21f, 0.1f));
        // Back between Enter and Exit is not inside again until under Enter.
        Assert.False(c.Update(0.15f, 0.1f));
    }

    [Fact]
    public void LeavingRestartsDwell()
    {
        var c = new ContactDwell
        {
            Enter = 0.1f,
            Exit = 0.1f,
            Dwell = 0.3f,
        };
        c.Update(0f, 0.2f);
        c.Update(1f, 0.1f);
        Assert.False(c.Update(0f, 0.2f));
        Assert.True(c.Update(0f, 0.2f));
    }

    [Fact]
    public void Disarmed_ExistingContactMustBreakFirst()
    {
        var c = new ContactDwell
        {
            Enter = 0.1f,
            Exit = 0.2f,
            Dwell = 0f,
        };
        c.Disarm();
        Assert.False(c.Update(0f, 0.1f));
        Assert.False(c.Update(0f, 0.1f));
        Assert.False(c.Update(0.5f, 0.1f));
        Assert.True(c.Update(0f, 0.1f));
    }

    [Fact]
    public void Disarmed_NoContactArmsAtOnce()
    {
        var c = new ContactDwell { Dwell = 0f };
        c.Disarm();
        Assert.False(c.Update(1f, 0.1f));
        Assert.True(c.Update(0f, 0.1f));
    }
}

public class HeatZoneTests
{
    static readonly Vector3 Fire = new(10f, 2f, -3f);

    [Fact]
    public void InsideCylinder_IsZero()
    {
        Assert.Equal(
            0f,
            HeatZone.Distance(Fire + new Vector3(0.3f, 0.5f, 0f), Fire, 0.6f, 0.2f, 1f)
        );
    }

    [Fact]
    public void BesideCylinder_IsRadialGap()
    {
        TestUtil.Near(
            0.4f,
            HeatZone.Distance(Fire + new Vector3(0f, 0.5f, 1f), Fire, 0.6f, 0.2f, 1f)
        );
    }

    [Fact]
    public void InTheFlames_BelowMin_IsOutside()
    {
        TestUtil.Near(
            0.15f,
            HeatZone.Distance(Fire + new Vector3(0f, 0.05f, 0f), Fire, 0.6f, 0.2f, 1f)
        );
    }

    [Fact]
    public void AboveAndBeside_IsCornerDistance()
    {
        TestUtil.Near(
            0.5f,
            HeatZone.Distance(Fire + new Vector3(0.9f, 1.4f, 0f), Fire, 0.6f, 0.2f, 1f)
        );
    }

    [Fact]
    public void Nearest_Inside_IsThePointItself()
    {
        var p = Fire + new Vector3(0.3f, 0.5f, 0f);
        Assert.Equal(p, HeatZone.Nearest(p, Fire, 0.6f, 0.2f, 1f));
    }

    [Fact]
    public void Nearest_BesideAndAbove_IsOnTheRimAtTheTop()
    {
        var n = HeatZone.Nearest(Fire + new Vector3(0f, 1.4f, 1f), Fire, 0.6f, 0.2f, 1f);
        TestUtil.Near(Fire.X, n.X);
        TestUtil.Near(Fire.Y + 1f, n.Y);
        TestUtil.Near(Fire.Z + 0.6f, n.Z);
    }

    [Fact]
    public void Nearest_IsAsFarAsDistanceSays()
    {
        var p = Fire + new Vector3(0.9f, 0.05f, -0.4f);
        TestUtil.Near(
            HeatZone.Distance(p, Fire, 0.6f, 0.2f, 1f),
            Vector3.Distance(p, HeatZone.Nearest(p, Fire, 0.6f, 0.2f, 1f))
        );
    }
}

public class BodyZonesTests
{
    static readonly Vector3 Head = new(0f, 1.7f, 0f);

    [Fact]
    public void ChestInFrontOfSpine_IsInside()
    {
        Assert.Equal(
            0f,
            BodyZones.TorsoDistance(new Vector3(0f, 1.3f, 0.12f), Head, Vector3.UnitZ, 0.25f)
        );
    }

    [Fact]
    public void AtArmsLength_IsOutside()
    {
        Assert.True(
            BodyZones.TorsoDistance(new Vector3(0f, 1.3f, 0.6f), Head, Vector3.UnitZ, 0.25f) > 0.3f
        );
    }

    [Fact]
    public void FaceHeight_IsAboveTheTorso()
    {
        TestUtil.Near(
            0.15f,
            BodyZones.TorsoDistance(new Vector3(0f, 1.6f, 0f), Head, Vector3.UnitZ, 0.25f),
            1e-3f
        );
    }

    [Fact]
    public void SpineSitsBehindTheEyes()
    {
        // Facing +X: a point 0.1 m behind the eyes is on the spine, 0.35 m in front is not.
        Assert.Equal(
            0f,
            BodyZones.TorsoDistance(new Vector3(-0.1f, 1.3f, 0f), Head, Vector3.UnitX, 0.05f)
        );
        Assert.True(
            BodyZones.TorsoDistance(new Vector3(0.35f, 1.3f, 0f), Head, Vector3.UnitX, 0.2f) > 0f
        );
    }

    [Fact]
    public void Forearm_RunsBackFromTheWrist()
    {
        var hand = new Vector3(0.3f, 1.2f, 0.4f);
        var forward = Vector3.UnitZ;
        Assert.True(
            BodyZones.ForearmDistance(hand + new Vector3(0f, 0f, -0.15f), hand, forward) < 0.01f
        );
        Assert.True(
            BodyZones.ForearmDistance(hand + new Vector3(0f, 0f, 0.2f), hand, forward) > 0.15f
        );
    }

    [Fact]
    public void SegmentDistance_ClampsToEnds()
    {
        TestUtil.Near(
            1f,
            BodyZones.SegmentDistance(new Vector3(0f, 0f, 2f), Vector3.Zero, Vector3.UnitZ)
        );
        TestUtil.Near(
            0.5f,
            BodyZones.SegmentDistance(new Vector3(0.5f, 0f, 0.5f), Vector3.Zero, Vector3.UnitZ)
        );
    }
}

public class LidLiftTests
{
    [Fact]
    public void Fraction_FollowsRise()
    {
        TestUtil.Near(0.5f, LidLift.Fraction(0.125f, 0.25f));
        Assert.Equal(0f, LidLift.Fraction(-0.1f, 0.25f));
        Assert.Equal(1f, LidLift.Fraction(0.4f, 0.25f));
    }

    [Fact]
    public void CapHeldTime_GameAddCannotPassTheLid()
    {
        float total = 2f;
        float dt = 0.02f;
        float held = 1.5f;
        float capped = LidLift.CapHeldTime(held, total, 0.5f, dt);
        Assert.True(capped + dt <= 0.5f * total + 1e-5f);
    }

    [Fact]
    public void CapHeldTime_BelowCapUnchanged()
    {
        Assert.Equal(0.3f, LidLift.CapHeldTime(0.3f, 2f, 0.5f, 0.02f));
    }

    [Fact]
    public void CapHeldTime_FullLiftRunsOnTheGameTimer()
    {
        Assert.Equal(1.99f, LidLift.CapHeldTime(1.99f, 2f, 1f, 0.02f));
    }

    [Fact]
    public void CapHeldTime_ClosedLidHoldsAtZero()
    {
        // Zero after the game adds the frame, not before: the read-back progress stays 0.
        Assert.Equal(0f, LidLift.CapHeldTime(0.5f, 2f, 0f, 0.02f) + 0.02f, 5);
    }

    [Fact]
    public void CapHeldTime_LongFrameNeverRunsOrOpensAClosedLid()
    {
        // A 0.6 s hitch on a 0.5 s case (two-player run 23 saw 85-125 ms frames): the add alone would finish it.
        float total = 0.5f;
        float dt = 0.6f;
        float after = LidLift.CapHeldTime(0f, total, 0f, dt) + dt;
        Assert.True(after <= 1e-5f && after < total);
    }

    [Fact]
    public void CapHeldTime_NeverCompletesShortOfFullLift()
    {
        float total = 1f;
        float held = 0f;
        for (int i = 0; i < 500; i++)
        {
            held = LidLift.CapHeldTime(held, total, 0.99f, 0.011f) + 0.011f;
            Assert.True(held < total);
        }
    }
}

public class PalmWarmthTests
{
    static readonly Vector3 Fire = new(0f, 0.5f, 2f);

    [Fact]
    public void PalmTowardFire_Warms()
    {
        var hand = new Vector3(0f, 0.5f, 1.5f);
        Assert.True(PalmWarmth.Amount(Vector3.UnitZ, hand, Fire, 1f) > 0.3f);
    }

    [Fact]
    public void PalmAway_DoesNot()
    {
        var hand = new Vector3(0f, 0.5f, 1.5f);
        Assert.Equal(0f, PalmWarmth.Amount(-Vector3.UnitZ, hand, Fire, 1f));
    }

    [Fact]
    public void OutOfRange_DoesNot()
    {
        Assert.Equal(0f, PalmWarmth.Amount(Vector3.UnitZ, new Vector3(0f, 0.5f, 0.5f), Fire, 1f));
    }

    [Fact]
    public void Closer_IsWarmer()
    {
        float near = PalmWarmth.Amount(Vector3.UnitZ, new Vector3(0f, 0.5f, 1.7f), Fire, 1f);
        float far = PalmWarmth.Amount(Vector3.UnitZ, new Vector3(0f, 0.5f, 1.2f), Fire, 1f);
        Assert.True(near > far);
        Assert.True(near <= 1f);
    }
}

public class RunnerSlideTests
{
    [Fact]
    public void SlideUpPastTravel_Opens()
    {
        var s = new RunnerSlide { Travel = 0.15f };
        s.Begin(0.1f);
        Assert.Equal(0, s.Update(0.2f));
        Assert.Equal(1, s.Update(0.26f));
    }

    [Fact]
    public void SlideDown_Closes()
    {
        var s = new RunnerSlide { Travel = 0.15f };
        s.Begin(0f);
        Assert.Equal(-1, s.Update(-0.16f));
    }

    [Fact]
    public void FiresOncePerStroke_ThenRebases()
    {
        var s = new RunnerSlide { Travel = 0.15f };
        s.Begin(0f);
        Assert.Equal(1, s.Update(0.2f));
        Assert.Equal(0, s.Update(0.25f));
        Assert.Equal(-1, s.Update(0.04f));
    }

    [Fact]
    public void NotBegun_NeverFires()
    {
        var s = new RunnerSlide();
        Assert.Equal(0, s.Update(5f));
        s.Begin(0f);
        s.End();
        Assert.Equal(0, s.Update(5f));
    }
}
