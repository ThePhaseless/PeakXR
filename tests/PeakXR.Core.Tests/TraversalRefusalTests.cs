namespace PeakXR.Core.Tests;

public class TraversalRefusalTests
{
    // maxRopeAngle defaults to 90 (CharacterRopeHandling.cs:29): segments within 54 deg of vertical are grabbable.
    const float MaxRopeAngle = 90f;

    [Fact]
    public void Rope_Grabbable_IsNone() =>
        Assert.Equal(
            DenyReason.None,
            TraversalRefusal.Rope(10f, MaxRopeAngle, anchored: true, outOfStamina: false)
        );

    [Fact]
    public void Rope_NotAnchored_IsUnanchored() =>
        Assert.Equal(
            DenyReason.Unanchored,
            TraversalRefusal.Rope(10f, MaxRopeAngle, anchored: false, outOfStamina: false)
        );

    [Theory]
    [InlineData(53.9f, false)]
    [InlineData(54.1f, true)]
    [InlineData(90f, true)]
    [InlineData(125.9f, true)]
    [InlineData(126.1f, false)]
    [InlineData(180f, false)]
    public void Rope_AngleLimit_IsFiftyFourFromVerticalBothWays(float angleFromUp, bool refused) =>
        Assert.Equal(
            refused ? DenyReason.TooFlat : DenyReason.None,
            TraversalRefusal.Rope(angleFromUp, MaxRopeAngle, anchored: true, outOfStamina: false)
        );

    [Fact]
    public void Rope_OutOfStamina_IsNoStamina() =>
        Assert.Equal(
            DenyReason.NoStamina,
            TraversalRefusal.Rope(0f, MaxRopeAngle, anchored: true, outOfStamina: true)
        );

    [Fact]
    public void Rope_Unanchored_WinsOverStamina()
    {
        // Resting would not help: the rope has to be fixed first.
        Assert.Equal(
            DenyReason.Unanchored,
            TraversalRefusal.Rope(80f, MaxRopeAngle, anchored: false, outOfStamina: true)
        );
    }

    [Fact]
    public void Vine_OnlyStaminaRefuses()
    {
        Assert.Equal(DenyReason.None, TraversalRefusal.Vine(outOfStamina: false));
        Assert.Equal(DenyReason.NoStamina, TraversalRefusal.Vine(outOfStamina: true));
    }

    [Fact]
    public void Handle_TakenBySomeoneElse_IsOccupied()
    {
        Assert.Equal(DenyReason.Occupied, TraversalRefusal.Handle(taken: true, takenByMe: false));
        Assert.Equal(DenyReason.None, TraversalRefusal.Handle(taken: true, takenByMe: true));
        Assert.Equal(DenyReason.None, TraversalRefusal.Handle(taken: false, takenByMe: false));
    }

    [Theory]
    [InlineData(1.01f, 0f, DenyReason.None)]
    [InlineData(1f, 0f, DenyReason.TooSoon)]
    [InlineData(0.2f, 0f, DenyReason.TooSoon)]
    [InlineData(2f, 0.49f, DenyReason.None)]
    [InlineData(2f, 0.5f, DenyReason.NoStamina)]
    [InlineData(0.2f, 3f, DenyReason.NoStamina)]
    public void Lunge_FollowsTheGamesClimbJumpGate(
        float sinceClimbJump,
        float outOfStaminaFor,
        DenyReason expected
    ) => Assert.Equal(expected, TraversalRefusal.Lunge(sinceClimbJump, outOfStaminaFor));

    [Fact]
    public void Pull_Down_IsNeverRefused() =>
        Assert.Equal(
            DenyReason.None,
            TraversalRefusal.Pull(-0.8f, slideOnly: true, outOfStamina: true)
        );

    [Fact]
    public void Pull_SmallUp_IsNoise() =>
        Assert.Equal(
            DenyReason.None,
            TraversalRefusal.Pull(0.2f, slideOnly: false, outOfStamina: true)
        );

    [Fact]
    public void Pull_UpWithoutStamina_IsNoStamina() =>
        Assert.Equal(
            DenyReason.NoStamina,
            TraversalRefusal.Pull(0.6f, slideOnly: false, outOfStamina: true)
        );

    [Fact]
    public void Pull_UpOnSlideOnlySurface_IsTooSteep() =>
        Assert.Equal(
            DenyReason.TooSteep,
            TraversalRefusal.Pull(0.6f, slideOnly: true, outOfStamina: false)
        );

    [Fact]
    public void Pull_UpWithStamina_IsNone() =>
        Assert.Equal(
            DenyReason.None,
            TraversalRefusal.Pull(1f, slideOnly: false, outOfStamina: false)
        );

    [Theory]
    [InlineData(1.28f, false)]
    [InlineData(1.35f, false)]
    [InlineData(1.36f, true)]
    public void GameWallReach_IncludesTheThickenedLineCheck(float eyeToHold, bool refused)
    {
        // VR run 37: a hold 1.28 m from the camera, which the game's 0.1 m thickened climb check reaches
        // (CharacterClimbing.cs:553-561), was refused by a bare 1.25 m cap and vr.15 could not climb.
        Assert.Equal(
            refused ? DenyReason.OutOfReach : DenyReason.None,
            TraversalRefusal.WallReach(eyeToHold, TraversalRefusal.GameWallReach)
        );
    }

    [Theory]
    [InlineData(1.2f, 1.25f, false)]
    [InlineData(1.25f, 1.25f, false)]
    [InlineData(1.26f, 1.25f, true)]
    public void WallReach_IsCappedFromTheCamera(float eyeToHold, float reach, bool refused) =>
        Assert.Equal(
            refused ? DenyReason.OutOfReach : DenyReason.None,
            TraversalRefusal.WallReach(eyeToHold, reach)
        );

    [Theory]
    [InlineData(0.05f, false)]
    [InlineData(0.1f, false)]
    [InlineData(0.3f, true)]
    [InlineData(0.5f, true)]
    [InlineData(0.55f, false)]
    public void GripShortOfAWall_IsOutOfReach(float handToRock, bool refused)
    {
        // VR run 64: the hand stops at arm's length, so a wall just beyond it never reached the grab and the grip was
        // silent. Rock within the band past the grab radius (0.10 m) is the "too far" refusal; farther is open air.
        Assert.Equal(
            refused ? DenyReason.OutOfReach : DenyReason.None,
            TraversalRefusal.ReachBand(
                handToRock,
                grabRadius: 0.1f,
                band: TraversalRefusal.ReachProbe
            )
        );
    }
}
