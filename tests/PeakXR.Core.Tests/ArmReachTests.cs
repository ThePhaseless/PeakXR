namespace PeakXR.Core.Tests;

public class ArmReachTests
{
    // The scout: upper arm 0.394 m, forearm 0.211 m (vr.45 run 28).
    const float Avatar = 0.605f;
    static float Bent => Avatar * ArmReach.BentReach;

    [Fact]
    public void WithinTheKnee_HandStaysOnTheController()
    {
        foreach (float player in new[] { 0f, 0.7f, 0.9f, 2f })
        foreach (float d in new[] { 0f, 0.1f, 0.2f, 0.3f })
            TestUtil.Near(d, ArmReach.Map(d, Avatar, player));
    }

    [Fact]
    public void NeverPastTheBentReach()
    {
        foreach (float player in new[] { 0f, 0.7f, 0.86f, 1.2f, 5f })
            for (float d = 0f; d < 2f; d += 0.01f)
                Assert.True(
                    ArmReach.Map(d, Avatar, player) <= Bent + 1e-5f,
                    $"d {d} player {player}"
                );
    }

    [Fact]
    public void PlayersFullReach_IsTheAvatarsFullReach()
    {
        // Run 28: the leaned shoulders sat 0.86 m from the controllers.
        TestUtil.Near(Bent, ArmReach.Map(0.86f, Avatar, 0.86f), 1e-4f);
        TestUtil.Near(Bent, ArmReach.Map(1.5f, Avatar, 0.86f), 1e-4f);
    }

    [Fact]
    public void Monotone_AndSmooth()
    {
        foreach (float player in new[] { 0f, 0.65f, 0.8f, 0.86f, 1.0f, 1.15f, 3f })
        {
            float last = 0f;
            float lastSlope = 1f;
            const float step = 0.001f;
            for (float d = step; d < 1.6f; d += step)
            {
                float m = ArmReach.Map(d, Avatar, player);
                float slope = (m - last) / step;
                Assert.True(slope >= -1e-3f, $"falls at d {d} player {player}");
                Assert.True(
                    slope <= 1.001f,
                    $"steeper than the controller at d {d} player {player}"
                );
                // C1: no visible jerk where the compression starts or ends.
                Assert.True(MathF.Abs(slope - lastSlope) < 0.05f, $"kink at d {d} player {player}");
                last = m;
                lastSlope = slope;
            }
        }
    }

    [Fact]
    public void Target_LiesOnTheShoulderControllerLine()
    {
        var shoulder = new Vector3(0.2f, 1.1f, 0f);
        var controller = shoulder + new Vector3(0.1f, -0.2f, 0.8f);
        var t = ArmReach.Target(shoulder, controller, Avatar, 0.9f);
        float d = Vector3.Distance(shoulder, controller);
        TestUtil.Near(ArmReach.Map(d, Avatar, 0.9f), Vector3.Distance(shoulder, t));
        TestUtil.Near(
            Vector3.Normalize(controller - shoulder),
            Vector3.Normalize(t - shoulder),
            1e-4f
        );
    }

    [Fact]
    public void PlayerReach_IsWhereTheMapReachesTheBentArm()
    {
        foreach (float player in new[] { 0f, 0.72f, 0.86f, 5f })
        {
            float p = ArmReach.PlayerReach(Avatar, player);
            TestUtil.Near(Bent, ArmReach.Map(p, Avatar, player), 1e-4f);
            Assert.True(ArmReach.Map(p - 0.02f, Avatar, player) < Bent, $"player {player}");
        }
        TestUtil.Near(Bent * ArmReach.MinPlayer, ArmReach.PlayerReach(Avatar, 0f));
        TestUtil.Near(0.72f, ArmReach.PlayerReach(Avatar, 0.72f));
        TestUtil.Near(Bent * ArmReach.MaxPlayer, ArmReach.PlayerReach(Avatar, 5f));
    }

    [Fact]
    public void GhostReach_MappedHandReachesThePlayersReach_HeldHandTheArm()
    {
        // Run 17 (rig x1.095): a resting empty hand 0.72 m from the shoulder, past the bare arm, within the player's.
        TestUtil.Near(0.72f, ArmReach.GhostReach(Avatar, 0.72f, 1f));
        TestUtil.Near(Avatar, ArmReach.GhostReach(Avatar, 0.72f, 0f));
        TestUtil.Near((Avatar + 0.72f) * 0.5f, ArmReach.GhostReach(Avatar, 0.72f, 0.5f));
        TestUtil.Near(Avatar, ArmReach.GhostReach(Avatar, 0.72f, float.NaN));
    }

    [Fact]
    public void Target_CloseHandUnchanged()
    {
        var shoulder = new Vector3(0.2f, 1.1f, 0f);
        var controller = shoulder + new Vector3(0f, -0.1f, 0.3f);
        TestUtil.Near(controller, ArmReach.Target(shoulder, controller, Avatar, 0.9f));
    }

