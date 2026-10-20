namespace PeakXR.Core.Tests;

public class TwistKnobTests
{
    static readonly Vector3 Axis = new(0, 0, 1);

    [Fact]
    public void TwistAngle_RollAboutAxis_IsSignedDegrees()
    {
        var from = Quaternion.Identity;
        var to = Quaternion.CreateFromAxisAngle(Axis, 20f * UnityQuat.Deg2Rad);
        TestUtil.Near(20f, TwistKnob.TwistAngle(from, to, Axis), 1e-3f);
        TestUtil.Near(-20f, TwistKnob.TwistAngle(to, from, Axis), 1e-3f);
    }

    [Fact]
    public void TwistAngle_IgnoresSwingAboutOtherAxes()
    {
        var from = Quaternion.Identity;
        var to = Quaternion.CreateFromAxisAngle(new Vector3(1, 0, 0), 40f * UnityQuat.Deg2Rad);
        TestUtil.Near(0f, TwistKnob.TwistAngle(from, to, Axis), 1e-3f);
    }

    [Fact]
    public void TwistAngle_UsesTheAxisInTheStartFrame()
    {
        // Hand pointing along +X (yawed 90), rolling about its own forward.
        var from = Quaternion.CreateFromAxisAngle(new Vector3(0, 1, 0), MathF.PI / 2f);
        var roll = Quaternion.CreateFromAxisAngle(Axis, 15f * UnityQuat.Deg2Rad);
        var to = UnityQuat.Mul(from, roll);
        TestUtil.Near(15f, TwistKnob.TwistAngle(from, to, Axis), 1e-3f);
    }

    [Fact]
    public void Steps_EmittedPerStepAngle_BothWays()
    {
        var k = new TwistKnob { StepDegrees = 30f };
        Assert.Equal(0, k.Update(20f, 0.1f));
        Assert.Equal(1, k.Update(20f, 0.1f));
        Assert.Equal(0, k.Update(-15f, 0.1f));
        Assert.Equal(-1, k.Update(-30f, 0.1f));
    }

    [Fact]
    public void FastTurn_EmitsSeveralSteps()
    {
        var k = new TwistKnob { StepDegrees = 30f };
        Assert.Equal(3, k.Update(95f, 0.1f));
    }

    [Fact]
    public void Direction_FollowsSustainedTwistRate()
    {
        var k = new TwistKnob { HoldRate = 90f };
        k.Update(1f, 0.1f);
        Assert.Equal(0, k.Direction);
        for (int i = 0; i < 10; i++)
            k.Update(-20f, 0.1f);
        Assert.Equal(-1, k.Direction);
        for (int i = 0; i < 20; i++)
            k.Update(0f, 0.1f);
        Assert.Equal(0, k.Direction);
    }

    [Fact]
    public void Reset_ClearsPartialStep()
    {
        var k = new TwistKnob { StepDegrees = 30f };
        k.Update(25f, 0.1f);
        k.Reset();
        Assert.Equal(0, k.Update(10f, 0.1f));
    }
}
