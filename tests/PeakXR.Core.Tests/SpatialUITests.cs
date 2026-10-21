namespace PeakXR.Core.Tests;

public class SpatialMathTests
{
    [Fact]
    public void MetersPerPixel_GrowsLinearlyWithDistance()
    {
        var near = SpatialMath.MetersPerPixel(1f, 0.05f);
        var far = SpatialMath.MetersPerPixel(3f, 0.05f);
        TestUtil.Near(3f * near, far, 1e-6f);
        TestUtil.Near(MathF.Tan(0.05f * MathF.PI / 180f), near, 1e-6f);
        Assert.Equal(0f, SpatialMath.MetersPerPixel(-1f, 0.05f));
    }

    [Fact]
    public void SpanMeters_IsChordOfTheAngle()
    {
        // 90 degrees at 1 m spans 2 m (2 * tan 45).
        TestUtil.Near(2f, SpatialMath.SpanMeters(1f, 90f), 1e-5f);
        TestUtil.Near(0f, SpatialMath.SpanMeters(0f, 30f));
    }

    [Fact]
    public void DegreesPerPixel_InvertsMetersPerPixel()
    {
        var mpp = SpatialMath.MetersPerPixel(1.2f, 0.04f);
        TestUtil.Near(0.04f, SpatialMath.DegreesPerPixel(mpp, 1.2f), 1e-5f);
        Assert.Equal(0f, SpatialMath.DegreesPerPixel(0.001f, 0f));
    }

    static float Degrees(Vector3 a, Vector3 b) =>
        MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)), -1f, 1f))
        * 180f
        / MathF.PI;

    [Fact]
    public void TargetLabel_SitsInFrontOfTheTargetAndAboveTheSightLine()
    {
        var eye = new Vector3(0f, 1.6f, 0f);
        var center = new Vector3(0f, 1.0f, 1.5f);
        var p = SpatialMath.TargetLabel(center, eye, 0.2f, 1f);
        // Pulled toward the eye by the radius, so its depth is never inside the object.
        Assert.True(Vector3.Distance(eye, p) <= Vector3.Distance(eye, center) - 0.2f + 1e-4f);
        // Above the straight line from the eye to the target centre.
        var along = Vector3.Normalize(center - eye);
        var onLine = eye + along * Vector3.Dot(p - eye, along);
        Assert.True(p.Y > onLine.Y);
    }

    [Theory]
    // An item on the floor seen from above, a big case at arm's length, a small item across the room.
    [InlineData(0f, 0.05f, 0.5f, 0.12f)]
    [InlineData(0f, 0.6f, 0.9f, 0.6f)]
    [InlineData(1f, 1.2f, 4f, 0.1f)]
    public void TargetLabel_NeverCrossesTheTargetInView(float x, float y, float z, float radius)
    {
        var eye = new Vector3(0f, 1.6f, 0f);
        var center = new Vector3(x, y, z);
        const float halfHeight = 1.2f;
        var p = SpatialMath.TargetLabel(center, eye, radius, halfHeight);
        // The label's lower edge clears the target's silhouette (its bounding sphere seen from the eye).
        float silhouette =
            MathF.Asin(MathF.Min(1f, radius / Vector3.Distance(eye, center))) * 180f / MathF.PI;
        Assert.True(Degrees(p - eye, center - eye) - halfHeight >= silhouette);
        Assert.False(float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsNaN(p.Z));
    }

    [Fact]
    public void TargetLabel_TallerLabelSitsHigher()
    {
        var eye = new Vector3(0f, 1.6f, 0f);
        var center = new Vector3(0f, 1f, 1.5f);
        var low = SpatialMath.TargetLabel(center, eye, 0.2f, 0.5f);
        var high = SpatialMath.TargetLabel(center, eye, 0.2f, 2f);
        Assert.True(Degrees(high - eye, center - eye) > Degrees(low - eye, center - eye) + 1f);
    }

    [Fact]
    public void TargetLabel_ClampsToComfortableRange()
    {
        var eye = Vector3.Zero;
        var near = SpatialMath.TargetLabel(new Vector3(0f, 0f, 0.6f), eye, 0.05f, 1f);
        Assert.True(near.Length() >= SpatialMath.TargetNear - 1e-4f);
        // Closer than the comfort range: never further than the target itself.
        var close = SpatialMath.TargetLabel(new Vector3(0f, 0f, 0.3f), eye, 0.05f, 1f);
        Assert.True(close.Length() <= 0.3f + 1e-4f);
        var far = SpatialMath.TargetLabel(new Vector3(0f, 0f, 10f), eye, 0.05f, 1f);
        TestUtil.Near(SpatialMath.TargetFar, far.Length(), 1e-3f);
    }

    [Fact]
    public void TargetLabel_OnTheEye_GoesAbove()
    {
        var p = SpatialMath.TargetLabel(Vector3.One, Vector3.One, 0.1f, 1f);
        Assert.True(p.Y > 1f);
    }

    [Fact]
    public void TargetLabel_LookingStraightDown_StaysFinite()
    {
        var eye = new Vector3(0f, 1.6f, 0f);
        var p = SpatialMath.TargetLabel(Vector3.Zero, eye, 0.1f, 1f);
        Assert.False(float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsNaN(p.Z));
        Assert.True(Degrees(p - eye, -Vector3.UnitY) > 4f);
    }

    [Fact]
    public void Banner_IsAheadAlongYawAndAboveEyeLevel()
    {
        var eye = new Vector3(5f, 2f, 0f);
        var p = SpatialMath.Banner(eye, 90f, 30f, 8f);
        Assert.True(p.X > eye.X + 25f);
        TestUtil.Near(eye.Z, p.Z, 1e-3f);
        TestUtil.Near(30f * MathF.Sin(8f * MathF.PI / 180f), p.Y - eye.Y, 1e-3f);
        TestUtil.Near(30f, Vector3.Distance(eye, p), 1e-3f);
    }

    [Fact]
    public void ArcSlot_IsSymmetricAndRunsLeftToRightForTheViewer()
    {
        // Viewer on +Z looking back at the player (yaw 0 points at them): their right is -X.
        var first = SpatialMath.ArcSlot(0, 3, 0f, 0.4f, 30f);
        var middle = SpatialMath.ArcSlot(1, 3, 0f, 0.4f, 30f);
        var last = SpatialMath.ArcSlot(2, 3, 0f, 0.4f, 30f);
        TestUtil.Near(new Vector3(0f, 0f, 0.4f), middle);
        Assert.True(first.X > 0f && last.X < 0f);
        TestUtil.Near(-first.X, last.X);
        TestUtil.Near(0.4f, first.Length());
    }
}

