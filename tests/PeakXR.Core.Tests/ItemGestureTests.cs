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
    // A case 2.6 m long lying 20 degrees off level on a rock (#127). Its lid's lower face is 0.5 m up its own axis, and
    // its render centre sits on that face. World height says nothing here: the lid's low end is under the centre, the
    // base's high end is over it.
    static readonly System.Numerics.Quaternion Tilt =
        System.Numerics.Quaternion.CreateFromAxisAngle(
            System.Numerics.Vector3.UnitZ,
            20f * MathF.PI / 180f
        );

    static System.Numerics.Vector3 InCase(float x, float y) =>
        System.Numerics.Vector3.Transform(new System.Numerics.Vector3(x, y, 0f), Tilt);

    static System.Numerics.Vector3 CaseUp => InCase(0f, 1f);

    static float TiltedLidFace() =>
        LidLift.LowerFace(
            InCase(0f, 0.75f),
            InCase(1.3f, 0f),
            InCase(0f, 0.25f),
            System.Numerics.Vector3.UnitZ * 0.8f,
            CaseUp
        );

    [Fact]
    public void LowerFace_IsTheLidsUnderside_AlongTheCasesUp() =>
        TestUtil.Near(0.5f, TiltedLidFace());

    [Fact]
    public void LowerFace_HoldsForABoxTurnedOnItsSide()
    {
        // The prefabs' lid boxes lie along other local axes (LidCollider z is the case's up on some).
        float face = LidLift.LowerFace(
            System.Numerics.Vector3.UnitY * 0.75f,
            System.Numerics.Vector3.UnitX * 1.3f,
            System.Numerics.Vector3.UnitZ * 0.8f,
            System.Numerics.Vector3.UnitY * -0.25f,
            System.Numerics.Vector3.UnitY
        );
        TestUtil.Near(0.5f, face);
    }

    [Fact]
    public void OnLid_LowEndOfATiltedLid_IsOnTheLid()
    {
        var hand = InCase(-1.2f, 0.6f);
        // Under the centre in world height (the old rule called this low).
        Assert.True(hand.Y < InCase(0f, 0.5f).Y);
        Assert.True(LidLift.OnLid(hand, TiltedLidFace(), CaseUp, 0.05f));
    }

    [Fact]
    public void OnLid_HighEndOfATiltedBase_IsLow()
    {
        var hand = InCase(1.2f, 0.3f);
        // Over the centre in world height (the old rule called this the lid).
        Assert.True(hand.Y > InCase(0f, 0.5f).Y);
        Assert.False(LidLift.OnLid(hand, TiltedLidFace(), CaseUp, 0.05f));
    }

    [Fact]
    public void OnLid_TheGiveCountsAtTheSeam()
    {
        Assert.True(LidLift.OnLid(InCase(0f, 0.46f), TiltedLidFace(), CaseUp, 0.05f));
        Assert.False(LidLift.OnLid(InCase(0f, 0.44f), TiltedLidFace(), CaseUp, 0.05f));
    }

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

public class CaseBoxTests
{
    // A 2.6 x 1.08 x 1.45 m case on its own frame's floor: thin walls, a floor and a lid, hollow inside (LuggageClown).
    const float HalfLength = 1.3f;
    const float Height = 1.08f;
    const float HalfWidth = 0.725f;
    const float Radius = 0.12f;

    static readonly (Vector3 Centre, Vector3 Half)[] Solids =
    {
        (new(0f, 0.05f, 0f), new(HalfLength, 0.05f, HalfWidth)),
        (new(0f, Height - 0.2f, 0f), new(HalfLength, 0.2f, HalfWidth)),
        (new(-HalfLength + 0.02f, 0.4f, 0f), new(0.02f, 0.4f, HalfWidth)),
        (new(HalfLength - 0.02f, 0.4f, 0f), new(0.02f, 0.4f, HalfWidth)),
    };

