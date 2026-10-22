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
    public void Target_CloseHandUnchanged()
    {
        var shoulder = new Vector3(0.2f, 1.1f, 0f);
        var controller = shoulder + new Vector3(0f, -0.1f, 0.3f);
        TestUtil.Near(controller, ArmReach.Target(shoulder, controller, Avatar, 0.9f));
    }

    [Fact]
    public void Learn_KeepsTheLongestPlausibleReach()
    {
        float p = 0f;
        p = ArmReach.Learn(p, 0.7f, Avatar);
        p = ArmReach.Learn(p, 0.5f, Avatar);
        TestUtil.Near(0.7f, p);
        p = ArmReach.Learn(p, 0.86f, Avatar);
        TestUtil.Near(0.86f, p);
        // A hand metres off the shoulder is a warp or a lagging view, not an arm.
        p = ArmReach.Learn(p, 3f, Avatar);
        TestUtil.Near(0.86f, p);
    }
}