public class LabelRevealTests
{
    const float Dt = 0.1f;

    [Fact]
    public void Change_ShowsForTheHoldTime()
    {
        var r = new LabelReveal { HoldSeconds = 1f };
        Assert.True(r.Update(true, 90f, Dt));
        for (int i = 0; i < 8; i++)
            Assert.True(r.Update(false, 90f, Dt));
        r.Update(false, 90f, Dt);
        r.Update(false, 90f, Dt);
        Assert.False(r.Update(false, 90f, Dt));
    }

    [Fact]
    public void Looking_ShowsWithHysteresis()
    {
        var r = new LabelReveal { EnterDegrees = 20f, ExitDegrees = 30f };
        Assert.False(r.Update(false, 25f, Dt));
        Assert.True(r.Update(false, 15f, Dt));
        Assert.True(r.Update(false, 25f, Dt));
        Assert.False(r.Update(false, 35f, Dt));
    }

    [Fact]
    public void Reset_HidesAtOnce()
    {
        var r = new LabelReveal();
        r.Update(true, 0f, Dt);
        r.Reset();
        Assert.False(r.Visible);
        Assert.False(r.Update(false, 90f, Dt));
    }
}

public class StatusAlertTests
{
    const float Dt = 0.05f;

    static float[] Statuses(float injury = 0f, float cold = 0f) => new[] { injury, 0f, cold, 0f };

    [Fact]
    public void FirstFrame_NeverAlerts()
    {
        var a = new StatusAlert();
        Assert.False(a.Update(0.05f, Statuses(0.5f, 0.5f), Dt));
    }

    [Fact]
    public void LowStamina_AlertsOnceUntilRearmed()
    {
        var a = new StatusAlert();
        a.Update(1f, Statuses(), Dt);
        Assert.True(a.Update(0.1f, Statuses(), Dt));
        Assert.False(a.Update(0.05f, Statuses(), 2f));
        Assert.False(a.Update(0.3f, Statuses(), 2f));
        Assert.False(a.Update(0.1f, Statuses(), 2f));
        a.Update(0.5f, Statuses(), 2f);
        Assert.True(a.Update(0.1f, Statuses(), 2f));
    }

    [Fact]
    public void NewAffliction_Alerts_GrowingOneDoesNot()
    {
        var a = new StatusAlert();
        a.Update(1f, Statuses(), Dt);
        Assert.True(a.Update(1f, Statuses(cold: 0.1f), 2f));
        Assert.False(a.Update(1f, Statuses(cold: 0.3f), 2f));
        Assert.True(a.Update(1f, Statuses(injury: 0.1f, cold: 0.3f), 2f));
    }

    [Fact]
    public void Cooldown_DelaysABurstInsteadOfDroppingIt()
    {
        var a = new StatusAlert { Cooldown = 1f };
        a.Update(1f, Statuses(), Dt);
        Assert.True(a.Update(1f, Statuses(cold: 0.1f), Dt));
        Assert.False(a.Update(1f, Statuses(injury: 0.1f, cold: 0.1f), Dt));
        Assert.False(a.Update(1f, Statuses(injury: 0.1f, cold: 0.1f), 0.5f));
        Assert.True(a.Update(1f, Statuses(injury: 0.1f, cold: 0.1f), 0.5f));
        Assert.False(a.Update(1f, Statuses(injury: 0.1f, cold: 0.1f), 2f));
    }