    const float Tick = 1f / 50f;

    static void Hold(ReachLearner l, float d, float seconds, float rise = 0f, bool valid = true)
    {
        for (float t = 0f; t < seconds; t += Tick)
            l.Sample(d, rise, Avatar, valid, Tick);
    }

    [Fact]
    public void Learner_KeepsTheLongestHeldReach()
    {
        var l = new ReachLearner();
        Hold(l, 0.7f, 0.5f);
        Hold(l, 0.5f, 0.5f);
        TestUtil.Near(0.7f, l.Reach);
        Hold(l, 0.86f, 0.5f);
        TestUtil.Near(0.86f, l.Reach);
    }

    [Fact]
    public void Learner_Rescale_KeepsTheReachInNewUnits()
    {
        var l = new ReachLearner();
        Hold(l, 0.7f, 0.5f);
        l.Rescale(1.1f);
        TestUtil.Near(0.77f, l.Reach);
        l.Rescale(0f);
        Assert.Equal(0f, l.Reach);
    }

    [Fact]
    public void Learner_IgnoresAOneFrameSpike()
    {
        // A warp, respawn or tracking pop: one frame under the cap, at rest (hip velocity 0).
        var l = new ReachLearner();
        Hold(l, 0.7f, 0.5f);
        l.Sample(1.05f, 0f, Avatar, true, Tick);
        Hold(l, 0.7f, 0.5f);
        TestUtil.Near(0.7f, l.Reach);
    }

    [Fact]
    public void Learner_SkipsInvalidSpans()
    {
        // The eye guard armed after a warp or takeover: a long held distance does not count.
        var l = new ReachLearner();
        Hold(l, 1.0f, 2f, valid: false);
        Assert.Equal(0f, l.Reach);
        // Nor does a run that an invalid frame interrupts before it is held.
        Hold(l, 0.9f, 0.15f);
        l.Sample(0.9f, 0f, Avatar, false, Tick);
        Hold(l, 0.9f, 0.15f);
        Assert.Equal(0f, l.Reach);
    }

    [Fact]
    public void Learner_RejectsNonFinite()
    {
        var l = new ReachLearner();
        Hold(l, 0.7f, 0.5f);
        Hold(l, float.NaN, 0.5f);
        Hold(l, float.PositiveInfinity, 0.5f);
        Hold(l, 0.8f, 0.5f, rise: float.NaN);
        for (int i = 0; i < 30; i++)
            l.Sample(0.8f, 0f, Avatar, true, float.NaN);
        TestUtil.Near(0.7f, l.Reach);
        Assert.True(float.IsFinite(ArmReach.Map(0.8f, Avatar, float.NaN)));
    }

    [Fact]
    public void Learner_IgnoresOverheadReach()
    {
        // The scout's shoulder sits 0.47 m under the eyes: a hand overhead is ~1 m from it.
        var l = new ReachLearner();
        Hold(l, 0.95f, 1f, rise: 0.8f);
        Assert.Equal(0f, l.Reach);
        Hold(l, 0.8f, 0.5f, rise: 0.2f);
        TestUtil.Near(0.8f, l.Reach);
    }

    [Fact]
    public void BeforeLearning_CloseHandsExactAndFarOnesWithinReach()
    {
        Assert.Equal(0f, new ReachLearner().Reach);
        TestUtil.Near(0.3f, ArmReach.Map(0.3f, Avatar, 0f));
        Assert.True(ArmReach.Map(0.86f, Avatar, 0f) <= Bent + 1e-5f);
    }

    [Fact]
    public void Weight_DropsAtOnceOnGrab()
    {
        // The item is welded to the controller pose the tick it is grabbed: the hand must already be there.
        Assert.Equal(0f, ArmReach.StepWeight(1f, holding: true, upright: true, Tick));
    }

    [Fact]
    public void Weight_FadesBackInOnRelease()
    {
        float w = ArmReach.StepWeight(1f, true, true, Tick);
        float last = w;
        for (int i = 0; i < 40; i++)
        {
            w = ArmReach.StepWeight(w, false, true, Tick);
            Assert.True(w - last <= Tick / ArmReach.FadeSeconds + 1e-6f, "release snapped");
            last = w;
        }
        Assert.Equal(1f, w);
    }

    [Fact]
    public void Weight_FadesOutWhenNotUpright()
    {
        float w = ArmReach.StepWeight(1f, false, false, Tick);
        TestUtil.Near(1f - Tick / ArmReach.FadeSeconds, w);
    }

    [Fact]
    public void Learner_ResetForgetsTheReach()
    {
        // A world-scale change: the reach was learned in the old world units.
        var l = new ReachLearner();
        Hold(l, 0.7f, 0.5f);
        l.Reset();
        Assert.Equal(0f, l.Reach);
        Hold(l, 0.6f, 0.5f);
        TestUtil.Near(0.6f, l.Reach);
    }
}
