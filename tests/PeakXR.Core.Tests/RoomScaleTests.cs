namespace PeakXR.Core.Tests;

public class RoomScaleTests
{
    [Fact]
    public void RotateYaw_MatchesUnityConvention()
    {
        // Unity: +90 deg yaw turns forward (+Z) into right (+X).
        var v = YawMath.Rotate(new Vector2(0f, 1f), 90f);
        TestUtil.Near(1f, v.X);
        TestUtil.Near(0f, v.Y);
        TestUtil.Near(90f, YawMath.Yaw(new Vector2(1f, 0f)));
    }

    [Fact]
    public void WorldToInput_FacingEastMakesEastForward()
    {
        var input = YawMath.Rotate(new Vector2(1f, 0f), -90f);
        TestUtil.Near(0f, input.X);
        TestUtil.Near(1f, input.Y);
    }

    [Fact]
    public void LeaningInsideRadius_RequestsNothing()
    {
        var f = new RoomScaleFollow { LeanRadius = 0.2f };
        var move = f.Update(new Vector2(0.15f, 0f), Vector2.Zero, canFollow: true);
        Assert.Equal(Vector2.Zero, move);
    }

    [Fact]
    public void WalkingAway_RequestsMoveTowardHead()
    {
        var f = new RoomScaleFollow { LeanRadius = 0.2f, RampDistance = 0.3f };
        var move = f.Update(new Vector2(0f, 0.35f), Vector2.Zero, canFollow: true);
        TestUtil.Near(0f, move.X);
        TestUtil.Near(0.5f, move.Y);
        move = f.Update(new Vector2(0f, 2f), Vector2.Zero, canFollow: true);
        TestUtil.Near(1f, move.Y);
    }

    [Fact]
    public void BodyCatchingUp_ShiftsAnchorButNeverPastTheHead()
    {
        var f = new RoomScaleFollow();
        var head = new Vector2(0f, 0.5f);
        f.Update(head, Vector2.Zero, true);
        f.Update(head, new Vector2(0.05f, 0.3f), true);
        TestUtil.Near(0f, f.Anchor.X);
        TestUtil.Near(0.3f, f.Anchor.Y);
        f.Update(head, new Vector2(0f, 1f), true);
        TestUtil.Near(0.5f, f.Anchor.Y);
    }

    [Fact]
    public void BodyPushedAway_KeepsAnchor()
    {
        var f = new RoomScaleFollow();
        f.Update(new Vector2(0f, 0.5f), new Vector2(0f, -0.2f), true);
        Assert.Equal(Vector2.Zero, f.Anchor);
    }

    [Fact]
    public void CannotFollow_NoRequestNoAbsorb()
    {
        var f = new RoomScaleFollow();
        var move = f.Update(new Vector2(0f, 0.5f), new Vector2(0f, 0.3f), canFollow: false);
        Assert.Equal(Vector2.Zero, move);
        Assert.Equal(Vector2.Zero, f.Anchor);
    }

    [Fact]
    public void FarOffset_NeedsRecenter()
    {
        var f = new RoomScaleFollow { RecenterDistance = 1f };
        f.Update(new Vector2(0f, 0.9f), Vector2.Zero, false);
        Assert.False(f.RecenterNeeded);
        f.Update(new Vector2(0f, 1.1f), Vector2.Zero, false);
        Assert.True(f.RecenterNeeded);
        f.Recenter(new Vector2(0f, 1.1f));
        Assert.Equal(new Vector2(0f, 1.1f), f.Anchor);
        Assert.False(f.RecenterNeeded);
    }

    [Theory]
    [InlineData(30f)]
    [InlineData(-45f)]
    [InlineData(90f)]
    public void AnchorAfterYaw_KeepsHeadWorldPosition(float degrees)
    {
        // World = body + R * (p - anchor): the camera feature keeps the anchor over the body.
        float rigYaw = 20f;
        var head = new Vector2(0.4f, -0.3f);
        var anchor = new Vector2(-0.1f, 0.2f);
        var before = YawMath.Rotate(head - anchor, rigYaw);

        var newAnchor = RoomScaleFollow.AnchorAfterYaw(anchor, head, degrees);
        var after = YawMath.Rotate(head - newAnchor, rigYaw + degrees);
        TestUtil.Near(before.X, after.X);
        TestUtil.Near(before.Y, after.Y);
    }

    [Fact]
    public void RecenterFade_SnapsOnlyAfterTheFadeOut()
    {
        var f = new RecenterFade { FadeTime = 0.2f };
        Assert.False(f.Update(false, 0.1f));
        Assert.Equal(0f, f.Target);

        Assert.False(f.Update(true, 0.1f));
        Assert.Equal(1f, f.Target);
        Assert.False(f.Update(true, 0.1f));
        Assert.True(f.Update(true, 0.11f));
        Assert.Equal(0f, f.Target);
    }

    [Fact]
    public void RecenterFade_KeepsGoingWhenNoLongerNeeded()
    {
        // Once the screen is going dark the snap happens, so the fade never ends without a recenter.
        var f = new RecenterFade { FadeTime = 0.2f };
        f.Update(true, 0.01f);
        Assert.False(f.Update(false, 0.1f));
        Assert.True(f.Update(false, 0.15f));
        Assert.False(f.Update(false, 0.1f));
        Assert.Equal(0f, f.Target);
    }

    [Fact]
    public void RecenterFade_ResetClearsAPendingFade()
    {
        var f = new RecenterFade();
        f.Update(true, 0.01f);
        f.Reset();
        Assert.Equal(0f, f.Target);
        Assert.False(f.Update(false, 1f));
    }
}
