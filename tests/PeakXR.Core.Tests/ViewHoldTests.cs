using System;
using System.Numerics;

namespace PeakXR.Core.Tests;

public class ViewHoldTests
{
    const float Dt = 1f / 90f;
    static readonly Vector3 Base = new(10f, 2f, -5f);

    /// <summary>Run 43's idle sway on 3.0: about 0.07 m/s at 1 Hz, with a 0.037 m peak-to-peak vertical bob.</summary>
    static Vector3 Sway(float t)
    {
        float w = 2f * MathF.PI;
        float a = 0.07f / w;
        return Base
            + new Vector3(
                a * MathF.Sin(w * t),
                0.0185f * MathF.Sin(w * t + 1f),
                a * MathF.Cos(w * t)
            );
    }

    static float PeakToPeak(
        ViewHold hold,
        Func<float, Vector3> raw,
        bool still,
        float from,
        float to
    )
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (float t = 0f; t < to; t += Dt)
        {
            var o = hold.Update(raw(t), still, Dt);
            if (t < from)
                continue;
            min = Vector3.Min(min, o);
            max = Vector3.Max(max, o);
        }
        return (max - min).Length();
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.25f)]
    [InlineData(0.5f)]
    [InlineData(0.75f)]
    public void IdleSway_StandingStill_ViewMovesUnder5mm(float phase)
    {
        var hold = new ViewHold();
        // The whole standing window after the engage delay, whatever phase the hold starts in.
        float p2p = PeakToPeak(
            hold,
            t => Sway(t + phase),
            true,
            ViewHold.EngageSeconds + 0.05f,
            12f
        );
        Assert.True(p2p < 0.005f, $"view moved {p2p:0.0000} m");
    }

    [Fact]
    public void IdleSway_LongStand_StaysUnder5mm()
    {
        var hold = new ViewHold();
        PeakToPeak(hold, Sway, true, 0f, 60f);
        float p2p = PeakToPeak(hold, t => Sway(t + 60f), true, 0f, 10f);
        Assert.True(p2p < 0.005f, $"view moved {p2p:0.0000} m");
    }

    [Fact]
    public void Walk_PassesThroughUnchanged()
    {
        var hold = new ViewHold();
        for (float t = 0f; t < 3f; t += Dt)
        {
            var raw = Base + new Vector3(1.5f * t, 0.02f * MathF.Sin(9f * t), 0f);
            Assert.Equal(raw, hold.Update(raw, false, Dt));
        }
    }

    [Fact]
    public void Fall_PassesThroughUnchanged()
    {
        var hold = new ViewHold();
        for (float t = 0f; t < 1f; t += Dt)
        {
            var raw = Base - new Vector3(0f, 4.9f * t * t, 0f);
            Assert.Equal(raw, hold.Update(raw, false, Dt));
        }
    }

    [Fact]
    public void HoldReleases_SmoothlyOntoTheBody()
    {
        var hold = new ViewHold();
        for (float t = 0f; t < 5f; t += Dt)
            hold.Update(Sway(t), true, Dt);
        // The player starts walking: the view joins the body within half a second, without a jump.
        var last = hold.Update(Sway(5f), true, Dt);
        Vector3 raw = default;
        for (float t = 0f; t < 0.5f; t += Dt)
        {
            raw = Sway(5f) + new Vector3(1.5f * t, 0f, 0f);
            var o = hold.Update(raw, false, Dt);
            Assert.True((o - last).Length() < 1.5f * Dt + 0.01f, "the release jumped");
            last = o;
        }
        Assert.True(
            (last - raw).Length() < 0.002f,
            $"still {(last - raw).Length():0.0000} m off the body"
        );
    }

    [Fact]
    public void SlowCreepPastTheDeadband_IsFollowed()
    {
        var hold = new ViewHold();
        Vector3 raw = Base;
        Vector3 o = Base;
        // A body sliding 2 cm/s without crossing any gate: the view never trails it by more than the deadband.
        for (float t = 0f; t < 10f; t += Dt)
        {
            raw = Base + new Vector3(0.02f * t, 0f, 0f);
            o = hold.Update(raw, true, Dt);
            Assert.True((raw - o).Length() <= ViewHold.Deadband + 1e-4f);
        }
        Assert.True(o.X > Base.X + 0.1f);
    }

    [Theory]
    [InlineData(30f)]
    [InlineData(-30f)]
    [InlineData(90f)]
    public void SnapTurnWithAHeldOffset_PivotsOnTheEye(float snap)
    {
        // rig.camera's first-person view: the eye anchor ahead of the torso along the rig heading, the turn shift
        // that cancels its swing, and the hold on the anchor (run 45, vr.16: the eye slid 3.2 cm in a 30 degree snap).
        var eyes = new Vector2(0.19f, 1.0f);
        var hold = new ViewHold();
        var shift = Vector3.Zero;
        Vector3 view = default;
        float t = 0f;
        for (; t < 3f; t += Dt)
            view = hold.Update(RigMath.EyeAnchor(Sway(t), 0f, eyes), true, Dt) + shift;
        Assert.True(hold.Holding && hold.Offset > 0.005f, "no held offset to carry");

        shift = RigMath.TurnShift(shift, 0f, snap, eyes.X);
        var body = RigMath.EyeAnchor(Sway(t), snap, eyes);
        hold.Rebase(body);
        var after = hold.Update(body, true, Dt) + shift;
        float slide = new Vector2(after.X - view.X, after.Z - view.Z).Length();
        Assert.True(slide < 0.005f, $"eye slid {slide:0.0000} m");
    }

    [Fact]
    public void Teleport_Snaps()
    {
        var hold = new ViewHold();
        for (float t = 0f; t < 2f; t += Dt)
            hold.Update(Base, true, Dt);
        var far = Base + new Vector3(30f, 5f, 0f);
        Assert.Equal(far, hold.Update(far, true, Dt));
    }

    static ViewHoldInputs Calm() =>
        new()
        {
            BodyFree = true,
            LeanDegrees = 0f,
            Speed = 0.07f,
        };

    [Fact]
    public void Gate_StandingStill_Holds() => Assert.Null(ViewHoldGate.Why(Calm()));

    [Fact]
    public void Gate_ClosesOnMotion()
    {
        var i = Calm();
        i.BodyFree = false;
        Assert.NotNull(ViewHoldGate.Why(i));
        i = Calm();
        i.LeanDegrees = 5f;
        Assert.NotNull(ViewHoldGate.Why(i));
        i = Calm();
        i.Speed = 1f;
        Assert.NotNull(ViewHoldGate.Why(i));
        i = Calm();
        i.Jumping = true;
        Assert.NotNull(ViewHoldGate.Why(i));
        i = Calm();
        i.RoomWalking = true;
        Assert.NotNull(ViewHoldGate.Why(i));
    }
}
