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

    [Fact]
    public void SightPoints_IncludeTheTopOfTheItem()
    {
        // VR run 50: an Apple partly sunk into uneven ground. The point nearest the hand and the centre sit below the
        // terrain surface, so a line to either hits the ground; the top of the item still shows.
        var into = new Vector3[GrabSelector.SightPointCount];
        int n = GrabSelector.SightPoints(
            new Vector3(0, -0.02f, 0.55f),
            new Vector3(0, 0f, 0.55f),
            new Vector3(0.04f, 0.04f, 0.04f),
            into
        );
        Assert.Equal(4, n);
        Assert.Equal(new Vector3(0, -0.02f, 0.55f), into[0]);
        Assert.Equal(new Vector3(0, 0f, 0.55f), into[1]);
        Assert.Equal(new Vector3(0, 0.04f, 0.55f), into[2]);
        // Just above its top: a flat item in a rock hollow (VR mp19's compass) shows only from above.
        TestUtil.Near(new Vector3(0, 0.04f + GrabSelector.SightAboveTop, 0.55f), into[3]);
    }

    [Fact]
    public void HandOnTheItem_SeenByTheHand_CountsWhenTheEyeSeesTheHand()
    {
        // VR mp19: a compass 3 cm from the hand on rocky ground, hidden from the stooped eye on every point.
        Assert.True(GrabSelector.Seen(eyeSeesItem: false, handSeesItem: true, eyeSeesHand: true));
        Assert.True(GrabSelector.Seen(eyeSeesItem: true, handSeesItem: false, eyeSeesHand: false));
        // A hand pushed through a rock fin: the eye cannot see it, so nothing behind the fin is grabbed.
        Assert.False(GrabSelector.Seen(eyeSeesItem: false, handSeesItem: true, eyeSeesHand: false));
        Assert.False(GrabSelector.Seen(eyeSeesItem: false, handSeesItem: false, eyeSeesHand: true));
    }
}
