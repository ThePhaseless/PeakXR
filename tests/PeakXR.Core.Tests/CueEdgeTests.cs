namespace PeakXR.Core.Tests;

public class CueEdgeTests
{
    static readonly Vector3 Eye = new(0f, 1.6f, 0f);
    static readonly Vector3 Forward = Vector3.UnitZ;

    static float Angle(Vector3 a, Vector3 b) =>
        MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)), -1f, 1f))
        * 180f
        / MathF.PI;

    [Theory]
    [InlineData(0f, 3f, -1f)] // overhead and a little behind (a hand on a rope)
    [InlineData(1f, 0f, 0f)] // beside, right
    [InlineData(-0.5f, -0.2f, -1f)] // behind, left
    public void Spot_SitsOnTheConeEdge_TowardTheRealSpot_AtComfortDepth(float x, float y, float z)
    {
        var target = Eye + new Vector3(x, y, z);
        float edge = CueEdge.EdgeDegrees(CueEdge.HalfAngle, CueLayout.DenyDegrees);
        var spot = CueEdge.Spot(Eye, Forward, target, edge, CueEdge.Depth, out var outward);
        Assert.Equal(CueEdge.Depth, Vector3.Distance(Eye, spot), 3);
        Assert.Equal(edge, Angle(Forward, spot - Eye), 1);
        // Toward the real spot: the edge spot lies in the plane of forward and the target, on the target's side.
        var toTarget = target - Eye;
        var side = toTarget - Forward * Vector3.Dot(toTarget, Forward);
        Assert.True(Vector3.Dot(spot - Eye, side) > 0f);
        Assert.True(Vector3.Dot(outward, side) > 0.99f * side.Length());
        Assert.Equal(1f, outward.Length(), 3);
    }

    [Fact]
    public void Spot_StraightBehind_StillLandsOnTheEdge()
    {
        var spot = CueEdge.Spot(Eye, Forward, Eye - Forward * 2f, 40f, 1f, out var outward);
        Assert.Equal(40f, Angle(Forward, spot - Eye), 1);
        Assert.Equal(1f, outward.Length(), 3);
    }

    [Fact]
    public void EdgeRing_IsWhollyInsideTheCone()
    {
        float edge = CueEdge.EdgeDegrees(CueEdge.HalfAngle, CueLayout.DenyDegrees);
        Assert.True(edge + CueLayout.DenyDegrees * 0.5f < CueEdge.HalfAngle);
        Assert.True(
            edge > CueEdge.HalfAngle - 10f,
            "close to the edge, not in the middle of the view"
        );
    }

    [Fact]
    public void Cone_FollowsANarrowDisplay_CapsAtTheComfortAngle()
    {
        Assert.Equal(CueEdge.HalfAngle, CueEdge.ConeHalf(110f));
        Assert.True(CueEdge.ConeHalf(60f) < 30f);
        Assert.Equal(CueEdge.HalfAngle, CueEdge.ConeHalf(0f));
    }

    [Fact]
    public void Needed_HasHysteresis_SoTheEdgeRingNeverFlickers()
    {
        float half = CueEdge.HalfAngle;
        Assert.False(CueEdge.Needed(half - 1f, half, wasShown: false));
        Assert.True(CueEdge.Needed(half + 1f, half, wasShown: false));
        // Shown: stays until well inside.
        Assert.True(CueEdge.Needed(half - 1f, half, wasShown: true));
        Assert.False(CueEdge.Needed(half - CueEdge.Hysteresis - 0.1f, half, wasShown: true));
        // Wobbling around the boundary does not toggle it every frame.
        bool shown = false;
        int toggles = 0;
        for (int i = 0; i < 100; i++)
        {
            float off = half + (i % 2 == 0 ? 1.5f : -1.5f);
            bool now = CueEdge.Needed(off, half, shown);
            if (now != shown)
                toggles++;
            shown = now;
        }
        Assert.Equal(1, toggles);
    }
}
