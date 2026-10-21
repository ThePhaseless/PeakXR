namespace PeakXR.Core.Tests;

public class LaserLookTests
{
    static readonly Vector3 Eye = Vector3.Zero;

    [Fact]
    public void RayFromTheMouth_CrossesTheFace()
    {
        // vr.21: the bugle hand at the mouth, tilted up, drew its ray up through the view.
        var mouth = new Vector3(0.03f, -0.07f, 0.08f);
        Assert.True(LaserLook.CrossesFace(mouth, mouth + new Vector3(0f, 0.5f, 0.87f), Eye));
    }

    [Fact]
    public void RayFromTheChest_IsClear()
    {
        var chest = new Vector3(0.15f, -0.4f, 0.25f);
        Assert.False(LaserLook.CrossesFace(chest, chest + new Vector3(0f, 0.1f, 1.5f), Eye));
    }

    [Fact]
    public void RaySweptAcrossInFrontOfTheEyes_CrossesTheFace()
    {
        Assert.True(
            LaserLook.CrossesFace(new Vector3(0.3f, 0f, 0.1f), new Vector3(-0.5f, 0f, 0.1f), Eye)
        );
    }

    [Fact]
    public void RayPointingAwayPastTheHead_IsClear()
    {
        // A hand raised beside the head pointing up and out.
        var hand = new Vector3(0.25f, 0.05f, 0.1f);
        Assert.False(LaserLook.CrossesFace(hand, hand + new Vector3(0.3f, 0.8f, 0.5f), Eye));
    }

    [Fact]
    public void ZeroLengthRay_UsesItsOrigin()
    {
        Assert.True(
            LaserLook.CrossesFace(new Vector3(0f, 0f, 0.1f), new Vector3(0f, 0f, 0.1f), Eye)
        );
        Assert.False(LaserLook.CrossesFace(new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, 1f), Eye));
    }

    [Fact]
    public void Cursor_KeepsItsAngularSize()
    {
        float near = LaserLook.CursorDiameter(1f, false);
        float far = LaserLook.CursorDiameter(3f, false);
        TestUtil.Near(3f * near, far, 1e-5f);
        // The old 8 mm dot at 1.5 m was 0.3 degrees: lost on white text.
        Assert.True(LaserLook.CursorDiameter(1.5f, false) >= 0.014f);
    }

    [Fact]
    public void Cursor_HasAFloorUpClose()
    {
        Assert.Equal(LaserLook.MinCursor, LaserLook.CursorDiameter(0.05f, false));
    }

    [Fact]
    public void Cursor_GrowsOnHover()
    {
        Assert.True(
            LaserLook.CursorDiameter(1.5f, true) > 1.3f * LaserLook.CursorDiameter(1.5f, false)
        );
    }
}
