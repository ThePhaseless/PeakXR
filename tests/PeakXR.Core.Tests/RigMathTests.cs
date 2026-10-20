namespace PeakXR.Core.Tests;

public class RigMathTests
{
    [Fact]
    public void RotateYaw_MatchesUnityConvention()
    {
        // Unity: Quaternion.Euler(0, 90, 0) * forward = right.
        TestUtil.Near(
            new Vector3(1f, 0f, 0f),
            RigMath.RotateYaw(90f, new Vector3(0f, 0f, 1f)),
            1e-5f
        );
        TestUtil.Near(
            new Vector3(0f, 0f, -1f),
            RigMath.RotateYaw(90f, new Vector3(1f, 0f, 0f)),
            1e-5f
        );
        TestUtil.Near(
            new Vector3(0f, 2f, 0f),
            RigMath.RotateYaw(37f, new Vector3(0f, 2f, 0f)),
            1e-5f
        );
    }

    [Fact]
    public void OriginPosition_PutsCalibratedHeadOnTheAnchor()
    {
        var anchor = new Vector3(10f, 5f, -3f);
        var center = new Vector2(0.4f, -0.2f);
        const float yaw = 63f;
        const float eye = 1.7f;

        Vector3 origin = RigMath.OriginPosition(anchor, yaw, center, eye);

        // Tracking-space head standing on the calibrated spot at the calibrated eye height.
        Vector3 headWorld = origin + RigMath.RotateYaw(yaw, new Vector3(center.X, eye, center.Y));
        TestUtil.Near(anchor, headWorld, 1e-4f);
    }

    [Fact]
    public void PivotCenter_KeepsHeadFixedInWorldWhenTurning()
    {
        var anchor = new Vector3(1f, 2f, 3f);
        var center = new Vector2(0.3f, 0.5f);
        var head = new Vector2(-0.4f, 0.9f);
        const float eye = 1.6f;
        const float yaw = 20f;
        const float delta = 30f;

        Vector3 Head(float y, Vector2 c) =>
            RigMath.OriginPosition(anchor, y, c, eye)
            + RigMath.RotateYaw(y, new Vector3(head.X, eye, head.Y));

        Vector3 before = Head(yaw, center);
        Vector2 newCenter = RigMath.PivotCenter(head, center, delta);
        Vector3 after = Head(yaw + delta, newCenter);

        TestUtil.Near(before, after, 1e-4f);
    }

    [Fact]
    public void PivotCenter_NoTurn_KeepsCenter()
    {
        var c = new Vector2(0.3f, -0.7f);
        Vector2 r = RigMath.PivotCenter(new Vector2(1f, 1f), c, 0f);
        TestUtil.Near(new Vector3(c.X, 0f, c.Y), new Vector3(r.X, 0f, r.Y), 1e-6f);
    }

    [Fact]
    public void RecenterYaw_AlignsHeadsetWithBody()
    {
        float yaw = RigMath.RecenterYaw(bodyYawDeg: 100f, hmdLocalYawDeg: 30f);
        TestUtil.Near(70f, yaw, 1e-4f);
        TestUtil.Near(-170f, RigMath.RecenterYaw(170f, -20f), 1e-4f);
    }

    [Fact]
    public void EyeHeight_FloorTracking_UsesCalibration()
    {
        TestUtil.Near(1.75f, RigMath.EyeHeight(true, HeightMode.Standing, 1.75f), 1e-5f);
        // Not tracked yet (zero height) falls back to the mode default.
        TestUtil.Near(
            HeightCalibration.DefaultStandingEyeHeight,
            RigMath.EyeHeight(true, HeightMode.Standing, 0f),
            1e-5f
        );
        TestUtil.Near(
            HeightCalibration.DefaultSeatedEyeHeight,
            RigMath.EyeHeight(true, HeightMode.Seated, float.NaN),
            1e-5f
        );
    }

    [Fact]
    public void EyeHeight_DeviceTracking_UsesHeadsetHeightAsIs()
    {
        // Device-relative tracking spaces start at the head, so whatever Y the headset reports is the eye.
        TestUtil.Near(0.02f, RigMath.EyeHeight(false, HeightMode.Standing, 0.02f), 1e-6f);
        TestUtil.Near(0f, RigMath.EyeHeight(false, HeightMode.Standing, float.NaN), 1e-6f);
    }

    [Fact]
    public void Follow_ConvergesAndRespectsMaxLag()
    {
        var target = new Vector3(10f, 0f, 0f);
        Vector3 p = RigMath.Follow(Vector3.Zero, target, 0.01f, 5f, 5f, 0.1f);
        // Clamped to within 0.1 m of the target.
        Assert.True(Vector3.Distance(p, target) <= 0.1f + 1e-5f);

        Vector3 q = new Vector3(9.95f, 0f, 0f);
        for (int i = 0; i < 200; i++)
            q = RigMath.Follow(q, target, 0.02f, 5f, 5f, 0.1f);
        TestUtil.Near(target, q, 1e-3f);
    }

    [Fact]
    public void Follow_SeparateVerticalRate()
    {
        var target = new Vector3(1f, 1f, 0f);
        Vector3 p = RigMath.Follow(Vector3.Zero, target, 0.1f, 1000f, 0f, 100f);
        TestUtil.Near(1f, p.X, 1e-4f);
        TestUtil.Near(0f, p.Y, 1e-6f);
    }

    [Fact]
    public void Follow_NegativeRateSnaps()
    {
        var target = new Vector3(3f, 4f, 5f);
        TestUtil.Near(target, RigMath.Follow(Vector3.Zero, target, 0.016f, -1f, -1f, 10f), 1e-6f);
    }

    [Fact]
    public void HeightOffset_MapsMeasuredEyeToAvatarEye()
    {
        // Standing at 1.80 m with floor tracking: tracking Y 1.80 must land on the avatar's eyes.
        float offset = RigMath.HeightOffset(true, HeightMode.Standing, 1.8f);
        TestUtil.Near(1.8f, RigMath.TrackedEyeHeight(offset), 1e-5f);
        TestUtil.Near(HeightCalibration.DefaultStandingEyeHeight - 1.8f, offset, 1e-5f);

        // Seated at 1.15 m: the seated head height maps to the avatar's standing eyes.
        TestUtil.Near(
            1.15f,
            RigMath.TrackedEyeHeight(RigMath.HeightOffset(true, HeightMode.Seated, 1.15f)),
            1e-5f
        );

        // Device-relative tracking: whatever the head reports at recenter is the eye.
        TestUtil.Near(
            0.03f,
            RigMath.TrackedEyeHeight(RigMath.HeightOffset(false, HeightMode.Standing, 0.03f)),
            1e-5f
        );
    }

    [Fact]
    public void ThirdPersonPoint_BehindAndAboveAlongRigYaw()
    {
        var target = new Vector3(5f, 1f, 5f);
        TestUtil.Near(new Vector3(5f, 2f, 3f), RigMath.ThirdPersonPoint(target, 0f, 2f, 1f), 1e-5f);
        // Facing +X (yaw 90): behind is -X.
        TestUtil.Near(
            new Vector3(3f, 2f, 5f),
            RigMath.ThirdPersonPoint(target, 90f, 2f, 1f),
            1e-5f
        );
    }
}
