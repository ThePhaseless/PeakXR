namespace PeakXR.Core.Tests;

public class WristHudTests
{
    [Fact]
    public void Width_KeepsTheSameAngleAtAnyReadingDistance()
    {
        float near = WristHud.Width(0.16f, 0.3f, 0f);
        float far = WristHud.Width(0.16f, 0.6f, 0f);
        Assert.Equal(0.16f, WristHud.Width(0.16f, WristHud.ReadDistance, 0f), 4);
        Assert.Equal(near / 0.3f, far / 0.6f, 4);
    }

    [Fact]
    public void Width_StaysWristSizedWhenTheArmIsFarOrAtTheFace()
    {
        Assert.Equal(WristHud.Width(0.16f, 0.7f, 0f), WristHud.Width(0.16f, 3f, 0f), 4);
        Assert.Equal(WristHud.Width(0.16f, 0.2f, 0f), WristHud.Width(0.16f, 0.01f, 0f), 4);
    }

    [Fact]
    public void Width_GrowsOnAnAlertPulse()
    {
        Assert.True(WristHud.Width(0.16f, 0.4f, 1f) > WristHud.Width(0.16f, 0.4f, 0f));
    }

    [Fact]
    public void Facing_FullWhenLookedAt()
    {
        Assert.Equal(1f, WristHud.Facing(5f, 0f));
        Assert.Equal(1f, WristHud.Facing(WristHud.LookDegrees, 0f));
    }

    [Fact]
    public void Facing_HalfTurnedWhenNotLookedAt()
    {
        Assert.Equal(WristHud.RestFacing, WristHud.Facing(90f, 0f));
        float mid = WristHud.Facing((WristHud.LookDegrees + WristHud.AwayDegrees) / 2f, 0f);
        Assert.True(mid > WristHud.RestFacing && mid < 1f);
    }

    [Fact]
    public void Facing_TurnsToTheEyeOnAnAlert()
    {
        Assert.Equal(1f, WristHud.Facing(90f, 1f));
    }

    [Fact]
    public void ScaledWidth_GrowsWithTheTextScale()
    {
        TestUtil.Near(0.16f, WristHud.ScaledWidth(0.16f, 1f), 1e-6f);
        TestUtil.Near(0.24f, WristHud.ScaledWidth(0.16f, 1.5f), 1e-6f);
    }

    [Fact]
    public void ScaledWidth_StaysWithinTheWristAndNeverShrinks()
    {
        // The setting's own largest width bounds the grown display; a wide setting is not cut back.
        Assert.Equal(WristHud.MaxWidth, WristHud.ScaledWidth(0.3f, 2f));
        Assert.Equal(0.4f, WristHud.ScaledWidth(0.4f, 2f));
        Assert.Equal(0.16f, WristHud.ScaledWidth(0.16f, 0.5f));
    }
}

public class WristHudPlacementTests
{
    private static void Near(System.Numerics.Vector3 a, System.Numerics.Vector3 b) =>
        Assert.True(System.Numerics.Vector3.Distance(a, b) < 1e-4f, $"{a} vs {b}");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Anchor_SitsBehindTheFistOnTheBackOfTheWrist(bool left)
    {
        var p = WristHud.Anchor(
            System.Numerics.Vector3.Zero,
            System.Numerics.Quaternion.Identity,
            left
        );
        // Toward the forearm (grip +Y), not down the handle (grip -Z), and out of the back of the hand.
        Assert.Equal(WristHud.WristBack, System.Numerics.Vector3.Dot(p, GripFrame.Forearm), 4);
        Assert.Equal(0f, p.Z, 4);
        Assert.Equal(
            WristHud.DorsalOffset,
            System.Numerics.Vector3.Dot(p, GripFrame.BackOfHand(left)),
            4
        );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Rest_ReadsWithTextUpTowardTheFingers(bool left)
    {
        var q = WristHud.Rest(System.Numerics.Quaternion.Identity, left);
        Near(GripFrame.Fingers, UnityQuat.Rotate(q, System.Numerics.Vector3.UnitY));
        // Faces out of the back of the hand: its forward points into the wrist.
        Near(GripFrame.Palm(left), UnityQuat.Rotate(q, System.Numerics.Vector3.UnitZ));
    }

    [Theory]
    // The game's stamina frame grows from its left pivot as afflictions overflow (614 -> 661 px): back to 614.
    [InlineData(0f, 661f, 0f, 47f, 0f, 614f)]
    // A centred pivot gives back half on each side.
    [InlineData(-330.5f, 330.5f, 0.5f, 47f, -307f, 307f)]
    // Not grown (or narrower than at rest): as drawn.
    [InlineData(0f, 614f, 0f, 0f, 0f, 614f)]
    [InlineData(0f, 600f, 0f, -14f, 0f, 600f)]
    public void FrameAtRest_TakesTheOverflowGrowthBackAroundThePivot(
        float min,
        float max,
        float pivot,
        float grown,
        float restMin,
        float restMax
    )
    {
        var (lo, hi) = WristHud.FrameAtRest(min, max, pivot, grown);
        Assert.Equal(restMin, lo, 3);
        Assert.Equal(restMax, hi, 3);
    }
}
