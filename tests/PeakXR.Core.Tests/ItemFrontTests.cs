namespace PeakXR.Core.Tests;

public class ItemFrontTests
{
    static readonly Vector3 Pivot = new(0.24f, -0.10f, -0.04f); // BingBong's Hand_R
    static readonly Vector3 Centre = new(-0.08f, -0.16f, 0.18f);
    static readonly Vector3 Face = new(0f, 0f, -1f);

    static Vector3 At(Vector3 pos, Quaternion rot, Vector3 local) =>
        pos + UnityQuat.Rotate(rot, local);

    [Theory]
    [InlineData("BingBong(Clone)", "BingBong")]
    [InlineData("Compass (Clone)", "Compass")]
    [InlineData("Granola Bar", "Granola Bar")]
    public void Key_DropsTheCloneSuffix(string name, string key) =>
        Assert.Equal(key, ItemFront.Key(name));

    [Fact]
    public void Table_HasFronts_ForPresentableItems_AndNoneForAimedOrUnknownOnes()
    {
        Assert.True(ItemFront.TryGet("BingBong(Clone)", out var face));
        TestUtil.Near(new Vector3(0f, 0f, -1f), face, 1e-3f);
        Assert.True(ItemFront.TryGet("Compass", out _));
        Assert.True(ItemFront.TryGet("Passport", out _));
        Assert.True(ItemFront.TryGet("Amulet_Healing", out _));
        Assert.False(ItemFront.TryGet("RopeShooter", out _));
        Assert.False(ItemFront.TryGet("Bugle", out _));
        Assert.False(ItemFront.TryGet("Apple Berry Red", out _));
        Assert.False(ItemFront.TryGet("SomeFutureItem", out _));
        Assert.False(ItemFront.TryGet(null, out _));
    }

