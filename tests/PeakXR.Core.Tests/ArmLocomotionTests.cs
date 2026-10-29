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
    public void TwoHands_TheNewestGripLeads()
    {
        // Hand over hand: the hand that just took a hold pulls, the lower one still gripping must not halve it.
        var a = new ArmLocomotion();
        a.Update(true, Vector3.Zero, false, Vector3.Zero, 0.01f);
        a.Update(true, Vector3.Zero, true, new Vector3(0, 0.5f, 0), 0.01f);
        var d = a.Update(true, Vector3.Zero, true, new Vector3(0, 0.48f, 0), 0.01f);
        TestUtil.Near(new Vector3(0, 0.02f, 0), d, 1e-5f);
        // Either hand: the left taking a new hold leads in turn.
        a.Update(false, new Vector3(0, 0.6f, 0), true, new Vector3(0, 0.48f, 0), 0.01f);
        a.Update(true, new Vector3(0, 0.6f, 0), true, new Vector3(0, 0.48f, 0), 0.01f);
        d = a.Update(true, new Vector3(0, 0.57f, 0), true, new Vector3(0, 0.48f, 0), 0.01f);
        TestUtil.Near(new Vector3(0, 0.03f, 0), d, 1e-5f);
    }

    [Fact]
    public void LeadLetsGo_TheOtherGripCarriesOn()
    {
        var a = new ArmLocomotion();
        a.Update(true, Vector3.Zero, false, Vector3.Zero, 0.01f);
        a.Update(true, Vector3.Zero, true, new Vector3(0, 0.5f, 0), 0.01f);
        // The right (lead) opens: the left, gripping all along, pulls from this frame on.
        var d = a.Update(true, new Vector3(0, -0.01f, 0), false, new Vector3(0, 0.6f, 0), 0.01f);
        TestUtil.Near(new Vector3(0, 0.01f, 0), d, 1e-5f);
    }

    [Fact]
    public void SecondHandGrabbing_JoinsWithoutJump()
    {
        // The new hand leads from its next frame; on its first, the hand already on the wall still counts.
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