    [Fact]
    public void Present_HasHysteresis()
    {
        var a = new StatusAlert { Present = 0.02f, Absent = 0.005f };
        a.Update(1f, Statuses(), Dt);
        Assert.True(a.Update(1f, Statuses(cold: 0.03f), 2f));
        Assert.False(a.Update(1f, Statuses(cold: 0.015f), 2f));
        Assert.False(a.Update(1f, Statuses(cold: 0.03f), 2f));
        a.Update(1f, Statuses(cold: 0f), 2f);
        Assert.True(a.Update(1f, Statuses(cold: 0.03f), 2f));
    }

    [Fact]
    public void IgnoredStatus_NeverAlerts()
    {
        var a = new StatusAlert { Ignore = 2 };
        a.Update(1f, Statuses(), Dt);
        Assert.False(a.Update(1f, Statuses(cold: 0.5f), 2f));
        Assert.True(a.Update(1f, Statuses(injury: 0.5f, cold: 0.5f), 2f));
    }

    [Fact]
    public void Pulse_RisesAndFallsBackToZero()
    {
        var a = new StatusAlert { PulseSeconds = 0.5f };
        a.Update(1f, Statuses(), Dt);
        a.Update(1f, Statuses(cold: 0.1f), Dt);
        a.Update(1f, Statuses(cold: 0.1f), 0.2f);
        Assert.True(a.Pulse > 0.5f);
        a.Update(1f, Statuses(cold: 0.1f), 1f);
        Assert.Equal(0f, a.Pulse);
    }

    [Fact]
    public void Reset_ForgetsThePast()
    {
        var a = new StatusAlert();
        a.Update(1f, Statuses(), Dt);
        a.Reset();
        Assert.False(a.Update(0.05f, Statuses(cold: 0.5f), 2f));
        Assert.Equal(0f, a.Pulse);
    }
}

public class ControllerLabelTests
{
    [Fact]
    public void Offsets_MirrorBetweenHandsAndPointOutward()
    {
        foreach (var c in Enum.GetValues<VRControl>())
        {
            var right = ControllerLabels.Offset(c, left: false);
            var left = ControllerLabels.Offset(c, left: true);
            TestUtil.Near(new Vector3(-right.X, right.Y, right.Z), left);
        }
        Assert.True(ControllerLabels.Offset(VRControl.Trigger, false).X > 0f);
        Assert.True(ControllerLabels.Offset(VRControl.Trigger, false).Y < 0f);
        Assert.True(ControllerLabels.Offset(VRControl.Secondary, false).Y > 0f);
    }

    [Fact]
    public void StickArrows_SitForwardAndBack()
    {
        var up = ControllerLabels.Offset(VRControl.StickUp, false);
        var down = ControllerLabels.Offset(VRControl.StickDown, false);
        Assert.True(up.Z > down.Z);
    }

    [Fact]
    public void Prompts_FollowTheItemHandAndTheBindingTable()
    {
        Assert.True(
            ControllerLabels.ForPrompt(
                ItemPrompt.Main,
                true,
                false,
                "DominantSecondary",
                out var l,
                out var c
            )
        );
        Assert.True(l);
        Assert.Equal(VRControl.Trigger, c);

        ControllerLabels.ForPrompt(
            ItemPrompt.Secondary,
            true,
            false,
            "DominantSecondary",
            out l,
            out c
        );
        Assert.False(l);
        Assert.Equal(VRControl.Trigger, c);

        ControllerLabels.ForPrompt(ItemPrompt.Drop, true, false, "DominantSecondary", out l, out c);
        Assert.False(l);
        Assert.Equal(VRControl.Secondary, c);

        ControllerLabels.ForPrompt(
            ItemPrompt.Throw,
            false,
            false,
            "DominantSecondary",
            out l,
            out c
        );
        Assert.False(l);
        Assert.Equal(VRControl.Grip, c);

        // Scroll: the ScrollForward binding (dominant stick), shown beside the stick.
        ControllerLabels.ForPrompt(
            ItemPrompt.Scroll,
            false,
            true,
            "DominantSecondary",
            out l,
            out c
        );
        Assert.True(l);
        Assert.Equal(VRControl.StickClick, c);

        Assert.False(
            ControllerLabels.ForPrompt(ItemPrompt.Drop, false, false, "None", out _, out _)
        );
    }

    [Fact]
    public void Action_ResolvesFromTheDefaultTable()
    {
        Assert.True(
            ControllerLabels.ForAction(VRAction.SpectateNext, true, false, out var l, out var c)
        );
        Assert.True(l);
        Assert.Equal(VRControl.StickUp, c);
        Assert.True(
            ControllerLabels.ForAction(VRAction.SpectatePrevious, false, false, out l, out c)
        );
        Assert.False(l);
        Assert.Equal(VRControl.StickDown, c);
    }
}
