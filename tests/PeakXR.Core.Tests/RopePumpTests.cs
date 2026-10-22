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
}