    static CaseBox Case()
    {
        var box = default(CaseBox);
        foreach (var (centre, half) in Solids)
            box.Add(centre, Vector3.UnitX * half.X, Vector3.UnitY * half.Y, Vector3.UnitZ * half.Z);
        return box;
    }

    static readonly Vector3 CasePos = new(10f, 2f, -3f);
    static readonly Quaternion Yawed = Quaternion.CreateFromAxisAngle(
        Vector3.UnitY,
        45f * MathF.PI / 180f
    );
    static readonly Quaternion Tilted = Quaternion.CreateFromAxisAngle(
        Vector3.UnitZ,
        25f * MathF.PI / 180f
    );

    static Vector3 ToCase(Vector3 world, Quaternion pose) =>
        Vector3.Transform(world - CasePos, Quaternion.Inverse(pose));

    /// <summary>The world-axis box around the posed solids: what SunkInto used to test (2cad5c4).</summary>
    static bool InWorldBox(Vector3 world, Quaternion pose)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var (centre, half) in Solids)
            for (int i = 0; i < 8; i++)
            {
                var corner =
                    centre
                    + new Vector3(
                        (i & 1) == 0 ? -half.X : half.X,
                        (i & 2) == 0 ? -half.Y : half.Y,
                        (i & 4) == 0 ? -half.Z : half.Z
                    );
                var at = CasePos + Vector3.Transform(corner, pose);
                min = Vector3.Min(min, at);
                max = Vector3.Max(max, at);
            }
        return world == Vector3.Clamp(world, min, max);
    }

    public static TheoryData<float, float, float, bool> Points =>
        new()
        {
            // Off a yawed case's long wall, inside its world box.
            { 1.2f, 0.5f, 1.2f, true },
            // Air over a tilted case's low end, inside its world box.
            { -1.1f, 1.3f, 0f, false },
            // Rock under a tilted case's high end, inside its world box.
            { 1.1f, -0.1f, 0f, false },
        };

    [Theory]
    [MemberData(nameof(Points))]
    public void PointOutsideTheWalls_InsideTheWorldBox_DoesNotCount(
        float x,
        float y,
        float z,
        bool yawed
    )
    {
        var pose = yawed ? Yawed : Tilted;
        var world = CasePos + new Vector3(x, y, z);
        Assert.True(InWorldBox(world, pose));
        Assert.False(Case().Within(ToCase(world, pose), Radius));
    }

    [Fact]
    public void HandInTheHollow_CountsHoweverTheCaseLies()
    {
        var hollow = new Vector3(0.5f, 0.45f, 0.2f);
        foreach (var pose in new[] { Yawed, Tilted })
            Assert.True(
                Case().Within(ToCase(CasePos + Vector3.Transform(hollow, pose), pose), Radius)
            );
    }

    [Fact]
    public void GrabRadiusOffAWall_Counts_FartherDoesNot()
    {
        var box = Case();
        Assert.True(box.Within(new Vector3(0f, 0.5f, HalfWidth + Radius - 0.01f), Radius));
        Assert.False(box.Within(new Vector3(0f, 0.5f, HalfWidth + Radius + 0.01f), Radius));
    }

    [Fact]
    public void BoxTurnedInTheCase_ReachesItsCorners()
    {
        // A lid bone a little open: half axes are the box's own, read in the case's frame.
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 45f * MathF.PI / 180f);
        var box = default(CaseBox);
        box.Add(
            Vector3.Zero,
            Vector3.UnitX,
            Vector3.Transform(Vector3.UnitY, turn),
            Vector3.Transform(Vector3.UnitZ, turn)
        );
        TestUtil.Near(MathF.Sqrt(2f), box.Max.Y);
        TestUtil.Near(-MathF.Sqrt(2f), box.Min.Z);
        TestUtil.Near(1f, box.Max.X);
    }

    [Fact]
    public void NoColliders_NothingCounts() =>
        Assert.False(default(CaseBox).Within(Vector3.Zero, Radius));
}
