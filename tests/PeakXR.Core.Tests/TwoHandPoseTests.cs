namespace PeakXR.Core.Tests;

public class TwoHandPoseTests
{
    static readonly Vector3 Up = new(0f, 1f, 0f);

    // A box held at its ends: grips 0.4 m apart along item +X, its up item +Y.
    static readonly Vector3 GripA = new(-0.2f, 0f, 0f);
    static readonly Vector3 GripB = new(0.2f, 0f, 0f);

    static void Solve(
        Vector3 hand1,
        Vector3 hand2,
        Vector3 grip1,
        Vector3 grip2,
        out Vector3 pos,
        out Quaternion rot
    )
    {
        var axis = Vector3.Normalize(hand2 - hand1);
        var up = Up;
        TwoHandPose.Solve(
            hand1,
            hand2,
            Up,
            Up,
            grip1,
            grip2,
            Up,
            ref axis,
            ref up,
            out pos,
            out rot
        );
    }

    [Fact]
    public void Item_SitsAtTheMidpoint_AlongTheHandToHandAxis()
    {
        var h1 = new Vector3(1f, 1f, 0f);
        var h2 = new Vector3(1f, 1f, 0.4f);
        Solve(h1, h2, GripA, GripB, out var pos, out var rot);
        // Grips land on the hands, the grip axis runs from hand 1 to hand 2, the item's up stays up.
        TestUtil.Near(h1, pos + UnityQuat.Rotate(rot, GripA));
        TestUtil.Near(h2, pos + UnityQuat.Rotate(rot, GripB));
        TestUtil.Near(Up, UnityQuat.Rotate(rot, Up));
    }

    [Fact]
    public void SpacingMismatch_IsSplitEvenly()
    {
        // The hands 0.6 m apart on grips 0.4 m apart: each grip is 0.1 m inside its hand, none exact.
        var h1 = new Vector3(0f, 1f, 0f);
        var h2 = new Vector3(0.6f, 1f, 0f);
        Solve(h1, h2, GripA, GripB, out var pos, out var rot);
        var g1 = pos + UnityQuat.Rotate(rot, GripA);
        var g2 = pos + UnityQuat.Rotate(rot, GripB);
        TestUtil.Near(0.1f, Vector3.Distance(h1, g1));
        TestUtil.Near(0.1f, Vector3.Distance(h2, g2));
        TestUtil.Near((h1 + h2) * 0.5f, (g1 + g2) * 0.5f);
    }

