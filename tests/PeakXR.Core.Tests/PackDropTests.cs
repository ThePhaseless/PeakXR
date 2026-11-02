namespace PeakXR.Core.Tests;

public class PackDropTests
{
    [Fact]
    public void AFreePocket_TakesTheItem() =>
        Assert.Equal(
            DenyReason.None,
            PackDrop.Refusal(false, DenyReason.CantSwitch, true, 2, fromBehind: true)
        );

    [Fact]
    public void NoFreePocket_IsPackFull() =>
        Assert.Equal(
            DenyReason.PackFull,
            PackDrop.Refusal(false, DenyReason.CantSwitch, true, -1, fromBehind: true)
        );

    [Fact]
    public void AnItemTheWheelRefuses_CantGoIn() =>
        Assert.Equal(
            DenyReason.CantHolster,
            PackDrop.Refusal(false, DenyReason.CantSwitch, false, 0, fromBehind: true)
        );

    [Fact]
    public void ABusyHand_SaysWhyFirst() =>
        Assert.Equal(
            DenyReason.TooSoon,
            PackDrop.Refusal(true, DenyReason.TooSoon, false, -1, fromBehind: false)
        );

    [Fact]
    public void AWornPackReachedFromTheFront_IsTheWrongAngle() =>
        Assert.Equal(
            DenyReason.WrongAngle,
            PackDrop.Refusal(false, DenyReason.CantSwitch, true, 0, fromBehind: false)
        );

    [Theory]
    // The pack faces +Z, its wearer's way. Standing behind, the eye looks along it at the pack.
    [InlineData(0f, -1f, true)]
    // Beside the wearer, 90 degrees round: the game's wheel still opens (under 110, BackpackOnBackVisuals.cs:103).
    [InlineData(1f, 0f, true)]
    // In front of the wearer, looking back at their chest.
    [InlineData(0f, 1f, false)]
    // 120 degrees round: past the game's 110.
    [InlineData(0.866f, 0.5f, false)]
    public void FromBehind_FollowsTheGamesAngleFromWhereTheEyeIs(float x, float z, bool behind)
    {
        var pack = new Vector3(0f, 1.2f, 0f);
        var eye = pack + new Vector3(x, 0.4f, z);
        Assert.Equal(behind, PackDrop.FromBehind(eye, pack, Vector3.UnitZ));
    }

    [Fact]
    public void TheHandPicksTheNearestFreePocket()
    {
        var pockets = new[] { Vector3.Zero, Vector3.UnitX, 2f * Vector3.UnitX };
        var free = new[] { true, false, true };
        // Nearest to pocket 1, which is taken: the nearer of the free ones.
        Assert.Equal(2, PackDrop.NearestFree(new Vector3(1.2f, 0f, 0f), pockets, free));
        Assert.Equal(0, PackDrop.NearestFree(new Vector3(0.9f, 0f, 0f), pockets, free));
        Assert.Equal(0, PackDrop.NearestFree(new Vector3(-1f, 0f, 0f), pockets, free));
    }

    [Fact]
    public void TheShownPocket_HoldsUntilAnotherIsClearlyNearer()
    {
        // Two pockets 0.2 m apart and the hand about midway: the pin must not flip with every sway (MP run 15b, #105:
        // the pin showed pocket 2 and the release, a frame later, chose pocket 3).
        var pockets = new[] { Vector3.Zero, 0.2f * Vector3.UnitX };
        var free = new[] { true, true };
        var nearOne = new Vector3(0.11f, 0f, 0f);
        Assert.Equal(1, PackDrop.NearestFree(nearOne, pockets, free));
        Assert.Equal(0, PackDrop.NearestFree(nearOne, pockets, free, keep: 0));
        // Past the margin the nearer pocket wins.
        Assert.Equal(1, PackDrop.NearestFree(new Vector3(0.18f, 0f, 0f), pockets, free, keep: 0));
        // A kept pocket that is taken, or out of range, is not kept.
        Assert.Equal(1, PackDrop.NearestFree(nearOne, pockets, new[] { false, true }, keep: 0));
        Assert.Equal(1, PackDrop.NearestFree(nearOne, pockets, free, keep: 5));
    }

    [Fact]
    public void NoFreePocket_PicksNone() =>
        Assert.Equal(
            -1,
            PackDrop.NearestFree(Vector3.Zero, new[] { Vector3.UnitX }, new[] { false })
        );

    [Theory]
    [InlineData(DenyReason.PackFull)]
    [InlineData(DenyReason.WrongAngle)]
    [InlineData(DenyReason.None)]
    public void APackAimedPastReach_IsOutOfReachFirst(DenyReason inReach) =>
        Assert.Equal(DenyReason.OutOfReach, PackDrop.PointRefusal(2.01f, inReach));

    [Theory]
    [InlineData(DenyReason.None)]
    [InlineData(DenyReason.WrongAngle)]
    [InlineData(DenyReason.PackFull)]
    [InlineData(DenyReason.CantHolster)]
    public void APackAimedWithinReach_KeepsItsOwnVerdict(DenyReason inReach)
    {
        Assert.Equal(inReach, PackDrop.PointRefusal(1.99f, inReach));
        // The game's interact distance itself is in reach (Interaction.distance, 2 m).
        Assert.Equal(inReach, PackDrop.PointRefusal(PackDrop.PointReach, inReach));
    }

    [Fact]
    public void APackBehindRockSaysSo_ReachStillFirst()
    {
        // #144: a near-miss aim at a pack with terrain before its pivot got nothing at all.
        Assert.Equal(
            DenyReason.NoPath,
            PackDrop.PointRefusal(1.5f, DenyReason.None, occluded: true)
        );
        Assert.Equal(
            DenyReason.OutOfReach,
            PackDrop.PointRefusal(3f, DenyReason.None, occluded: true)
        );
        Assert.Equal(
            DenyReason.None,
            PackDrop.PointRefusal(1.5f, DenyReason.None, occluded: false)
        );
    }
}
