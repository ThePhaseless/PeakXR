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
    static List<float> Walk(
        RoomScaleFollow f,
        Func<float, Vector2> head,
        float seconds,
        float speed = 1.5f,
        float tau = 0.12f
    )
    {
        const float dt = 1f / 90f;
        var travel = new List<float>();
        float body = 0f;
        var v = Vector2.Zero;
        var delta = Vector2.Zero;
        for (float t = 0f; t < seconds; t += dt)
        {
            var request = f.Update(head(t), delta, canFollow: true, dt);
            v += (request * speed - v) * (1f - MathF.Exp(-dt / tau));
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
    public void LeanPastTheLimit_WalksOnceAndNeverBackAfterStandingUp()
    {
        var f = new RoomScaleFollow();
        var lean = Walk(f, Lean(0f, 0.45f, 0f, 0.5f), 3f);
        Assert.True(lean[^1] > 0.05f, $"walked only {lean[^1]:0.000} m after a 0.45 m lean");
        // Standing up leaves the body ahead of the head, inside the limit. It stays put: a walk back after standing
        // up is the forward-and-back motion players reported.
        var up = Walk(f, Lean(0.45f, 0f, 0f, 0.5f), 5f);
        Assert.True(
            up.Max() - up.Min() < 0.005f,
            $"body moved {up.Max() - up.Min():0.000} m standing up"
        );
        // Instead the avatar's own lean eases to neutral: the stance it stood back up into counts as upright.
        TestUtil.Near(f.Offset.Y, f.Neutral.Y, 0.01f);
        Assert.True(f.Offset.Y < -0.2f, $"offset {f.Offset.Y:0.000} m");
    }

    [Fact]
    public void AbsorbInTheCameraFrame_SameAsInUpdate()
    {
        // The camera absorbs the body's travel the frame it moves the eye; the next Update then sees no delta.
        var a = new RoomScaleFollow();
        var b = new RoomScaleFollow();
        var head = new Vector2(0f, 0.5f);
        a.Update(head, Vector2.Zero, true, 0.01f);
        b.Update(head, Vector2.Zero, true, 0.01f);
        var step = new Vector2(0.01f, 0.03f);
        var ra = a.Update(head, step, true, 0.01f);
        b.Absorb(head, step);
        var rb = b.Update(head, Vector2.Zero, true, 0.01f);
        TestUtil.Near(a.Anchor.X, b.Anchor.X, 1e-6f);
        TestUtil.Near(a.Anchor.Y, b.Anchor.Y, 1e-6f);
        TestUtil.Near(ra.Y, rb.Y, 1e-6f);
    }

    [Fact]
    public void FollowThatCoastsOntoTheHead_StillNeverWalksBack()
    {
        // Run 31: the far lean's walk coasted until the anchor reached the head (offset 0.007 m), so standing back
        // up left the head 0.43 m behind, past the lean radius, and the body walked 0.44 m back.
        var f = new RoomScaleFollow();
        Walk(f, Lean(0f, 0.5f, 0f, 0.5f), 3f, speed: 3f, tau: 0.5f);
        Assert.True(
            f.Offset.Y < 0.1f,
            $"the walk did not coast onto the head: offset {f.Offset.Y:0.000}"
        );
        var up = Walk(f, Lean(0.5f, 0f, 0f, 0.5f), 5f, speed: 3f, tau: 0.5f);
        Assert.True(
            up.Max() - up.Min() < 0.005f,
            $"body walked {up.Max() - up.Min():0.000} m back after standing up"
        );
    }

    [Fact]
    public void AfterAFollow_AStepFarBehindIt_StillFollows()
    {
        // Only the stance the follow walked away from counts as a lean; stepping well behind it walks the body back.
        var f = new RoomScaleFollow();
        Walk(f, Lean(0f, 0.5f, 0f, 0.5f), 3f);
        float before = f.Anchor.Y;
        var back = Walk(f, Lean(0.5f, -1.0f, 0f, 1f), 4f);
        Assert.True(back[^1] < -0.3f, $"walked only {back[^1]:0.000} m after stepping 1.5 m back");
        Assert.True(f.Anchor.Y < before);
    }

    [Fact]
    public void Neutral_EasesNeverJumps_AndOnlyAgainstTheFollow()
    {
        var f = new RoomScaleFollow();
        Walk(f, Lean(0f, 0.45f, 0f, 0.5f), 3f);
        float last = 0f;
        float steepest = 0f;
        for (int i = 0; i < 450; i++)
        {
            float t = i / 90f;
            f.Update(Lean(0.45f, 0f, 0f, 0.5f)(t), Vector2.Zero, true, 1f / 90f);
            steepest = MathF.Max(steepest, MathF.Abs(f.Neutral.Y - last) * 90f);
            last = f.Neutral.Y;
        }
        Assert.True(steepest < 1f, $"neutral moved {steepest:0.00} m/s");

        // Back over the body: neutral returns to zero. A lean the follow's way stays a lean.
        float over = f.Anchor.Y;
        Walk(f, Lean(0f, over + 0.3f, 0f, 0.5f), 4f);
        Assert.True(MathF.Abs(f.Neutral.Y) < 0.01f, $"neutral {f.Neutral.Y:0.000} m");
        Assert.True(f.Offset.Y > 0.25f);
    }

    [Fact]
    public void HeldLeanInsideTheLimit_StaysALean()
    {
        var f = new RoomScaleFollow();
        var held = Walk(f, Lean(0f, -0.3f, 0f, 0.5f), 6f);
        Assert.True(held.Max(MathF.Abs) < 0.005f, $"walked {held.Max(MathF.Abs):0.000} m");
        Assert.Equal(Vector2.Zero, f.Neutral);
        Walk(f, Lean(0f, 0.7f, 0f, 0.4f), 4f);
        var ahead = Walk(f, _ => new Vector2(0f, 0.7f), 4f);
        Assert.True(ahead.Max() - ahead.Min() < 0.005f, "moved after a step ahead");
        Assert.True(f.Neutral.Length() < 1e-4f, "a stance ahead, the follow's way, became neutral");
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
