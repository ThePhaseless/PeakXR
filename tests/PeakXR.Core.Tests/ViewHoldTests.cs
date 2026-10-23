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
            LeanSpeed = 0.01f,
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
        i.LeanSpeed = 0.5f;
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

    [Fact]
    public void Gate_SteadyHeldLean_Holds()
    {
        // A lean held still bends the body but moves nothing: the bend alone no longer closes the gate.
        var i = Calm();
        i.LeanSpeed = 0.02f;
        Assert.Null(ViewHoldGate.Why(i));
    }

    /// <summary>Eye shift (m) per degree of bend: run 43's held leans, 0.152 m at 15.7 degrees.</summary>
    const float ShiftPerDegree = 0.0097f;

    /// <summary>The eye's lean shift (m): a 0.15 m lean (about 16 degrees) eased in over 0.25 s at
    /// <paramref name="on"/>, held, eased out over 0.25 s at <paramref name="off"/>. It is computed on the 50 Hz
    /// animation tick, with run 43's half-millimetre jitter and a slow 2 mm sway while leaning.</summary>
    static Vector3 LeanShift(float t, float on, float off)
    {
        static float Ease(float s) =>
            s <= 0f ? 0f
            : s >= 1f ? 1f
            : s * s * (3f - 2f * s);
        float tick = MathF.Floor(t * 50f) / 50f;
        float amount = Ease((tick - on) / 0.25f) - Ease((tick - off) / 0.25f);
        return amount
            * new Vector3(
                0.15f + 0.0005f * MathF.Sin(tick * 377f),
                0.0005f * MathF.Sin(tick * 53f),
                0.002f * MathF.Sin(2f * MathF.PI * tick)
            );
    }

    /// <summary>rig.camera's chain at 90 Hz: the eye's lean shift through <see cref="LeanMotion"/> into the gate,
    /// the swaying body through the hold. The view, the gate's verdict and the shift, per frame.</summary>
    static (Vector3 view, string why, Vector3 shift)[] RunLean(float on, float off, float end)
    {
        var hold = new ViewHold();
        var motion = new LeanMotion();
        var frames = new (Vector3, string, Vector3)[(int)(end / Dt)];
        for (int f = 0; f < frames.Length; f++)
        {
            float t = f * Dt;
            var shift = LeanShift(t, on, off);
            var i = Calm();
            i.LeanSpeed = motion.Update(shift, Dt);
            string why = ViewHoldGate.Why(i);
            frames[f] = (hold.Update(Sway(t), why == null, Dt), why, shift);
        }
        return frames;
    }

    [Fact]
    public void SteadyHeldLean_ViewHeldStill()
    {
        // Run 43: "leans: view moved 0.038 m" while the head held each lean still on 3.0's idle bob.
        var frames = RunLean(1f, 6f, 7f);
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (int f = (int)(1.9f / Dt); f < (int)(6f / Dt); f++)
        {
            Assert.Null(frames[f].why);
            min = Vector3.Min(min, frames[f].view);
            max = Vector3.Max(max, frames[f].view);
        }
        float p2p = (max - min).Length();
        Assert.True(p2p < 0.005f, $"view moved {p2p:0.0000} m during the held lean");
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(6f)]
    public void LeanOnsetAndOffset_ReleaseNoLaterThanTheBendGate(float start)
    {
        // The bend gate closed at 1 degree: a moving lean must release the view at least as early, and keep it
        // released while the lean moves, so the view is on the body as the lean carries it.
        var frames = RunLean(1f, 6f, 7f);
        int from = (int)(start / Dt);
        var rest = frames[from].shift;
        int closed = -1;
        int bendGate = -1;
        for (int f = from; f < frames.Length && (closed < 0 || bendGate < 0); f++)
        {
            if (closed < 0 && frames[f].why != null)
                closed = f;
            if (bendGate < 0 && (frames[f].shift - rest).Length() >= ShiftPerDegree)
                bendGate = f;
        }
        Assert.True(closed >= 0, "the moving lean never released the view");
        Assert.True(
            closed <= bendGate,
            $"released {(closed - bendGate) * Dt:0.000} s after the bend gate would have"
        );
        for (int f = closed; f < (int)((start + 0.25f) / Dt); f++)
            Assert.Equal("lean", frames[f].why);
    }

    [Fact]
    public void WalkFromAHeldLean_Releases()
    {
        // Pushing the stick or the body speeding up closes the gate however still the lean is.
        var i = Calm();
        i.LeanSpeed = 0.01f;
        i.BodyFree = false;
        Assert.Equal("body", ViewHoldGate.Why(i));
        i = Calm();
        i.LeanSpeed = 0.01f;
        i.Speed = 1.5f;
        Assert.Equal("moving", ViewHoldGate.Why(i));
    }

    [Fact]
    public void LeanMotion_SteadyLeanJitter_StaysUnderTheGate()
    {
        var motion = new LeanMotion();
        for (float t = 0f; t < 10f; t += Dt)
        {
            float speed = motion.Update(LeanShift(t, -1f, 100f), Dt);
            Assert.True(speed < ViewHoldGate.MaxLeanSpeed, $"{speed:0.000} m/s at {t:0.00} s");
        }
    }

    [Fact]
    public void LeanMotion_PausedFrame_KeepsItsSpeed()
    {
        var motion = new LeanMotion();
        motion.Update(Vector3.Zero, Dt);
        float moving = motion.Update(new Vector3(0.01f, 0f, 0f), Dt);
        Assert.Equal(moving, motion.Update(new Vector3(0.02f, 0f, 0f), 0f));
    }
}
