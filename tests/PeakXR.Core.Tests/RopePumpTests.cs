namespace PeakXR.Core.Tests;

public class RopePumpTests
{
    [Fact]
    public void StillHead_DoesNothing() =>
        Assert.Equal(Vector3.Zero, RopePump.Push(Vector3.Zero, 0.25f, 4f, 3f));

    [Fact]
    public void SwayBelowDeadzone_DoesNothing() =>
        Assert.Equal(Vector3.Zero, RopePump.Push(new Vector3(0.2f, 0f, 0f), 0.25f, 4f, 3f));

    [Fact]
    public void Lean_PushesTheSameWay()
    {
        var a = RopePump.Push(new Vector3(0.5f, 0f, 0f), 0.25f, 4f, 3f);
        TestUtil.Near(2f, a.X);
        Assert.Equal(0f, a.Z);
    }

    [Fact]
    public void VerticalHeadMotion_IsIgnored()
    {
        // A nod or a crouch is not a pump: only the level part of the head's motion swings the rope.
        var a = RopePump.Push(new Vector3(0f, 2f, 0.5f), 0.25f, 4f, 3f);
        Assert.Equal(0f, a.Y);
        TestUtil.Near(2f, a.Z);
    }

    [Fact]
    public void HardLean_IsCapped()
    {
        var a = RopePump.Push(new Vector3(3f, 0f, 4f), 0.25f, 4f, 3f);
        TestUtil.Near(3f, a.Length());
    }

    [Fact]
    public void SwingAtTheComfortCeiling_PushAlongItIsDropped()
    {
        var push = new Vector3(2f, 0f, 0f);
        Assert.Equal(Vector3.Zero, RopePump.Limit(push, new Vector3(0.8f, 0f, 0f), 0.8f));
        Assert.Equal(Vector3.Zero, RopePump.Limit(push, new Vector3(1.5f, -3f, 0.2f), 0.8f));
    }

    [Fact]
    public void SwingBelowTheCeiling_KeepsThePush()
    {
        var push = new Vector3(2f, 0f, 0f);
        Assert.Equal(push, RopePump.Limit(push, new Vector3(0.5f, 0f, 0f), 0.8f));
        // Only the level swing counts: a rope dropping fast as the climber moves is not a swing.
        Assert.Equal(push, RopePump.Limit(push, new Vector3(0.5f, -3f, 0f), 0.8f));
    }

    [Fact]
    public void LeanAgainstAFastSwing_StillBrakesIt()
    {
        var push = new Vector3(-2f, 0f, 0f);
        Assert.Equal(push, RopePump.Limit(push, new Vector3(1.5f, 0f, 0f), 0.8f));
    }

    [Fact]
    public void PumpOnSomeoneElsesRope_RefusedOncePerHold()
    {
        bool refused = false;
        Assert.True(RopePump.RefuseOnce(false, 1f, ref refused));
        Assert.False(RopePump.RefuseOnce(false, 1f, ref refused));
        Assert.True(refused);
    }

    [Fact]
    public void OwnRopeOrNoPump_NeverRefused()
    {
        bool refused = false;
        Assert.False(RopePump.RefuseOnce(true, 1f, ref refused));
        Assert.False(RopePump.RefuseOnce(false, 0f, ref refused));
        Assert.False(refused);
    }
}
