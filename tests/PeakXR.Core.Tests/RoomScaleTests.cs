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
    public void Absorb_AWalkAndItsCoast_NeverMoveTheView()
    {
        // While the stance walk walks and settles, the anchor takes all of the eye anchor's travel: its gait sway
        // (run 35), its coast past the head and back (run 36). The view's world position moves by the anchor's
        // travel less the room anchor's, which must stay zero.
        var f = new RoomScaleFollow();
        const float dt = 1f / 90f;
        var view = Vector2.Zero;
        var last = Vector2.Zero;
        float max = 0f;
        for (float t = 0f; t < 2f; t += dt)
        {
            float along =
                t < 0.6f
                    ? 0.48f * MathF.Sin(t / 0.6f * MathF.PI / 2f)
                    : 0.48f - 0.08f * MathF.Min(1f, (t - 0.6f) / 0.6f);
            var pos = new Vector2(0.04f * MathF.Sin(t * 9f), along);
            var delta = pos - last;
            last = pos;
            var before = f.Anchor;
            f.Absorb(delta);
            view += delta - (f.Anchor - before);
            max = MathF.Max(max, view.Length());
        }
        Assert.True(max < 1e-4f, $"view moved {max:0.000} m");
    }

    [Fact]
    public void Absorb_LeavesAJumpToTheView()
    {
        // A warp or a knock moves the body farther in one frame than a walk can: the view goes with it.
        var f = new RoomScaleFollow();
        f.Absorb(new Vector2(0f, f.MaxFollowStep + 0.05f));
        Assert.Equal(Vector2.Zero, f.Anchor);
    }

    [Fact]
    public void Recenter_PutsTheAnchorOnTheHead()
    {
        var f = new RoomScaleFollow { Anchor = new Vector2(0.3f, -0.2f) };
        f.Recenter(new Vector2(1f, 2f));
        Assert.Equal(new Vector2(1f, 2f), f.Anchor);
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
