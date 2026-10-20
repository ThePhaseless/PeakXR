namespace PeakXR.Core.Tests;

public class LocomotionTests
{
    [Fact]
    public void HandRelative_PointingRightMakesForwardGoRight()
    {
        var v = MoveInput.HandRelative(new Vector2(0f, 1f), headYaw: 0f, handYaw: 90f);
        TestUtil.Near(1f, v.X);
        TestUtil.Near(0f, v.Y);
    }

    [Fact]
    public void HandRelative_HandAlignedWithHeadChangesNothing()
    {
        var v = MoveInput.HandRelative(new Vector2(0.3f, 0.6f), headYaw: 170f, handYaw: -190f);
        TestUtil.Near(0.3f, v.X);
        TestUtil.Near(0.6f, v.Y);
    }

    [Fact]
    public void TrackingToStick_HeadFacingEastMakesEastForward()
    {
        var v = MoveInput.TrackingToStick(new Vector2(1f, 0f), headYaw: 90f);
        TestUtil.Near(0f, v.X);
        TestUtil.Near(1f, v.Y);
    }

    [Fact]
    public void Yaw_FromForwardVector()
    {
        TestUtil.Near(90f, MoveInput.Yaw(new Vector3(1f, 0.5f, 0f)));
        TestUtil.Near(0f, MoveInput.Yaw(new Vector3(0f, -1f, 0f)));
    }

    [Fact]
    public void Combine_ClampsToUnitLength()
    {
        var v = MoveInput.Combine(new Vector2(0f, 1f), new Vector2(1f, 0f));
        TestUtil.Near(1f, v.Length());
    }

    [Fact]
    public void TurnInput_SnapFiresOnceAndHasNoSmoothRate()
    {
        var t = new TurnInput { Mode = TurnMode.Snap };
        t.Snap.Angle = 30f;
        Assert.Equal(30f, t.Update(new Vector2(0.9f, 0f), 0.01f));
        Assert.Equal(0f, t.Update(new Vector2(0.9f, 0f), 0.01f));
        Assert.Equal(0f, t.SmoothRate);
    }

    [Fact]
    public void TurnInput_SmoothReportsRate()
    {
        var t = new TurnInput { Mode = TurnMode.Smooth, SmoothSpeed = 100f };
        float d = t.Update(new Vector2(-1f, 0f), 0.1f);
        TestUtil.Near(-10f, d);
        TestUtil.Near(-100f, t.SmoothRate);
    }

    [Fact]
    public void TurnInput_IgnoresUpwardFlicks()
    {
        var t = new TurnInput { Mode = TurnMode.Snap };
        Assert.Equal(0f, t.Update(new Vector2(0.75f, 0.9f), 0.01f));
        var s = new TurnInput { Mode = TurnMode.Smooth };
        Assert.Equal(0f, s.Update(new Vector2(0.5f, -0.9f), 0.01f));
    }

    [Fact]
    public void TurnInput_SwitchingToSmoothRearmsSnap()
    {
        var t = new TurnInput { Mode = TurnMode.Snap };
        t.Update(new Vector2(1f, 0f), 0.01f);
        t.Mode = TurnMode.Smooth;
        t.Update(new Vector2(1f, 0f), 0.01f);
        t.Mode = TurnMode.Snap;
        Assert.Equal(30f, t.Update(new Vector2(1f, 0f), 0.01f));
    }
}
