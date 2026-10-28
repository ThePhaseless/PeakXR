namespace PeakXR.Core.Tests;

public class CueEdgeLabelTests
{
    static readonly Vector3 Eye = new(0f, 1.6f, 0f);
    static readonly Vector3 Forward = Vector3.UnitZ;

    static float Angle(Vector3 a, Vector3 b) =>
        MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)), -1f, 1f))
        * 180f
        / MathF.PI;

    [Fact]
    public void LabelClear_CoversHalfTheRingAndTheLabelsExtentAlongThePointer()
    {
        // Pointing right: half the label's width counts; pointing down: half its height.
        Assert.Equal(5f + CueEdge.LabelGap + 10f, CueEdge.LabelClear(10f, 20f, 4f, 1f, 0f), 3);
        Assert.Equal(5f + CueEdge.LabelGap + 2f, CueEdge.LabelClear(10f, 20f, 4f, 0f, -1f), 3);
    }

    [Theory]
    [InlineData(1f, 0f, 0f)] // right
    [InlineData(0f, 3f, -1f)] // overhead behind
    [InlineData(-1f, -2f, -0.5f)] // low left behind
    public void LabelSpot_SitsInsideTheCone_BesideTheEdgeCopy_AtItsDepth(float x, float y, float z)
    {
        float ring = CueLayout.DenyDegrees;
        float edge = CueEdge.EdgeDegrees(CueEdge.HalfAngle, ring);
        var copy = CueEdge.Spot(
            Eye,
            Forward,
            Eye + new Vector3(x, y, z),
            edge,
            CueEdge.Depth,
            out var outward
        );
        float clear = CueEdge.LabelClear(ring, 20f, 4f, outward.X, outward.Y);
        var label = CueEdge.LabelSpot(Eye, copy, outward, clear);
        Assert.Equal(CueEdge.Depth, Vector3.Distance(Eye, label), 3);
        Assert.Equal(clear, Angle(copy - Eye, label - Eye), 1);
        // Toward the view centre, never further out than the edge copy.
        Assert.Equal(edge - clear, Angle(Forward, label - Eye), 1);
    }
}
