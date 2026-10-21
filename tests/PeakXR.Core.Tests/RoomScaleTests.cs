using System.Collections.Generic;
using System.Linq;

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
        // The walk ramps from the stop radius (half the lean radius), so it keeps going until the head is well inside.
        var f = new RoomScaleFollow { LeanRadius = 0.2f, RampDistance = 0.3f };
        var move = f.Update(new Vector2(0f, 0.35f), Vector2.Zero, canFollow: true);
        TestUtil.Near(0f, move.X);
        TestUtil.Near(0.25f / 0.3f, move.Y);
        move = f.Update(new Vector2(0f, 2f), Vector2.Zero, canFollow: true);
        TestUtil.Near(1f, move.Y);
    }

    /// <summary>
    /// Runs the follow at 90 Hz against a body that walks after its request like the game's driven ragdoll (speed
    /// eases toward input times 1.5 m/s with a 0.12 s time constant, CharacterMovement drag 0.85 per tick). Returns
    /// the body's forward travel per frame.
    /// </summary>
    static List<float> Walk(RoomScaleFollow f, Func<float, Vector2> head, float seconds)
    {
        const float dt = 1f / 90f;
        var travel = new List<float>();
        float body = 0f;
        var v = Vector2.Zero;
        var delta = Vector2.Zero;
        for (float t = 0f; t < seconds; t += dt)
        {
            var request = f.Update(head(t), delta, canFollow: true);
            v += (request * 1.5f - v) * (1f - MathF.Exp(-dt / 0.12f));
            delta = v * dt;
            body += delta.Y;
            travel.Add(body);
        }
        return travel;
    }

    static Func<float, Vector2> Lean(float from, float to, float start, float seconds) =>
        t => new Vector2(0f, from + (to - from) * Math.Clamp((t - start) / seconds, 0f, 1f));

    [Fact]
    public void NaturalLean_DoesNotWalkTheBody()
    {
        // Leaning 0.3 m over a table and back up is upper-body work: the avatar bends, the character stays put.
        var f = new RoomScaleFollow();
        var forward = Walk(f, Lean(0f, 0.3f, 0.2f, 0.5f), 2f);
        var back = Walk(f, Lean(0.3f, 0f, 0f, 0.5f), 2f);
        Assert.True(
            forward.Max(MathF.Abs) < 0.005f,
            $"walked {forward.Max(MathF.Abs):0.000} m leaning"
        );
        Assert.True(
            back.Max(MathF.Abs) < 0.005f,
            $"walked {back.Max(MathF.Abs):0.000} m standing up"
        );
    }

    [Fact]
    public void LeanPastTheLimit_WalksOnceAndNotBackWhenStandingUp()
    {
        var f = new RoomScaleFollow();
        var lean = Walk(f, Lean(0f, 0.45f, 0f, 0.5f), 3f);
        Assert.True(lean[^1] > 0.05f, $"walked only {lean[^1]:0.000} m after a 0.45 m lean");
        var up = Walk(f, Lean(0.45f, 0f, 0f, 0.5f), 3f);
        Assert.True(up.Min() > -0.005f, $"walked back {-up.Min():0.000} m standing up");
    }

    [Fact]
    public void AfterAStep_HeadSwayDoesNotRockTheBody()
    {
        // The follow must stop well inside the lean radius: stopping on its edge let every breath of sway start
        // another shuffle, and the character rocked forward and back while the player stood still.
        var f = new RoomScaleFollow();
        Walk(f, Lean(0f, 0.7f, 0f, 0.4f), 4f);
        var sway = Walk(f, t => new Vector2(0f, 0.7f + 0.03f * MathF.Sin(MathF.PI * t)), 6f);
        float peak = sway.Max() - sway.Min();
        Assert.True(peak < 0.005f, $"body rocked {peak:0.000} m peak to peak");
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
    public void GaitSway_DoesNotRatchetTheAnchor()
    {
        // Each step swings the body forward and back; only its net travel may reach the anchor.
        var f = new RoomScaleFollow();
        var head = new Vector2(0f, 0.5f);
        for (int i = 0; i < 20; i++)
        {
            f.Update(head, new Vector2(0f, 0.03f), true);
            f.Update(head, new Vector2(0f, -0.03f), true);
        }
        TestUtil.Near(0f, f.Anchor.Y);
    }

    [Fact]
    public void BackSwing_NeverPullsTheAnchorBehindItsStart()
    {
        var f = new RoomScaleFollow();
        var head = new Vector2(0f, 0.5f);
        f.Update(head, new Vector2(0f, 0.1f), true);
        f.Update(head, new Vector2(0f, -0.3f), true);
        TestUtil.Near(0f, f.Anchor.Y);
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
