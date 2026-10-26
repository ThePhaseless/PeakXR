namespace PeakXR.Core.Tests;

public class HoldHandsTests
{
    [Fact]
    public void RightPrimary_UsesTheGamesGripsAsTheyAre()
    {
        var p = new Vector3(0.1f, -0.05f, 0.3f);
        Assert.Equal(p, HoldHands.ItemLocal(p, primaryLeft: false));
    }

    [Fact]
    public void LeftPrimary_SecondGripIsTheMirrorOfTheRightHoldsOne()
    {
        // The left hold is the mirror image of the right one (GripMath.MirrorX in the controller frame). Where the
        // right hold puts Hand_L, mirrored, is where the left hold's off-hand grip must be.
        var handR = new Vector3(0.04f, -0.02f, -0.1f);
        var handL = new Vector3(-0.12f, 0.03f, 0.25f);
        var handRRot = UnityQuat.Euler(10f, 25f, -5f);
        var bonePos = new Vector3(0.01f, -0.03f, 0.05f);
        var boneRot = UnityQuat.Euler(0f, 0f, 90f);
        GripMath.ItemInHand(handR, handRRot, bonePos, boneRot, out var rp, out var rr);
        var lp = rp;
        var lr = rr;
        GripMath.MirrorX(ref lp, ref lr);

        var rightOffHand = rp + UnityQuat.Rotate(rr, handL);
        var leftOffHand = lp + UnityQuat.Rotate(lr, HoldHands.ItemLocal(handL, primaryLeft: true));
        TestUtil.Near(new Vector3(-rightOffHand.X, rightOffHand.Y, rightOffHand.Z), leftOffHand);

        // And the left hand bone sits on the mirrored primary grip.
        var leftBone = lp + UnityQuat.Rotate(lr, HoldHands.ItemLocal(handR, primaryLeft: true));
        TestUtil.Near(new Vector3(-bonePos.X, bonePos.Y, bonePos.Z), leftBone);
    }

    [Theory]
    [InlineData(true, true, true, true)]
    // The other hand only brushed it, or let go too: no handoff, the release drops or throws as usual.
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    public void PrimaryOpens_HandsOffOnlyToASecondHandStillGripping(
        bool twoHand,
        bool otherGripClosed,
        bool otherIsSecondHand,
        bool expected
    ) => Assert.Equal(expected, HoldHands.HandOff(twoHand, otherGripClosed, otherIsSecondHand));

    [Fact]
    public void Ease_StartsAtTheOldPoseAndSettles()
    {
        Assert.Equal(0f, HoldHands.Ease(0f, 0.25f));
        Assert.Equal(0.5f, HoldHands.Ease(0.125f, 0.25f), 4);
        Assert.Equal(1f, HoldHands.Ease(0.25f, 0.25f));
        Assert.Equal(1f, HoldHands.Ease(3f, 0.25f));
        Assert.Equal(1f, HoldHands.Ease(-1f, 0.25f));
        Assert.True(HoldHands.Ease(0.05f, 0.25f) < 0.2f);
    }
}
