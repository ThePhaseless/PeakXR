namespace PeakXR.Core.Tests;

public class LeftHoldTests
{
    static readonly Vector3 GripPos = new(0.193f, 0.191f, -0.086f); // the flag's Hand_R
    static readonly Quaternion GripRot = UnityQuat.Euler(-20f, 75f, 40f);
    static readonly Vector3 BonePos = new(0.01f, 0.07f, 0.02f);
    static readonly Quaternion BoneRot = UnityQuat.Euler(90f, 0f, 10f);
    static readonly Vector3 Centre = new(0.46f, 0.23f, 0.08f);

    static Vector3 Apply(Vector3 p, Quaternion r, Vector3 q) => p + UnityQuat.Rotate(r, q);

    /// <summary>The left-primary pose: the plain mirror of the right hold, then the plane's correction.</summary>
    static void LeftPose(LeftPlane plane, Vector3 shift, out Vector3 pos, out Quaternion rot)
    {
        GripMath.ItemInHand(GripPos, GripRot, BonePos, BoneRot, out pos, out rot);
        GripMath.MirrorX(ref pos, ref rot);
        LeftHold.Correction(plane, Centre, shift, out var cp, out var cr);
        pos = Apply(pos, rot, cp);
        rot = UnityQuat.Mul(rot, cr);
    }

    [Theory]
    [InlineData(LeftPlane.Origin)]
    [InlineData(LeftPlane.CentreX)]
    [InlineData(LeftPlane.CentreZ)]
    public void EveryPoint_IsTheMirrorImage_OfItsReflectionInTheRightHold(LeftPlane plane)
    {
        GripMath.ItemInHand(GripPos, GripRot, BonePos, BoneRot, out var rp, out var rr);
        LeftPose(plane, Vector3.Zero, out var lp, out var lr);
        foreach (
            var q in new[]
            {
                Vector3.Zero,
                new Vector3(0.3f, -0.1f, 0.5f),
                new Vector3(-0.4f, 0.6f, 0.1f),
            }
        )
        {
            var right = Apply(rp, rr, LeftHold.Reflect(q, plane, Centre));
            var left = Apply(lp, lr, q);
            TestUtil.Near(new Vector3(-right.X, right.Y, right.Z), left, 1e-4f);
        }
    }

    [Theory]
    [InlineData(LeftPlane.Origin)]
    [InlineData(LeftPlane.CentreX)]
    [InlineData(LeftPlane.CentreZ)]
    public void TheLeftHand_HoldsTheReflectedGrip_PlusTheShift(LeftPlane plane)
    {
        var shift = new Vector3(0.03f, -0.05f, 0.02f);
        LeftPose(plane, shift, out var lp, out var lr);
        // The left bone is the mirror of the right bone in the controller frame.
        var bone = new Vector3(-BonePos.X, BonePos.Y, BonePos.Z);
        var onItem = UnityQuat.Rotate(Quaternion.Inverse(lr), bone - lp);
        TestUtil.Near(LeftHold.Reflect(GripPos, plane, Centre) + shift, onItem, 1e-4f);
    }

    [Fact]
    public void Reflect_IsAcrossThePlane()
    {
        var p = new Vector3(0.2f, 0.3f, 0.4f);
        Assert.Equal(new Vector3(-0.2f, 0.3f, 0.4f), LeftHold.Reflect(p, LeftPlane.Origin, Centre));
        TestUtil.Near(
            new Vector3(0.72f, 0.3f, 0.4f),
            LeftHold.Reflect(p, LeftPlane.CentreX, Centre)
        );
        TestUtil.Near(
            new Vector3(0.2f, 0.3f, -0.24f),
            LeftHold.Reflect(p, LeftPlane.CentreZ, Centre)
        );
    }

    [Fact]
    public void Shift_BringsAPalmOffTheItemBackToTheRightHandsGap()
    {
        var palm = new Vector3(0.3f, 0f, 0f);
        var closest = new Vector3(0.1f, 0f, 0f); // 0.2 m off; the right palm was 0.05 m off
        var shift = LeftHold.Shift(0.05f, palm, closest);
        TestUtil.Near(new Vector3(-0.15f, 0f, 0f), shift, 1e-5f);
        // Within the tolerance of the right hold's gap, or nearer: no shift.
        Assert.Equal(Vector3.Zero, LeftHold.Shift(0.05f, palm, new Vector3(0.245f, 0f, 0f)));
        Assert.Equal(Vector3.Zero, LeftHold.Shift(0.05f, palm, palm));
    }

