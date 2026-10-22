namespace PeakXR.Core.Tests;

public class LegibilityTests
{
    static readonly Vector3 Eye = new(0f, 1.6f, 0f);
    static readonly Vector3 Forward = new(0f, 0f, 1f);

    // 1 cm tall at 0.6 m straight ahead, facing the eye: about 0.95 degrees.
    [Fact]
    public void LabelAheadAtArmLength_Reads()
    {
        var why = Legibility.Check(
            Eye,
            Forward,
            Eye + new Vector3(0f, 0f, 0.6f),
            Forward,
            0.01f,
            out var deg,
            out var dist,
            out var off,
            out var tilt
        );
        Assert.Null(why);
        TestUtil.Near(0.955f, deg, 0.01f);
        TestUtil.Near(0.6f, dist);
        TestUtil.Near(0f, off);
        TestUtil.Near(0f, tilt);
    }

    [Fact]
    public void SpeckOfText_IsTooSmall()
    {
        var why = Legibility.Check(
            Eye,
            Forward,
            Eye + new Vector3(0f, 0f, 2f),
            Forward,
            0.01f,
            out var deg,
            out _,
            out _,
            out _
        );
        Assert.True(deg < Legibility.MinTextDegrees);
        Assert.Contains("small", why, StringComparison.Ordinal);
    }

    [Fact]
    public void LabelAtTheNose_IsTooNear()
    {
        var why = Legibility.Check(
            Eye,
            Forward,
            Eye + new Vector3(0f, 0f, 0.1f),
            Forward,
            0.01f,
            out _,
            out _,
            out _,
            out _
        );
        Assert.Contains("near", why, StringComparison.Ordinal);
    }

    [Fact]
    public void LabelBesideTheHead_IsOutOfView()
    {
        var why = Legibility.Check(
            Eye,
            Forward,
            Eye + new Vector3(0.6f, 0f, 0.3f),
            new Vector3(1f, 0f, 0f),
            0.02f,
            out _,
            out _,
            out var off,
            out _
        );
        Assert.True(off > Legibility.MaxOffViewDegrees);
        Assert.Contains("view", why, StringComparison.Ordinal);
    }

    [Fact]
    public void LabelSeenEdgeOnOrFromBehind_IsUnreadable()
    {
        var at = Eye + new Vector3(0f, 0f, 0.6f);
        Assert.Contains(
            "edge",
            Legibility.Check(
                Eye,
                Forward,
                at,
                new Vector3(1f, 0f, 0f),
                0.01f,
                out _,
                out _,
                out _,
                out var tilt
            ),
            StringComparison.Ordinal
        );
        TestUtil.Near(90f, tilt, 0.01f);
        Assert.NotNull(
            Legibility.Check(Eye, Forward, at, -Forward, 0.01f, out _, out _, out _, out _)
        );
    }
}