    [Fact]
    public void BothHandsRoll_TheItemRollsWithThem_OneHandRolls_Half()
    {
        var h1 = new Vector3(-0.2f, 1f, 0.4f);
        var h2 = new Vector3(0.2f, 1f, 0.4f);
        var tilt = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 40f * UnityQuat.Deg2Rad);
        var rolled = UnityQuat.Rotate(tilt, Up);
        var axis = Vector3.UnitX;
        var up = Up;
        TwoHandPose.Solve(
            h1,
            h2,
            rolled,
            rolled,
            GripA,
            GripB,
            Up,
            ref axis,
            ref up,
            out _,
            out var both
        );
        TestUtil.Near(rolled, UnityQuat.Rotate(both, Up));
        TwoHandPose.Solve(
            h1,
            h2,
            rolled,
            Up,
            GripA,
            GripB,
            Up,
            ref axis,
            ref up,
            out _,
            out var one
        );
        var half = UnityQuat.Rotate(
            Quaternion.CreateFromAxisAngle(Vector3.UnitX, 20f * UnityQuat.Deg2Rad),
            Up
        );
        TestUtil.Near(half, UnityQuat.Rotate(one, Up));
    }

    [Fact]
    public void HandsTogether_KeepTheLastAxis()
    {
        // The hands meet (1 cm apart, any direction): the axis is noise, so the last one holds.
        var axis = Vector3.UnitX;
        var up = Up;
        var h = new Vector3(0f, 1f, 0.4f);
        TwoHandPose.Solve(
            h,
            h + new Vector3(0f, 0f, 0.01f),
            Up,
            Up,
            GripA,
            GripB,
            Up,
            ref axis,
            ref up,
            out var pos,
            out var rot
        );
        TestUtil.Near(Vector3.UnitX, axis);
        TestUtil.Near(Vector3.UnitX, Vector3.Normalize(UnityQuat.Rotate(rot, GripB - GripA)));
        TestUtil.Near(
            h + new Vector3(0f, 0f, 0.005f),
            pos + UnityQuat.Rotate(rot, (GripA + GripB) * 0.5f)
        );
    }

    [Fact]
    public void UpAlongTheAxis_KeepsTheLastUp()
    {
        // Both hands' up runs along the hand-to-hand axis: no roll can be read, the last up holds.
        var axis = Vector3.UnitX;
        var up = Up;
        TwoHandPose.Solve(
            new Vector3(-0.2f, 1f, 0f),
            new Vector3(0.2f, 1f, 0f),
            Vector3.UnitX,
            Vector3.UnitX,
            GripA,
            GripB,
            Up,
            ref axis,
            ref up,
            out _,
            out var rot
        );
        TestUtil.Near(Up, up);
        TestUtil.Near(Up, UnityQuat.Rotate(rot, Up));
    }

    [Fact]
    public void Capture_KeepsTheFrontWhereTheOneHandHoldShowedIt()
    {
        // One hand shows the front (item -Z) to the eyes at world -Z. The second hand closes behind and to the side,
        // so the grip axis must swing about 150 degrees: turning the shortest way about the first hand carried the
        // front round to face away (the old rule); the captured up keeps it facing the eyes.
        var front = new Vector3(0f, 0f, -1f);
        var axisLocal = Vector3.Normalize(GripA - GripB); // grips: hand 1 on B, hand 2 on A (item -X)
        var upLocal = TwoHandPose.UpLocal(axisLocal, front);
        var oneHand = Quaternion.Identity;
        var a0 = Vector3.Normalize(new Vector3(0.9f, 0f, 0.44f));
        var hand1 = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.3f);
        var hand2 = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.5f);
        TwoHandPose.Capture(
            oneHand,
            upLocal,
            a0,
            hand1,
            hand2,
            out var v1,
            out var v2,
            out var up0
        );
        var axis = a0;
        var up = up0;
        var h1 = new Vector3(0f, 1f, 0.4f);
        TwoHandPose.Solve(
            h1,
            h1 + a0 * 0.4f,
            UnityQuat.Rotate(hand1, v1),
            UnityQuat.Rotate(hand2, v2),
            GripB,
            GripA,
            upLocal,
            ref axis,
            ref up,
            out _,
            out var rot
        );
        Assert.True(Vector3.Dot(UnityQuat.Rotate(rot, front), -Vector3.UnitZ) > 0.85f);
        var oldRule = GripMath.AlignAxis(oneHand, axisLocal, a0);
        Assert.True(Vector3.Dot(UnityQuat.Rotate(oldRule, front), -Vector3.UnitZ) < 0f);
    }

    [Fact]
    public void Capture_HandsUpAreTheItemUpInEachHand()
    {
        // At the join each hand's up (in its own frame) turns back into the same world up: no roll jump.
        var h1 = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1f);
        var h2 = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.7f);
        TwoHandPose.Capture(
            Quaternion.Identity,
            Up,
            Vector3.UnitX,
            h1,
            h2,
            out var v1,
            out var v2,
            out var up0
        );
        TestUtil.Near(Up, up0);
        TestUtil.Near(Up, UnityQuat.Rotate(h1, v1));
        TestUtil.Near(Up, UnityQuat.Rotate(h2, v2));
    }

    [Fact]
    public void UpLocal_IsThePreferredSideAcrossTheAxis_ElseAnyAcross()
    {
        TestUtil.Near(
            Up,
            TwoHandPose.UpLocal(Vector3.UnitX, Vector3.Normalize(new Vector3(0.5f, 1f, 0f)))
        );
        var any = TwoHandPose.UpLocal(Vector3.UnitY, Up);
        TestUtil.Near(1f, any.Length());
        TestUtil.Near(0f, Vector3.Dot(any, Vector3.UnitY));
    }

    [Theory]
    // Near Hand_L (within reach): the palm snaps onto it. On the body farther off: it stays where it closed.
    [InlineData(0.1f, true, true)]
    [InlineData(0.4f, true, false)]
    [InlineData(0.1f, false, false)]
    public void SecondGrip_SnapsOntoTheGripOnlyNearIt(float off, bool hasGrip, bool snaps)
    {
        var grip = new Vector3(-0.3f, 0f, 0f);
        var hand = grip + new Vector3(0f, off, 0f);
        var got = TwoHandPose.SecondGrip(hand, hasGrip, grip, 0.25f);
        TestUtil.Near(snaps ? grip : hand, got);
    }

    [Theory]
    // The free, open hand near a held item that takes a second hand: the ring shows where it grips.
    [InlineData(true, true, false, 0.3f, true)]
    // Too far off, already joined, the hand busy or the item one-handed: nothing.
    [InlineData(true, true, false, 0.6f, false)]
    [InlineData(true, true, true, 0.1f, false)]
    [InlineData(true, false, false, 0.1f, false)]
    [InlineData(false, true, false, 0.1f, false)]
    public void SecondGripCue_ShowsOnlyForAFreeHandNearby(
        bool takesSecondHand,
        bool handFree,
        bool joined,
        float distance,
        bool shows
    ) =>
        Assert.Equal(
            shows,
            TwoHandPose.ShowSecondGrip(takesSecondHand, handFree, joined, distance)
        );

    [Fact]
    public void Blend_StartsAtTheOneHandPoseAndEndsBetweenTheHands()
    {
        var aPos = new Vector3(0f, 1f, 0f);
        var aRot = Quaternion.Identity;
        var bPos = new Vector3(0.2f, 1f, 0f);
        var bRot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1f);
        TwoHandPose.Blend(aPos, aRot, bPos, bRot, TwoHandPose.Weight(0f), out var p0, out var r0);
        TestUtil.Near(aPos, p0);
        TestUtil.SameRotation(aRot, r0);
        TwoHandPose.Blend(
            aPos,
            aRot,
            bPos,
            bRot,
            TwoHandPose.Weight(HoldHands.EaseSeconds),
            out var p1,
            out var r1
        );
        TestUtil.Near(bPos, p1);
        TestUtil.SameRotation(bRot, r1);
        // Eased: barely moved after the first frame at 90 Hz, so a join or release never jumps.
        TwoHandPose.Blend(aPos, aRot, bPos, bRot, TwoHandPose.Weight(1f / 90f), out var pf, out _);
        Assert.True(Vector3.Distance(aPos, pf) < 0.01f);
        Assert.True(TwoHandPose.Weight(-1f) == 0f && TwoHandPose.Weight(10f) == 1f);
    }
}