    [Fact]
    public void Choose_KeepsThePlainMirror_WhileThePalmStaysOnTheItem()
    {
        Assert.Equal(LeftPlane.Origin, LeftHold.Choose(0.05f, 0.055f, 0.4f, 0.1f, 0.0f, false));
    }

    [Fact]
    public void Choose_TakesTheSymmetryPlane_ThePalmFallsOff()
    {
        // A flag: lopsided across x, flat across z.
        Assert.Equal(LeftPlane.CentreZ, LeftHold.Choose(0.05f, 0.25f, 0.3f, 0.2f, 0.01f, false));
        // Off-centre but even across its own middle (a shell): the centre plane in x.
        Assert.Equal(LeftPlane.CentreX, LeftHold.Choose(0.05f, 0.25f, 0.3f, 0.02f, 0.03f, false));
        // Nearly as even about the plain mirror: it wins, being the game's own x.
        Assert.Equal(LeftPlane.Origin, LeftHold.Choose(0.05f, 0.25f, 0.025f, 0.02f, 0.01f, false));
    }

    [Fact]
    public void Choose_NeverTurnsALaunchersBarrelBack()
    {
        Assert.Equal(LeftPlane.CentreX, LeftHold.Choose(0.05f, 0.25f, 0.3f, 0.2f, 0.01f, true));
    }

    [Fact]
    public void Choose_RejectsAPlaneThatTurnsTheFarEndRound()
    {
        // The checkpoint flag in the headset (vr.244): the colliders read it nearly as even across x as across z, and
        // the x plane put its pole's far end on the other side of the fist.
        Assert.Equal(
            LeftPlane.CentreZ,
            LeftHold.Choose(
                0.22f,
                0.33f,
                0.4f,
                0.04f,
                0.031f,
                false,
                xTurns: true,
                originTurns: true
            )
        );
        // Even across the plane, turning it round shows the same item: allowed.
        Assert.Equal(
            LeftPlane.CentreX,
            LeftHold.Choose(
                0.05f,
                0.25f,
                0.3f,
                0.01f,
                0.03f,
                false,
                xTurns: true,
                originTurns: true
            )
        );
        // Every plane turns it and none is even: the plain mirror, as before.
        Assert.Equal(
            LeftPlane.Origin,
            LeftHold.Choose(0.05f, 0.25f, 0.3f, 0.2f, 0.1f, false, true, true, true)
        );
    }

    [Fact]
    public void Choose_LeavesThePlainMirror_WhenItTurnsAnUnevenItemRound()
    {
        // The palm stays on, but the item lies the other way round: the z plane keeps it.
        Assert.Equal(
            LeftPlane.CentreZ,
            LeftHold.Choose(0.05f, 0.05f, 0.3f, 0.2f, 0.01f, false, originTurns: true, xTurns: true)
        );
        // Even across x = 0 (a ball): turning it round shows the same thing.
        Assert.Equal(
            LeftPlane.Origin,
            LeftHold.Choose(0.05f, 0.05f, 0.01f, 0.2f, 0.3f, false, originTurns: true)
        );
    }

    [Fact]
    public void TurnsEnd_WhenTheReflectedFarEndCrossesTheGrip()
    {
        // The flag: pole along x, grip near one end, far end at the other.
        var centre = new Vector3(0.46f, 0.23f, 0.08f);
        var grip = new Vector3(0.19f, 0.19f, -0.09f);
        var end = new Vector3(1.15f, 0.23f, 0.08f);
        Assert.True(LeftHold.TurnsEnd(LeftPlane.CentreX, centre, grip, end));
        Assert.True(LeftHold.TurnsEnd(LeftPlane.Origin, centre, grip, end));
        Assert.False(LeftHold.TurnsEnd(LeftPlane.CentreZ, centre, grip, end));
    }

    [Fact]
    public void Asymmetry_IsZeroForAMirrorPair_AndScalesWithSize()
    {
        var d = new[] { 0.1f, 0.2f, 0.3f };
        Assert.Equal(0f, LeftHold.Asymmetry(d, d, 1f));
        Assert.Equal(0.05f, LeftHold.Asymmetry(d, new[] { 0.2f, 0.1f, 0.3f }, 4f / 3f), 4);
    }

    [Fact]
    public void Probes_RingTheGrownBox_WithoutItsCentre()
    {
        var probes = new Vector3[LeftHold.ProbeCount];
        LeftHold.Probes(new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f), probes);
        Assert.DoesNotContain(Vector3.Zero, probes);
        Assert.Contains(new Vector3(1.2f, 1.2f, 1.2f), probes);
        Assert.Equal(26, probes.Length);
    }
}