    [Fact]
    public void EveryFront_IsAUnitVector()
    {
        foreach (var front in ItemFront.All)
            TestUtil.Near(1f, front.Length(), 2e-3f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EyeInGrip_LiesBackAlongTheForearm_TowardThePalm(bool left)
    {
        var eye = ItemFront.EyeInGrip(left);
        TestUtil.Near(1f, eye.Length(), 1e-4f);
        Assert.True(Vector3.Dot(eye, GripFrame.Forearm) > 0.8f);
        Assert.True(Vector3.Dot(eye, GripFrame.Palm(left)) > 0f);
        Assert.True(Vector3.Dot(eye, GripFrame.ThumbSide) > 0f);
    }

    public static TheoryData<float, float, float> Rotations =>
        new()
        {
            { 0f, 0f, 0f },
            { 30f, 120f, -40f },
            { -70f, 200f, 15f },
            { 160f, -35f, 90f },
        };

    static float Facing(Vector3 pos, Quaternion rot, Vector3 eyePoint, float scale = 1f)
    {
        var grip = At(pos, rot, Pivot);
        var mid = grip + scale * UnityQuat.Rotate(rot, Centre - Pivot);
        return Vector3.Dot(UnityQuat.Rotate(rot, Face), Vector3.Normalize(eyePoint - mid));
    }

    static float ArmTurnDeg(Quaternion a, Quaternion b)
    {
        var u = Vector3.Normalize(UnityQuat.Rotate(a, Centre - Pivot));
        var v = Vector3.Normalize(UnityQuat.Rotate(b, Centre - Pivot));
        return MathF.Acos(Math.Clamp(Vector3.Dot(u, v), -1f, 1f)) * UnityQuat.Rad2Deg;
    }

    [Theory]
    [MemberData(nameof(Rotations))]
    public void Turn_KeepsTheHandOnTheGrip(float x, float y, float z)
    {
        var rot = UnityQuat.Euler(x, y, z);
        var pos = new Vector3(0.02f, -0.05f, 0.11f);
        ItemFront.Turn(
            pos,
            rot,
            Pivot,
            Centre,
            0.75f,
            Face,
            ItemFront.EyePoint(false),
            ItemFront.MaxTilt,
            out var p2,
            out var r2
        );
        TestUtil.Near(At(pos, rot, Pivot), At(p2, r2, Pivot), 1e-4f);
    }

    [Theory]
    [MemberData(nameof(Rotations))]
    public void Turn_WithoutTilt_OnlySpins_LeavingTheMiddleWhereItWas(float x, float y, float z)
    {
        var rot = UnityQuat.Euler(x, y, z);
        var eye = ItemFront.EyePoint(true);
        ItemFront.Turn(Vector3.Zero, rot, Pivot, Centre, 1f, Face, eye, 0f, out var p2, out var r2);
        TestUtil.Near(At(Vector3.Zero, rot, Centre), At(p2, r2, Centre), 1e-4f);
        Assert.True(Facing(p2, r2, eye) >= Facing(Vector3.Zero, rot, eye) - 1e-4f);
    }

    [Theory]
    [MemberData(nameof(Rotations))]
    public void Turn_TiltsNoFurtherThanItsBudget_AndNeverLessFacing(float x, float y, float z)
    {
        var rot = UnityQuat.Euler(x, y, z);
        var eye = ItemFront.EyePoint(false);
        ItemFront.Turn(
            Vector3.Zero,
            rot,
            Pivot,
            Centre,
            1f,
            Face,
            eye,
            30f,
            out var p2,
            out var r2
        );
        Assert.True(ArmTurnDeg(rot, r2) <= 30.01f, $"{ArmTurnDeg(rot, r2)} degrees");
        ItemFront.Turn(Vector3.Zero, rot, Pivot, Centre, 1f, Face, eye, 0f, out var p0, out var r0);
        Assert.True(Facing(p2, r2, eye) >= Facing(p0, r0, eye) - 1e-3f);
    }

    [Fact]
    public void Turn_TiltsADialTheSpinCannotReach_TowardTheEyes()
    {
        // The front runs along the grip-to-middle line, so no spin about it moves the front: only the tilt can.
        var eye = ItemFront.EyePoint(false);
        var arm = Vector3.Normalize(Centre - Pivot);
        var rot = GripMath.FromTo(arm, Vector3.Normalize(new Vector3(0f, -1f, 0.6f)));
        ItemFront.Turn(Vector3.Zero, rot, Pivot, Centre, 1f, arm, eye, 0f, out var ps, out var rs);
        ItemFront.Turn(
            Vector3.Zero,
            rot,
            Pivot,
            Centre,
            1f,
            arm,
            eye,
            ItemFront.MaxTilt,
            out var pt,
            out var rt
        );
        TestUtil.SameRotation(rot, rs, 0.05f);
        var mid = At(pt, rt, Centre);
        float facing = Vector3.Dot(UnityQuat.Rotate(rt, arm), Vector3.Normalize(eye - mid));
        Assert.True(
            facing
                > Vector3.Dot(
                    UnityQuat.Rotate(rot, arm),
                    Vector3.Normalize(eye - At(ps, rs, Centre))
                )
        );
    }

    [Fact]
    public void Turn_ShowsTheFace_OfAnItemHeldFacingAway()
    {
        // The right hold puts the face straight away from the eyes, across the arm from the middle.
        var eye = ItemFront.EyePoint(false);
        var dir = Vector3.Normalize(eye);
        var axis = Vector3.Normalize(Vector3.Cross(dir, new Vector3(0f, 0f, 1f)));
        var rot = GripMath.FromTo(Face, -dir);
        var centre = UnityQuat.Rotate(Quaternion.Inverse(rot), axis * 0.2f);
        ItemFront.Turn(
            Vector3.Zero,
            rot,
            Vector3.Zero,
            centre,
            1f,
            Face,
            eye,
            ItemFront.MaxTilt,
            out var p2,
            out var r2
        );
        var mid = At(p2, r2, centre);
        Assert.True(Vector3.Dot(UnityQuat.Rotate(r2, Face), Vector3.Normalize(eye - mid)) > 0.95f);
    }

    [Fact]
    public void Turn_AnItemCentredOnTheGrip_TurnsTheFrontStraightAtTheEyes()
    {
        var rot = UnityQuat.Euler(10f, 150f, 30f);
        var eye = ItemFront.EyePoint(false);
        ItemFront.Turn(
            Vector3.Zero,
            rot,
            Pivot,
            Pivot,
            1f,
            Face,
            eye,
            ItemFront.MaxTilt,
            out var p2,
            out var r2
        );
        var grip = At(Vector3.Zero, rot, Pivot);
        TestUtil.Near(
            1f,
            Vector3.Dot(UnityQuat.Rotate(r2, Face), Vector3.Normalize(eye - grip)),
            1e-3f
        );
        TestUtil.Near(grip, At(p2, r2, Pivot), 1e-4f);
    }

    [Fact]
    public void Turn_IsTheSameEveryFrame_ForTheSameHold()
    {
        // Decided by the hold alone, not by where the head is: the item never swims as the wrist turns.
        var rot = UnityQuat.Euler(25f, 80f, -10f);
        var eye = ItemFront.EyePoint(false);
        ItemFront.Turn(
            Vector3.One,
            rot,
            Pivot,
            Centre,
            0.75f,
            Face,
            eye,
            60f,
            out var p1,
            out var r1
        );
        ItemFront.Turn(
            Vector3.One,
            rot,
            Pivot,
            Centre,
            0.75f,
            Face,
            eye,
            60f,
            out var p2,
            out var r2
        );
        Assert.Equal(p1, p2);
        Assert.Equal(r1, r2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NaturalGrip_PointsTheEyeLineAtTheEyes_ThumbUp(bool left)
    {
        var toEyes = Vector3.Normalize(new Vector3(left ? 0.2f : -0.2f, 0.3f, -0.9f));
        var up = new Vector3(0f, 1f, 0f);
        var rot = ItemFront.NaturalGrip(toEyes, up, left);
        TestUtil.Near(toEyes, UnityQuat.Rotate(rot, ItemFront.EyeInGrip(left)), 1e-4f);
        Assert.True(Vector3.Dot(UnityQuat.Rotate(rot, GripFrame.ThumbSide), up) > 0.7f);
    }
}
