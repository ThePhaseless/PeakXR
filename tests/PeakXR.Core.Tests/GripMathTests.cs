namespace PeakXR.Core.Tests;

public class GripMathTests
{
    [Fact]
    public void ItemInHand_PutsGripOnTheHand()
    {
        // Grip child of the item, and the hand bone's offset from the controller.
        var gripPos = new Vector3(0.1f, -0.05f, 0.2f);
        var gripRot = UnityQuat.Euler(10, 40, -20);
        var bonePos = new Vector3(0, 0, -0.07f);
        var boneRot = UnityQuat.Euler(0, 90, 0);

        GripMath.ItemInHand(gripPos, gripRot, bonePos, boneRot, out var itemPos, out var itemRot);

        // Item placed in the controller frame; the grip, seen from the controller, must match the bone offset.
        var gripInCtrlPos = itemPos + UnityQuat.Rotate(itemRot, gripPos);
        var gripInCtrlRot = UnityQuat.Mul(itemRot, gripRot);
        TestUtil.Near(bonePos, gripInCtrlPos);
        TestUtil.SameRotation(boneRot, gripInCtrlRot, 0.05f);
    }

    [Fact]
    public void Compose_AppliesParentThenChild()
    {
        GripMath.Compose(
            new Vector3(1, 0, 0),
            UnityQuat.Euler(0, 90, 0),
            new Vector3(0, 0, 1),
            Quaternion.Identity,
            out var p,
            out var r
        );
        TestUtil.Near(new Vector3(2, 0, 0), p);
        TestUtil.SameRotation(UnityQuat.Euler(0, 90, 0), r);
    }

    [Fact]
    public void MirrorX_FlipsSideAndKeepsUpForward()
    {
        var p = new Vector3(0.1f, 0.2f, 0.3f);
        var q = UnityQuat.Euler(0, 30, 0);
        GripMath.MirrorX(ref p, ref q);
        TestUtil.Near(new Vector3(-0.1f, 0.2f, 0.3f), p);
        // A yaw to the right becomes a yaw to the left.
        TestUtil.SameRotation(UnityQuat.Euler(0, -30, 0), q, 0.05f);
    }

    [Fact]
    public void MirrorX_Twice_IsIdentity()
    {
        var p = new Vector3(0.1f, -0.2f, 0.3f);
        var q = UnityQuat.Euler(20, 30, 40);
        var p0 = p;
        var q0 = q;
        GripMath.MirrorX(ref p, ref q);
        GripMath.MirrorX(ref p, ref q);
        TestUtil.Near(p0, p);
        TestUtil.SameRotation(q0, q);
    }

    [Fact]
    public void FromTo_RotatesOneDirectionOntoAnother()
    {
        var q = GripMath.FromTo(new Vector3(0, 0, 1), new Vector3(1, 0, 0));
        TestUtil.Near(new Vector3(1, 0, 0), UnityQuat.Rotate(q, new Vector3(0, 0, 1)));
        var opposite = GripMath.FromTo(new Vector3(0, 0, 1), new Vector3(0, 0, -1));
        TestUtil.Near(new Vector3(0, 0, -1), UnityQuat.Rotate(opposite, new Vector3(0, 0, 1)));
    }

    [Fact]
    public void AlignAxis_PointsItemAxisAtSecondHand()
    {
        var r0 = UnityQuat.Euler(0, 0, 0);
        var axis = new Vector3(0, 0, 1);
        var r = GripMath.AlignAxis(r0, axis, new Vector3(0, 1, 1));
        TestUtil.Near(Vector3.Normalize(new Vector3(0, 1, 1)), UnityQuat.Rotate(r, axis));
    }

    [Fact]
    public void AlignAxis_DegenerateDirection_KeepsRotation()
    {
        var r0 = UnityQuat.Euler(10, 20, 30);
        TestUtil.SameRotation(r0, GripMath.AlignAxis(r0, new Vector3(0, 0, 1), Vector3.Zero));
    }

    [Fact]
    public void LimitAngle_ClampsLargeRotations()
    {
        var big = UnityQuat.Euler(0, 0, 100);
        var limited = GripMath.LimitAngle(big, 40f);
        Assert.InRange(TestUtil.AngleDeg(Quaternion.Identity, limited), 39.9f, 40.1f);
        var small = UnityQuat.Euler(0, 0, 10);
        TestUtil.SameRotation(small, GripMath.LimitAngle(small, 40f));
    }

    [Fact]
    public void HeadDelta_IsIdentityWhenHmdMatchesLook()
    {
        var hmd = UnityQuat.Euler(-20, 45, 0);
        var d = GripMath.HeadDelta(hmd, yawDeg: 45, pitchDeg: 20, maxDeg: 60);
        TestUtil.SameRotation(Quaternion.Identity, d, 0.05f);
    }

    [Fact]
    public void HeadDelta_CarriesRoll()
    {
        var hmd = UnityQuat.Euler(0, 0, 25);
        var d = GripMath.HeadDelta(hmd, 0, 0, 60);
        Assert.InRange(TestUtil.AngleDeg(Quaternion.Identity, d), 24.9f, 25.1f);
    }

    [Fact]
    public void Slerp_HalfWay()
    {
        var q = GripMath.Slerp(Quaternion.Identity, UnityQuat.Euler(0, 90, 0), 0.5f);
        TestUtil.SameRotation(UnityQuat.Euler(0, 45, 0), q, 0.05f);
    }
}
