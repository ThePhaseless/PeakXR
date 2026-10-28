namespace PeakXR.Core.Tests;

public class FaceUpTests
{
    [Theory]
    [InlineData(0f, -0.2f, 1f)] // a little below
    [InlineData(0.3f, -2f, 1f)] // steeply below: looking down at a target near the feet
    [InlineData(-1f, 1.5f, 0.2f)] // above
    public void LookAtTheEye_FacesTheSightLine_WithNoRoll(float x, float y, float z)
    {
        var sight = new Vector3(x, y, z);
        var q = UnityQuat.LookRotation(sight, SpatialMath.FaceUp(sight, Vector3.UnitY));
        var forward = UnityQuat.Rotate(q, Vector3.UnitZ);
        var right = UnityQuat.Rotate(q, Vector3.UnitX);
        var up = UnityQuat.Rotate(q, Vector3.UnitY);
        TestUtil.Near(1f, Vector3.Dot(forward, Vector3.Normalize(sight)), 1e-4f);
        TestUtil.Near(0f, right.Y, 1e-4f);
        Assert.True(up.Y > 0f);
    }

    [Fact]
    public void StraightDown_TakesTheHeadsUp_SoTheTextStaysUprightInTheView()
    {
        var headUp = Vector3.Normalize(new Vector3(0.2f, 0f, 1f));
        var sight = new Vector3(0f, -1f, 0.001f);
        var q = UnityQuat.LookRotation(sight, SpatialMath.FaceUp(sight, headUp));
        var up = UnityQuat.Rotate(q, Vector3.UnitY);
        Assert.True(Vector3.Dot(up, headUp) > 0.99f);
    }
}
