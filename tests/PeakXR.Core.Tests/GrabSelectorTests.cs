namespace PeakXR.Core.Tests;

public class GrabSelectorTests
{
    static readonly Vector3 Fwd = new(0, 0, 1);

    [Fact]
    public void None_ReturnsMinusOne()
    {
        var pos = new[] { new Vector3(1, 0, 0) };
        Assert.Equal(-1, GrabSelector.Select(Vector3.Zero, Fwd, pos, null, pos.Length, 0.2f));
        Assert.Equal(-1, GrabSelector.Select(Vector3.Zero, Fwd, null, null, 0, 0.2f));
    }

    [Fact]
    public void PicksNearestInRange()
    {
        var pos = new[]
        {
            new Vector3(0, 0, 0.15f),
            new Vector3(0, 0, 0.05f),
            new Vector3(0, 0, 0.5f),
        };
        Assert.Equal(1, GrabSelector.Select(Vector3.Zero, Fwd, pos, null, pos.Length, 0.2f));
    }

    [Fact]
    public void RadiiExtendReach()
    {
        var pos = new[] { new Vector3(0, 0, 0.5f) };
        var radii = new[] { 0.4f };
        Assert.Equal(0, GrabSelector.Select(Vector3.Zero, Fwd, pos, radii, 1, 0.2f));
    }

    [Fact]
    public void AnglePenalty_PrefersObjectInFront()
    {
        var pos = new[] { new Vector3(0, 0, -0.1f), new Vector3(0, 0, 0.11f) };
        Assert.Equal(1, GrabSelector.Select(Vector3.Zero, Fwd, pos, null, pos.Length, 0.2f));
        Assert.Equal(0, GrabSelector.Select(Vector3.Zero, Fwd, pos, null, pos.Length, 0.2f, 0f));
    }

    [Fact]
    public void CountLimitsCandidates()
    {
        var pos = new[] { new Vector3(0, 0, 0.15f), new Vector3(0, 0, 0.01f) };
        Assert.Equal(0, GrabSelector.Select(Vector3.Zero, Fwd, pos, null, 1, 0.2f));
    }
}
