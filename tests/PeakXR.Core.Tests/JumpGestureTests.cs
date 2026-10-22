namespace PeakXR.Core.Tests;

public class JumpGestureTests
{
    const float Dt = 1f / 90f;

    // Calibrated standing eye height in tracking space.
    const float Eye = 1.6f;

    /// <summary>One frame of motion for the gesture: head height and pitch, hand heights, crouch, arm swing.</summary>
    struct Pose
    {
        public float Head;
        public float Pitch;
        public float Left;
        public float Right;
        public bool Crouched;
        public bool Swinging;
    }

    static Pose Standing =>
        new()
        {
            Head = Eye,
            Left = Eye - 0.5f,
            Right = Eye - 0.5f,
        };

    /// <summary>Runs <paramref name="pose"/> (t from 0 to seconds) through the gesture; counts the hops seen.</summary>
    static int Run(JumpGesture g, float seconds, Func<float, Pose> pose, bool enabled = true)
    {
        int hops = 0;
        for (float t = 0f; t < seconds; t += Dt)
        {
            var p = pose(t);
            if (
                g.Update(enabled, p.Head, Eye, p.Pitch, p.Left, p.Right, p.Crouched, p.Swinging, Dt)
            )
                hops++;
        }
        return hops;
    }

    static float Ramp(float t, float from, float to) =>
        Math.Clamp((t - from) / (to - from), 0f, 1f);

    /// <summary>A hop at <paramref name="at"/>: both arms swing up 0.45 m in 0.2 s as the head rises 0.1 m above
    /// standing in 0.2 s, then everything settles back.</summary>
    static Pose Hop(float t, float at)
    {
        var p = Standing;
        float up = Ramp(t, at, at + 0.2f) - Ramp(t, at + 0.35f, at + 0.6f);
        float rise = Ramp(t, at + 0.05f, at + 0.25f) - Ramp(t, at + 0.35f, at + 0.6f);
        p.Left += 0.45f * up;
        p.Right += 0.45f * up;
        p.Head += 0.1f * rise;
        return p;
    }

    [Fact]
    public void ArmsUpWhileTheHeadRisesAboveStanding_IsOneHop()
    {
        Assert.Equal(1, Run(new JumpGesture(), 1.5f, t => Hop(t, 0.5f)));
    }

    [Fact]
    public void Disabled_NeverHops()
    {
        Assert.Equal(0, Run(new JumpGesture(), 1.5f, t => Hop(t, 0.5f), enabled: false));
    }

    [Fact]
    public void StandingUpFromACrouch_IsNotAHop()
    {
        // Pushing up out of a deep crouch fast, arms swinging up for balance, overshooting standing by 3 cm.
        Assert.Equal(
            0,
            Run(
                new JumpGesture(),
                2f,
                t =>
                {
                    var p = Standing;
                    float rise = Ramp(t, 0.8f, 1.2f);
                    p.Head = Eye - 0.6f + 0.63f * rise;
                    p.Left = p.Right = Eye - 0.9f + 0.6f * rise;
                    p.Crouched = p.Head < Eye - 0.35f;
                    return p;
                }
            )
        );
    }

    [Fact]
    public void RisingQuicklyRightAfterACrouch_IsNotAHop()
    {
        // Out of the crouch and straight up past standing: still the stand-up, inside the crouch grace.
        Assert.Equal(
            0,
            Run(
                new JumpGesture(),
                2f,
                t =>
                {
                    var p = Hop(t, 0.6f);
                    p.Crouched = t < 0.5f;
                    return p;
                }
            )
        );
    }

    [Fact]
    public void LookingDownAndBackUp_IsNotAHop()
    {
        // The headset dips 6 cm as the head pitches 60 degrees down, then comes back quickly and a little past
        // standing, with the hands lifted to look at something held.
        Assert.Equal(
            0,
            Run(
                new JumpGesture(),
                2f,
                t =>
                {
                    var p = Standing;
                    float down = Ramp(t, 0.2f, 0.5f) - Ramp(t, 1f, 1.15f);
                    p.Pitch = 60f * down;
                    p.Head = Eye - 0.06f * down + 0.05f * Ramp(t, 1.1f, 1.2f);
                    p.Left = p.Right = Eye - 0.5f + 0.4f * Ramp(t, 1f, 1.15f);
                    return p;
                }
            )
        );
    }

    [Fact]
    public void HopDuringArmSwingSprint_IsNotAHop()
    {
        Assert.Equal(
            0,
            Run(
                new JumpGesture(),
                1.5f,
                t =>
                {
                    var p = Hop(t, 0.5f);
                    p.Swinging = true;
                    return p;
                }
            )
        );
    }

    [Fact]
    public void ReachingOverheadWithoutRising_IsNotAHop()
    {
        Assert.Equal(
            0,
            Run(
                new JumpGesture(),
                1.5f,
                t =>
                {
                    var p = Hop(t, 0.5f);
                    p.Head = Eye;
                    return p;
                }
            )
        );
    }

    [Fact]
    public void RisingOnTiptoesSlowly_IsNotAHop()
    {
        Assert.Equal(
            0,
            Run(
                new JumpGesture(),
                2f,
                t =>
                {
                    var p = Standing;
                    p.Head = Eye + 0.08f * Ramp(t, 0.2f, 1.2f);
                    return p;
                }
            )
        );
    }

    [Fact]
    public void HeadBobWithoutArms_IsNotAHop()
    {
        // A bounce on the spot (or a step up) lifts the head, but the arms stay down.
        Assert.Equal(
            0,
            Run(
                new JumpGesture(),
                1.5f,
                t =>
                {
                    var p = Hop(t, 0.5f);
                    p.Left = p.Right = Eye - 0.5f;
                    return p;
                }
            )
        );
    }

    [Fact]
    public void TwoHopsCloserThanTheCooldown_CountOnce()
    {
        Assert.Equal(1, Run(new JumpGesture(), 1.5f, t => t < 0.5f ? Hop(t, 0.1f) : Hop(t, 0.5f)));
    }
}
