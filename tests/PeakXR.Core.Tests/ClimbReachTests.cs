namespace PeakXR.Core.Tests;

public class ClimbReachTests
{
    const float Radius = 0.1f;

    [Theory]
    [InlineData(0.2f, 0f)]
    [InlineData(0.35f, 0f)]
    [InlineData(0.45f, 0.1f)]
    [InlineData(0.55f, 0.2f)]
    [InlineData(0.8f, 0.2f)]
    public void Allowance_GrowsOnlyAsTheArmStraightens(float eyeToHand, float expected) =>
        Assert.Equal(expected, ClimbReach.Allowance(eyeToHand, 0.2f), 3);

    [Fact]
    public void Allowance_ZeroAssistKeepsTheHandOnTheRock() =>
        Assert.Equal(0f, ClimbReach.Allowance(0.8f, 0f));

    static HoldVerdict Judge(
        float beyond,
        float allowance = 0f,
        float eyeToRock = 0.8f,
        float angle = 90f,
        bool climbing = false
    ) => ClimbReach.Judge(beyond, Radius, allowance, eyeToRock, angle, climbing);

    [Fact]
    public void HandOnAWall_Grabs() => Assert.Equal(HoldVerdict.Grab, Judge(0.05f));

    [Fact]
    public void HandPushedIntoTheRock_StillGrabs() =>
        // The tracked hand passes through rock; the surface it went through is the hold.
        Assert.Equal(HoldVerdict.Grab, Judge(-0.2f));

    [Fact]
    public void SlopeJustPastAnOutstretchedHand_GrabsWithTheAllowance()
    {
        // A 55 degree face 0.25 m past the hand at full stretch: the tester had to lean in for it.
        Assert.Equal(HoldVerdict.Grab, Judge(0.25f, allowance: 0.2f, angle: 55f));
        Assert.Equal(HoldVerdict.Approach, Judge(0.25f, allowance: 0f, angle: 55f));
    }

    [Fact]
    public void RockFarPastTheBand_IsOpenAir() =>
        Assert.Equal(
            HoldVerdict.None,
            Judge(Radius + 0.2f + TraversalRefusal.ReachProbe + 0.01f, 0.2f)
        );

    [Fact]
    public void ApproachOnlyForRockTheGameWouldGrab()
    {
        Assert.Equal(HoldVerdict.None, Judge(0.3f, angle: 40f));
        Assert.Equal(HoldVerdict.None, Judge(0.3f, eyeToRock: 1.4f));
    }

    [Theory]
    [InlineData(40f, HoldVerdict.TooFlat)]
    [InlineData(30f, HoldVerdict.TooFlat)]
    [InlineData(20f, HoldVerdict.None)]
    [InlineData(175f, HoldVerdict.Overhang)]
    [InlineData(50f, HoldVerdict.Grab)]
    [InlineData(170f, HoldVerdict.Grab)]
    public void AngleAtTheHand_FollowsTheGameGrabAngle(float angle, HoldVerdict expected) =>
        Assert.Equal(expected, Judge(0.02f, angle: angle));

    [Fact]
    public void HoldBeyondTheCameraReach_IsOutOfReach() =>
        // TryToStartWallClimb casts from the camera (CharacterClimbing.cs:541-561).
        Assert.Equal(
            HoldVerdict.OutOfReach,
            Judge(0.02f, eyeToRock: TraversalRefusal.GameWallReach + 0.01f)
        );

    [Fact]
    public void WhileClimbing_AnyRockAtTheHandHolds() =>
        // The climb is already the game's; a regrip only anchors the hand.
        Assert.Equal(HoldVerdict.Grab, Judge(0.02f, angle: 40f, climbing: true));
}
