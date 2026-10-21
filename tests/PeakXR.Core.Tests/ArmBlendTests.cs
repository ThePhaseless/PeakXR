namespace PeakXR.Core.Tests;

public class ArmBlendTests
{
    static void Run(ArmBlend b, bool tracked, float seconds, float dt = 0.01f)
    {
        for (float s = 0f; s < seconds - 1e-6f; s += dt)
            b.Update(tracked, dt);
    }

    [Theory]
    [InlineData(false, 0f, 1f, true)]
    [InlineData(false, 0f, 0.5f, true)]
    [InlineData(false, 0f, 0.49f, false)]
    [InlineData(false, 2f, 1f, false)]
    [InlineData(true, 0f, 1f, false)]
    public void Tracked_OnlyWhileTheGameControlsTheBody(
        bool down,
        float fallSeconds,
        float control,
        bool expected
    ) => Assert.Equal(expected, ArmBlend.Tracked(down, fallSeconds, control));

    [Fact]
    public void FreshPose_BlendsInFromVanilla()
    {
        var b = new ArmBlend();
        Assert.Equal(0f, b.Weight);
        Run(b, true, b.ReturnSeconds * 0.5f);
        Assert.InRange(b.Weight, 0.1f, 0.9f);
        Run(b, true, b.ReturnSeconds);
        Assert.Equal(1f, b.Weight);
    }

    [Fact]
    public void Ragdoll_ReleasesTheArmsFully()
    {
        var b = new ArmBlend();
        Run(b, true, 1f);
        Run(b, false, b.ReleaseSeconds + 0.02f);
        Assert.Equal(0f, b.Weight);
    }

    [Fact]
    public void Blend_NeverSnaps()
    {
        var b = new ArmBlend();
        const float dt = 0.01f;
        float last = b.Weight;
        float maxStep = 0f;
        foreach (
            var (tracked, seconds) in new[]
            {
                (true, 1f),
                (false, 0.1f),
                (true, 0.2f),
                (false, 1f),
                (true, 1f),
            }
        )
            for (float s = 0f; s < seconds; s += dt)
            {
                b.Update(tracked, dt);
                maxStep = System.Math.Max(maxStep, System.Math.Abs(b.Weight - last));
                last = b.Weight;
            }
        // Smoothstep's steepest slope is 1.5x the linear rate of the faster (release) fade.
        Assert.True(maxStep <= 1.5f * dt / b.ReleaseSeconds + 1e-4f, $"step {maxStep}");
    }

    [Fact]
    public void Release_FinishesBeforeTheCameraDetaches()
    {
        // While the body ragdolls the camera may move behind it (third person): the tracked hands follow the view, so
        // the arms must be fully released before the rig leaves the body.
        var b = new ArmBlend();
        Run(b, true, 1f);
        var view = new CameraTakeover();
        const float dt = 0.005f;
        for (float s = 0f; s < 2f; s += dt)
        {
            b.Update(false, dt);
            view.Update(true, dt);
            if (view.SwitchedThisFrame)
            {
                Assert.Equal(0f, b.Weight);
                return;
            }
        }
        Assert.Fail("view never detached");
    }

    [Fact]
    public void Reset_DropsToVanilla()
    {
        var b = new ArmBlend();
        Run(b, true, 1f);
        b.Reset();
        Assert.Equal(0f, b.Weight);
    }
}
