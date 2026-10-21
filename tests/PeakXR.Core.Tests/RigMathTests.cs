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
    public void ClearOfBody_LeavesAnEyeOutsideAlone()
    {
        var eye = new Vector3(0f, 2f, -3f);
        TestUtil.Near(
            eye,
            RigMath.ClearOfBody(eye, new Vector3(0f, 1.6f, 0f), new Vector3(0f, 1f, 0f)),
            1e-6f
        );
    }

    [Fact]
    public void ClearOfBody_PushesTheEyeOutOfTheHeadAlongItsOffset()
    {
        var head = new Vector3(0f, 1.6f, 0f);
        var torso = new Vector3(0f, 1f, 0f);
        // Following at the watched player's eyes: 10 cm behind their head centre.
        var p = RigMath.ClearOfBody(new Vector3(0f, 1.6f, -0.1f), head, torso);
        TestUtil.Near(RigMath.HeadClearance, Vector3.Distance(p, head), 1e-4f);
        Assert.True(p.Z < -0.1f);
        TestUtil.Near(1.6f, p.Y, 1e-4f);
    }

    [Fact]
    public void ClearOfBody_EndsOutsideHeadAndTorso()
    {
        var head = new Vector3(0f, 1.6f, 0f);
        var torso = new Vector3(0f, 1f, 0f);
        foreach (
            var eye in new[]
            {
                torso,
                head,
                new Vector3(0.1f, 1.3f, 0.05f),
                new Vector3(-0.2f, 0.9f, -0.3f),
            }
        )
        {
            var p = RigMath.ClearOfBody(eye, head, torso);
            Assert.True(Vector3.Distance(p, head) >= RigMath.HeadClearance - 1e-4f);
            Assert.True(Vector3.Distance(p, torso) >= RigMath.TorsoClearance - 1e-4f);
            Assert.False(float.IsNaN(p.X + p.Y + p.Z));
        }
    }

    [Theory]
    [InlineData(-1f, true)]
    [InlineData(12f, true)]
    [InlineData(3f, false)]
    public void NeedsFloorCue_OnlyOverAVoid(float groundBelow, bool expected) =>
        Assert.Equal(expected, RigMath.NeedsFloorCue(groundBelow));

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

    [Fact]
    public void ReplaceStillView_CutsAtOnce_DriftOnlyAfterTheDwell()
    {
        // A cut: the camera jumped this frame.
        Assert.True(RigMath.ReplaceStillView(frameJump: 5f, drift: 5f, sinceSwitch: 0.1f));
        // A moving shot that left the spot: not again within the dwell, so it cannot strobe.
        Assert.False(RigMath.ReplaceStillView(frameJump: 0.1f, drift: 5f, sinceSwitch: 0.6f));
        Assert.True(RigMath.ReplaceStillView(frameJump: 0.1f, drift: 5f, sinceSwitch: 3.5f));
        // Still near the spot: never.
        Assert.False(RigMath.ReplaceStillView(frameJump: 0.1f, drift: 1f, sinceSwitch: 10f));
    }

    [Fact]
    public void EyeOffset_IsForwardAndUpInTheFacingFrame_IgnoringSideways()
    {
        var center = new Vector3(2f, 1f, -1f);
        // Facing +X (yaw 90): an eye 0.15 m toward +X, 0.3 m up and 0.04 m to the side.
        var eye = center + new Vector3(0.15f, 0.3f, -0.04f);

        var offset = RigMath.EyeOffset(eye, center, 90f);
        TestUtil.Near(0.15f, offset.X, 1e-5f);
        TestUtil.Near(0.3f, offset.Y, 1e-5f);
    }

    [Fact]
    public void EyeAnchor_PutsTheLearnedEyeInFrontOfTheTorsoAlongTheRigHeading()
    {
        var center = new Vector3(2f, 1f, -1f);
        var offset = new Vector2(0.12f, 0.35f);

        // Heading +Z: straight ahead, above the torso.
        TestUtil.Near(new Vector3(2f, 1.35f, -0.88f), RigMath.EyeAnchor(center, 0f, offset), 1e-5f);
        // Learned at one heading, applied at another: same point relative to the body.
        var eye = center + RigMath.RotateYaw(-40f, new Vector3(0f, 0.35f, 0.12f));
        TestUtil.Near(
            eye,
            RigMath.EyeAnchor(center, -40f, RigMath.EyeOffset(eye, center, -40f)),
            1e-5f
        );
    }

    [Fact]
    public void TurnShift_KeepsTheEyeWhereItWasWhileTheBodyTurnsUnderIt()
    {
        var center = new Vector3(2f, 1f, -1f);
        var offset = new Vector2(0.12f, 0.35f);
        var shift = new Vector3(0.01f, 0f, -0.02f);
        var eyeBefore = RigMath.EyeAnchor(center, 30f, offset) + shift;

        var after = RigMath.TurnShift(shift, 30f, 75f, offset.X);

        TestUtil.Near(eyeBefore, RigMath.EyeAnchor(center, 75f, offset) + after, 1e-5f);
        Assert.Equal(0f, after.Y);
        // A full circle of turns gives the shift back.
        var round = shift;
        for (int i = 0; i < 8; i++)
            round = RigMath.TurnShift(round, 30f + 45f * i, 30f + 45f * (i + 1), offset.X);
        TestUtil.Near(shift, round, 1e-5f);
    }

    [Fact]
    public void TurnSettleRate_IsZeroStandingStill_AndScalesWithMoveInput()
    {
        // No input: the view never drifts on its own.
        Assert.Equal(0f, RigMath.TurnSettleRate(0f, 1.5f));
        Assert.Equal(0f, RigMath.TurnSettleRate(0.05f, 1.5f));
        TestUtil.Near(0.75f, RigMath.TurnSettleRate(0.5f, 1.5f), 1e-6f);
        TestUtil.Near(1.5f, RigMath.TurnSettleRate(3f, 1.5f), 1e-6f);
    }

    [Fact]
    public void HoldLevel_AbsorbsTheBodysLevelDriftButNotItsHeight()
    {
        var shift = new Vector3(0.05f, 0f, 0f);
        var before = new Vector3(1f, 1.6f, 2f);
        var after = new Vector3(1.03f, 1.62f, 1.98f);

        var held = RigMath.HoldLevel(shift, before, after);

        // The eye goal (body eye + shift) keeps its level position; height still follows the body.
        TestUtil.Near(before.X + shift.X, after.X + held.X, 1e-6f);
        TestUtil.Near(before.Z + shift.Z, after.Z + held.Z, 1e-6f);
        Assert.Equal(0f, held.Y);
    }
}
