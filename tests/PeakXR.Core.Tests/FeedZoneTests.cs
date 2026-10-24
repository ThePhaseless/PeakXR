namespace PeakXR.Core.Tests;

public class FeedZoneTests
{
    [Fact]
    public void ALongItem_CountsFromItsNearEnd_NotItsCentre()
    {
        // A lollipop 0.5 m long, its centre 0.25 m from the mouth while its candy end touches it.
        var centre = new Vector3(0f, 0f, 0.25f);
        var extents = new Vector3(0.05f, 0.05f, 0.25f);
        Assert.Equal(0f, FeedZone.Gap(centre, extents, Vector3.Zero), 4);
    }

    [Fact]
    public void Outside_IsTheDistanceToTheNearestFace()
    {
        var gap = FeedZone.Gap(
            new Vector3(0f, 0f, 1f),
            new Vector3(0.1f, 0.1f, 0.1f),
            Vector3.Zero
        );
        Assert.Equal(0.9f, gap, 4);
    }

    [Fact]
    public void Corner_IsEuclidean()
    {
        var gap = FeedZone.Gap(
            new Vector3(1f, 1f, 0f),
            new Vector3(0.5f, 0.5f, 0.5f),
            Vector3.Zero
        );
        Assert.Equal(MathF.Sqrt(0.5f), gap, 4);
    }

    [Fact]
    public void Inside_IsZero()
    {
        Assert.Equal(0f, FeedZone.Gap(Vector3.Zero, Vector3.One, new Vector3(0.2f, -0.3f, 0.5f)));
    }
}
