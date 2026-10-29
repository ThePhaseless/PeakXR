namespace PeakXR.Core.Tests;

public class ThrowPathTests
{
    static readonly Vector3 G = new(0f, -9.81f, 0f);

    [Fact]
    public void Trace_StartsAtTheReleasePoint()
    {
        var points = new Vector3[4];
        ThrowPath.Trace(new Vector3(1f, 2f, 3f), Vector3.UnitZ, G, 0f, 0.1f, points);
        Assert.Equal(new Vector3(1f, 2f, 3f), points[0]);
    }

    [Fact]
    public void Trace_FollowsTheBallisticCurveWithoutDamping()
    {
        // 50 steps of 0.02 s: after 1 s a 5 m/s level throw is 5 m out and about g/2 lower.
        var points = new Vector3[51];
        ThrowPath.Trace(Vector3.Zero, new Vector3(0f, 0f, 5f), G, 0f, 0.02f, points);
        Assert.Equal(5f, points[50].Z, 3);
        Assert.InRange(points[50].Y, -5.1f, -4.8f);
    }

    [Fact]
    public void Trace_DampingShortensTheThrow()
    {
        var free = new Vector3[51];
        var damped = new Vector3[51];
        ThrowPath.Trace(Vector3.Zero, new Vector3(0f, 3f, 8f), G, 0f, 0.02f, free);
        ThrowPath.Trace(Vector3.Zero, new Vector3(0f, 3f, 8f), G, 0.5f, 0.02f, damped);
        Assert.True(damped[50].Z < free[50].Z);
    }
}
