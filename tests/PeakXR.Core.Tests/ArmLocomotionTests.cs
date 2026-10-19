namespace PeakXR.Core.Tests;

public class ArmLocomotionTests
{
    [Fact]
    public void GrabStartFrame_DoesNotMove()
    {
        var a = new ArmLocomotion();
        Assert.Equal(
            Vector3.Zero,
            a.Update(true, new Vector3(0, 1, 0), false, Vector3.Zero, 0.01f)
        );
    }

    [Fact]
    public void PullingDown_MovesBodyUp()
    {
        var a = new ArmLocomotion();
        a.Update(true, new Vector3(0, 1.5f, 0.3f), false, Vector3.Zero, 0.01f);
        var d = a.Update(true, new Vector3(0, 1.48f, 0.3f), false, Vector3.Zero, 0.01f);
        TestUtil.Near(new Vector3(0, 0.02f, 0), d, 1e-5f);
    }

    [Fact]
    public void TwoHands_AverageDeltas()
    {
        var a = new ArmLocomotion();
        a.Update(true, Vector3.Zero, true, Vector3.Zero, 0.01f);
        var d = a.Update(true, new Vector3(0, -0.02f, 0), true, new Vector3(0, -0.04f, 0), 0.01f);
        TestUtil.Near(new Vector3(0, 0.03f, 0), d, 1e-5f);
    }

    [Fact]
    public void SecondHandGrabbing_JoinsWithoutJump()
    {
        var a = new ArmLocomotion();
        a.Update(true, Vector3.Zero, false, new Vector3(5, 5, 5), 0.01f);
        var d = a.Update(true, new Vector3(0, -0.01f, 0), true, new Vector3(0.3f, 1, 0), 0.01f);
        TestUtil.Near(new Vector3(0, 0.01f, 0), d, 1e-5f);
    }

    [Fact]
    public void Release_StopsMovement()
    {
        var a = new ArmLocomotion();
        a.Update(true, Vector3.Zero, false, Vector3.Zero, 0.01f);
        Assert.Equal(
            Vector3.Zero,
            a.Update(false, new Vector3(0, -1, 0), false, Vector3.Zero, 0.01f)
        );
        Assert.Equal(
            Vector3.Zero,
            a.Update(true, new Vector3(0, -2, 0), false, Vector3.Zero, 0.01f)
        );
    }

    [Fact]
    public void ClampsToMaxSpeed()
    {
        var a = new ArmLocomotion { MaxSpeed = 2f };
        a.Update(true, Vector3.Zero, false, Vector3.Zero, 0.1f);
        var d = a.Update(true, new Vector3(0, -1f, 0), false, Vector3.Zero, 0.1f);
        TestUtil.Near(new Vector3(0, 0.2f, 0), d, 1e-5f);
    }
}
