namespace PeakXR.Core.Tests;

public class WorldScaleTests
{
    [Fact]
    public void OriginPosition_ScaledRigStillPutsTheHeadOnTheAnchor()
    {
        var anchor = new Vector3(10f, 5f, -3f);
        var center = new Vector2(0.4f, -0.2f);
        const float yaw = 63f;
        const float eye = 1.6f;
        const float s = 1.25f;

        var origin = RigMath.OriginPosition(anchor, yaw, center, eye, s);

        // A scaled tracking space: every tracked metre is s world units.
        var head = origin + s * RigMath.RotateYaw(yaw, new Vector3(center.X, eye, center.Y));
        TestUtil.Near(anchor, head, 1e-4f);
    }

    [Fact]
    public void OriginPosition_UnitScaleIsTheOldPlacement()
    {
        var anchor = new Vector3(1f, 2f, 3f);
        var center = new Vector2(0.1f, 0.2f);
        TestUtil.Near(
            RigMath.OriginPosition(anchor, 30f, center, 1.7f),
            RigMath.OriginPosition(anchor, 30f, center, 1.7f, 1f)
        );
    }

    [Fact]
    public void RigScale_IsTheInverseOfHowBigTheWorldLooks()
    {
        TestUtil.Near(1f, WorldScale.RigScale(1f));
        TestUtil.Near(1.25f, WorldScale.RigScale(0.8f));
        // Out of range values clamp, a broken config never shrinks the player to nothing.
        TestUtil.Near(1f / WorldScale.Min, WorldScale.RigScale(0.01f));
        TestUtil.Near(1f / WorldScale.Max, WorldScale.RigScale(9f));
        TestUtil.Near(1f, WorldScale.RigScale(float.NaN));
    }

    [Fact]
    public void Conversions_KeepRealMetres()
    {
        TestUtil.Near(0.25f, WorldScale.ToWorld(0.2f, 1.25f));
        TestUtil.Near(0.2f, WorldScale.ToReal(0.25f, 1.25f));
    }

    [Fact]
    public void FloorReach_TheGameFloorIsAboveTheRealOneWhenThePlayerIsBigger()
    {
        // Eyes ride the avatar's 1.61 m eyes; a 1.60 m real eye at rig scale 1.25 reaches the game floor with the
        // hand 0.31 m above the real one.
        TestUtil.Near(0.312f, WorldScale.FloorReach(1.61f, 1.6f, 1.25f), 1e-3f);
        TestUtil.Near(0f, WorldScale.FloorReach(1.6f, 1.6f, 1f));
    }

    [Fact]
    public void Auto_PutsTheRealFloorOnTheGameFloor()
    {
        // A 1.52 m real eye on the scout's 1.64 m eyes: the world looks 0.93 its size, and the hand reaches the
        // game floor at the real floor.
        float auto = WorldScale.Auto(1.52f, 1.64f);
        TestUtil.Near(0.93f, auto, 1e-4f);
        TestUtil.Near(0f, WorldScale.FloorReach(1.64f, 1.52f, WorldScale.RigScale(auto)), 0.01f);
        // A tall player grows the world past the game's own size.
        TestUtil.Near(1.1f, WorldScale.Auto(1.80f, 1.64f), 1e-4f);
    }

    [Fact]
    public void Auto_StaysInTheSaneBand()
    {
        // A height taken kneeling (or a child) never shrinks the world below AutoMin; a giant never past AutoMax.
        TestUtil.Near(WorldScale.AutoMin, WorldScale.Auto(0.9f, 1.64f));
        TestUtil.Near(WorldScale.AutoMax, WorldScale.Auto(2.4f, 1.64f));
        // Implausible heights (untracked headset, a broken eye sample) read as the standing default.
        TestUtil.Near(1f, WorldScale.Auto(float.NaN, HeightCalibration.DefaultStandingEyeHeight));
        TestUtil.Near(1f, WorldScale.Auto(HeightCalibration.DefaultStandingEyeHeight, 0f));
        // Auto values are settable values: RigScale passes them through unclamped.
        TestUtil.Near(1f / WorldScale.AutoMax, WorldScale.RigScale(WorldScale.AutoMax));
    }

    [Fact]
    public void FromReferences_IsTheMedianRealOverGameRatio()
    {
        var refs = new (float Game, float Real)[] { (2f, 1f), (1f, 0.8f), (4f, 2.4f) };
        TestUtil.Near(0.6f, WorldScale.FromReferences(refs));
    }

    [Fact]
    public void Default_IsTheMeasuredRatioWithinTheComfortBand()
    {
        float measured = WorldScale.FromReferences(WorldScale.References);
        TestUtil.Near(WorldScale.Comfortable(measured), WorldScale.Default, 1e-3f);
        Assert.InRange(WorldScale.Default, WorldScale.ComfortMin, WorldScale.Max);
    }

    [Fact]
    public void ScaleEye_WidensTheEyeOffsetOnly()
    {
        // Row-vector view matrices (System.Numerics): a centre view, and an eye 32 mm to its left, canted 2 degrees.
        var center =
            Matrix4x4.CreateFromYawPitchRoll(0.3f, -0.1f, 0f)
            * Matrix4x4.CreateTranslation(new Vector3(1f, 2f, 3f));
        center = Invert(center);
        var offset =
            Matrix4x4.CreateRotationY(0.035f) * Matrix4x4.CreateTranslation(-0.032f, 0f, 0f);
        var eye = center * Invert(offset);

        var scaled = StereoScale.ScaleEye(eye, center, 1.5f);

        var wanted =
            center
            * Invert(
                Matrix4x4.CreateRotationY(0.035f) * Matrix4x4.CreateTranslation(-0.048f, 0f, 0f)
            );
        for (int r = 0; r < 4; r++)
        for (int c = 0; c < 4; c++)
            TestUtil.Near(Get(wanted, r, c), Get(scaled, r, c), 1e-4f);
        // Unit scale leaves the eye alone.
        var same = StereoScale.ScaleEye(eye, center, 1f);
        TestUtil.Near(Get(eye, 3, 0), Get(same, 3, 0), 1e-5f);
    }

    static Matrix4x4 Invert(Matrix4x4 m)
    {
        Matrix4x4.Invert(m, out var inv);
        return inv;
    }

    static float Get(Matrix4x4 m, int r, int c) =>
        (r, c) switch
        {
            (0, 0) => m.M11,
            (0, 1) => m.M12,
            (0, 2) => m.M13,
            (0, 3) => m.M14,
            (1, 0) => m.M21,
            (1, 1) => m.M22,
            (1, 2) => m.M23,
            (1, 3) => m.M24,
            (2, 0) => m.M31,
            (2, 1) => m.M32,
            (2, 2) => m.M33,
            (2, 3) => m.M34,
            (3, 0) => m.M41,
            (3, 1) => m.M42,
            (3, 2) => m.M43,
            _ => m.M44,
        };
}
