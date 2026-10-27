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

    [Theory]
    [MemberData(nameof(Rotations))]
    public void Turn_KeepsTheGripAndTheMiddleWhereTheyWere(float x, float y, float z)
    {
        var rot = UnityQuat.Euler(x, y, z);
        var pos = new Vector3(0.02f, -0.05f, 0.11f);
        var eye = ItemFront.EyeInGrip(false);
        ItemFront.Turn(pos, rot, Pivot, Centre, Face, eye, out var p2, out var r2);
        TestUtil.Near(At(pos, rot, Pivot), At(p2, r2, Pivot), 1e-4f);
        TestUtil.Near(At(pos, rot, Centre), At(p2, r2, Centre), 1e-4f);
    }

    [Theory]
    [MemberData(nameof(Rotations))]
    public void Turn_FacesTheFront_AsNearTheEyesAsASpinAboutTheMiddleCan(float x, float y, float z)
    {
        var rot = UnityQuat.Euler(x, y, z);
        var eye = ItemFront.EyeInGrip(true);
        ItemFront.Turn(Vector3.Zero, rot, Pivot, Centre, Face, eye, out _, out var r2);
        float before = Vector3.Dot(UnityQuat.Rotate(rot, Face), eye);
        float after = Vector3.Dot(UnityQuat.Rotate(r2, Face), eye);
        // The best a turn about the grip-to-middle axis reaches: the front's and the eyes' parts across it aligned.
        var axis = Vector3.Normalize(UnityQuat.Rotate(rot, Centre - Pivot));
        float fa = Vector3.Dot(UnityQuat.Rotate(rot, Face), axis);
        float ea = Vector3.Dot(eye, axis);
        float best = fa * ea + MathF.Sqrt(1f - fa * fa) * MathF.Sqrt(1f - ea * ea);
        Assert.True(after >= before - 1e-4f, $"{after} < {before}");
        TestUtil.Near(best, after, 1e-3f);
    }

    [Fact]
    public void Turn_ShowsTheFace_OfAnItemHeldFacingAway()
    {
        // The right hold puts the face straight away from the eyes, across the arm from the middle.
        var eye = ItemFront.EyeInGrip(false);
        var axis = Vector3.Normalize(Vector3.Cross(eye, new Vector3(0f, 0f, 1f)));
        var rot = GripMath.FromTo(Face, -eye);
        var pivot = Vector3.Zero;
        var centre = UnityQuat.Rotate(Quaternion.Inverse(rot), axis * 0.2f);
        ItemFront.Turn(Vector3.Zero, rot, pivot, centre, Face, eye, out _, out var r2);
        Assert.True(Vector3.Dot(UnityQuat.Rotate(r2, Face), eye) > 0.99f);
    }

    [Fact]
    public void Turn_AnItemCentredOnTheGrip_TurnsTheFrontStraightAtTheEyes()
    {
        var rot = UnityQuat.Euler(10f, 150f, 30f);
        var eye = ItemFront.EyeInGrip(false);
        ItemFront.Turn(Vector3.Zero, rot, Pivot, Pivot, Face, eye, out var p2, out var r2);
        TestUtil.Near(1f, Vector3.Dot(UnityQuat.Rotate(r2, Face), eye), 1e-3f);
        TestUtil.Near(At(Vector3.Zero, rot, Pivot), At(p2, r2, Pivot), 1e-4f);
    }

    [Fact]
    public void Turn_IsTheSameEveryFrame_ForTheSameHold()
    {
        // Decided by the hold alone, not by where the head is: the item never swims as the wrist turns.
        var rot = UnityQuat.Euler(25f, 80f, -10f);
        var eye = ItemFront.EyeInGrip(false);
        ItemFront.Turn(Vector3.One, rot, Pivot, Centre, Face, eye, out var p1, out var r1);
        ItemFront.Turn(Vector3.One, rot, Pivot, Centre, Face, eye, out var p2, out var r2);
        Assert.Equal(p1, p2);
        Assert.Equal(r1, r2);
    }
}
