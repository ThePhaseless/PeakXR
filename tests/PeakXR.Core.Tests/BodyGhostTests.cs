namespace PeakXR.Core.Tests;

public class BodyGhostTests
{
    [Theory]
    [InlineData(0.3f)]
    [InlineData(0.9f)]
    [InlineData(3f)]
    public void NoGhost_ShowsEverything(float distance)
    {
        Assert.Equal(1f, BodyGhost.Alpha(0f, distance, vertexRed: 1f, rawVertexColor: false));
        Assert.Equal(1f, BodyGhost.Alpha(0f, distance, vertexRed: 1f, rawVertexColor: true));
    }

    [Fact]
    public void Ghost_HidesRedVerticesWithinAMetreOfTheCamera()
    {
        // The outfit is painted red all over: a knee 0.4 m out from the eye goes, so do sleeves.
        Assert.Equal(0f, BodyGhost.Alpha(1f, 0.4f, vertexRed: 1f, rawVertexColor: false));
        Assert.Equal(0f, BodyGhost.Alpha(1f, 0.99f, vertexRed: 1f, rawVertexColor: false));
    }

    [Fact]
    public void Ghost_KeepsUnpaintedVertices()
    {
        // Hands (skin) and shoes carry no red: the first-person view keeps them.
        Assert.Equal(1f, BodyGhost.Alpha(1f, 0.3f, vertexRed: 0f, rawVertexColor: false));
    }

    [Fact]
    public void Ghost_FadesBackInFromOneToOnePointTwoMetres()
    {
        float mid = BodyGhost.Alpha(1f, 1.1f, 1f, false);
        Assert.InRange(mid, 0.4f, 0.6f);
        Assert.Equal(1f, BodyGhost.Alpha(1f, 1.2f, 1f, false));
        Assert.Equal(1f, BodyGhost.Alpha(1f, 5f, 1f, false));
    }

    [Fact]
    public void RawVertexColor_GhostsTheWholeMesh()
    {
        // Hats: everything near the camera goes, painted or not.
        Assert.Equal(0f, BodyGhost.Alpha(1f, 0.3f, vertexRed: 0f, rawVertexColor: true));
    }

    [Fact]
    public void PartialGhost_StillThinsTheBody()
    {
        Assert.Equal(0.5f, BodyGhost.Alpha(0.5f, 0.3f, 1f, false), 3);
    }
}
