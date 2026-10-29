namespace PeakXR.Core.Tests;

public class StereoCullTests
{
    const float Near = 0.1f;
    const float Far = 1500f;
    const float Ipd = 0.064f;

    // An Index-like pair: each eye sees further out than in (outer 1.2, inner 1.0), mirrored.
    static EyeView Left(Quaternion head, Vector3 at, Quaternion cant = default) =>
        new(
            at + Vector3.Transform(new Vector3(-Ipd / 2f, 0f, 0f), head),
            cant == default ? head : head * cant,
            -1.2f,
            1.0f,
            -1.1f,
            1.05f
        );

    static EyeView Right(Quaternion head, Vector3 at, Quaternion cant = default) =>
        new(
            at + Vector3.Transform(new Vector3(Ipd / 2f, 0f, 0f), head),
            cant == default ? head : head * cant,
            -1.0f,
            1.2f,
            -1.1f,
            1.05f
        );

    static Vector3 Corner(in EyeView e, float x, float y, float z) =>
        e.Position + Vector3.Transform(new Vector3(x * z, y * z, z), e.Rotation);

    static void AssertHoldsEye(in CullView u, in EyeView e)
    {
        foreach (float z in new[] { Near, 1f, 37f, Far })
        foreach (float x in new[] { e.Left, 0f, e.Right })
        foreach (float y in new[] { e.Bottom, 0f, e.Top })
            Assert.True(
                u.Contains(Corner(e, x, y, z), 1e-4f),
                $"eye point x {x} y {y} z {z} outside"
            );
    }

    [Fact]
    public void SideBySideEyesShareTheOuterEdges()
    {
        var head = Quaternion.Identity;
        var l = Left(head, Vector3.Zero);
        var r = Right(head, Vector3.Zero);
        Assert.True(StereoCull.TryUnion(l, r, Near, Far, out var u));
        // The apex sits where the two outer planes meet, so the union is no wider than the outer eye edges.
        Assert.Equal(-1.2f, u.Left, 3);
        Assert.Equal(1.2f, u.Right, 3);
        Assert.Equal(-1.1f, u.Bottom, 3);
        Assert.Equal(1.05f, u.Top, 3);
        float behind = Ipd / 2.4f;
        Assert.Equal(-behind, u.Position.Z, 4);
        Assert.Equal(0f, u.Position.X, 4);
        Assert.Equal(Near + behind, u.Near, 4);
        AssertHoldsEye(u, l);
        AssertHoldsEye(u, r);
    }

    [Fact]
    public void BothOrdersGiveTheSameFrustum()
    {
        var head = Quaternion.CreateFromYawPitchRoll(0.7f, -0.3f, 0.1f);
        var at = new Vector3(12f, 340f, -88f);
        Assert.True(StereoCull.TryUnion(Left(head, at), Right(head, at), Near, Far, out var a));
        Assert.True(StereoCull.TryUnion(Right(head, at), Left(head, at), Near, Far, out var b));
        Assert.Equal(a.Left, b.Left, 5);
        Assert.Equal(a.Right, b.Right, 5);
        Assert.Equal(a.Near, b.Near, 5);
        Assert.True(Vector3.Distance(a.Position, b.Position) < 1e-4f);
    }

    [Fact]
    public void TurnedAndRaisedHeadStillHoldsBothEyes()
    {
        var head = Quaternion.CreateFromYawPitchRoll(2.1f, 0.6f, -0.2f);
        var at = new Vector3(-5f, 12f, 3f);
        var l = Left(head, at);
        var r = Right(head, at);
        Assert.True(StereoCull.TryUnion(l, r, Near, Far, out var u));
        AssertHoldsEye(u, l);
        AssertHoldsEye(u, r);
        Assert.Equal(1.2f, u.Right, 3);
    }

    [Fact]
    public void CantedDisplaysAreHeldWhole()
    {
        // Displays turned 10 degrees outward (Pimax-style): the union widens to hold both.
        var head = Quaternion.Identity;
        var l = Left(head, Vector3.Zero, Quaternion.CreateFromYawPitchRoll(-0.1745f, 0f, 0f));
        var r = Right(head, Vector3.Zero, Quaternion.CreateFromYawPitchRoll(0.1745f, 0f, 0f));
        Assert.True(StereoCull.TryUnion(l, r, Near, Far, out var u));
        AssertHoldsEye(u, l);
        AssertHoldsEye(u, r);
        Assert.True(u.Right > 1.2f);
    }

    [Fact]
    public void PointsOutsideBothEyesStayOut()
    {
        var head = Quaternion.Identity;
        Assert.True(
            StereoCull.TryUnion(
                Left(head, Vector3.Zero),
                Right(head, Vector3.Zero),
                Near,
                Far,
                out var u
            )
        );
        Assert.False(u.Contains(new Vector3(0f, 0f, -1f), 1e-4f)); // behind the head
        Assert.False(u.Contains(new Vector3(0f, 0f, Far * 1.01f), 1e-4f)); // past the far plane
        Assert.False(u.Contains(new Vector3(10f * 1.3f, 0f, 10f), 1e-4f)); // right of every eye
        Assert.False(u.Contains(new Vector3(0f, 10f * 1.2f, 10f), 1e-4f)); // above
    }

    [Fact]
    public void OneEyeTwiceIsThatEye()
    {
        var e = new EyeView(new Vector3(1f, 2f, 3f), Quaternion.Identity, -1f, 1f, -1f, 1f);
        Assert.True(StereoCull.TryUnion(e, e, Near, Far, out var u));
        Assert.Equal(-1f, u.Left, 4);
        Assert.Equal(1f, u.Right, 4);
        Assert.Equal(Near, u.Near, 4);
        Assert.True(Vector3.Distance(e.Position, u.Position) < 1e-4f);
    }

    [Theory]
    [InlineData(0f, 100f)]
    [InlineData(0.1f, 0.05f)]
    [InlineData(float.NaN, 100f)]
    public void BadClipPlanesAreRefused(float near, float far)
    {
        var head = Quaternion.Identity;
        Assert.False(
            StereoCull.TryUnion(
                Left(head, Vector3.Zero),
                Right(head, Vector3.Zero),
                near,
                far,
                out _
            )
        );
    }

    [Fact]
    public void BrokenPoseIsRefused()
    {
        var bad = new EyeView(
            new Vector3(float.NaN, 0f, 0f),
            Quaternion.Identity,
            -1f,
            1f,
            -1f,
            1f
        );
        var head = Quaternion.Identity;
        Assert.False(StereoCull.TryUnion(bad, Right(head, Vector3.Zero), Near, Far, out _));
    }

    [Fact]
    public void TangentsComeFromAGLProjection()
    {
        // Unity's Matrix4x4.Frustum(l, r, b, t, n, f) at near 0.1 with tangents -1.2, 1.0, -1.1, 1.05.
        float n = 0.1f;
        float l = -1.2f * n,
            r = 1.0f * n,
            b = -1.1f * n,
            t = 1.05f * n;
        float m00 = 2f * n / (r - l);
        float m02 = (r + l) / (r - l);
        float m11 = 2f * n / (t - b);
        float m12 = (t + b) / (t - b);
        var (left, right, bottom, top) = StereoCull.Tangents(m00, m02, m11, m12);
        Assert.Equal(-1.2f, left, 4);
        Assert.Equal(1.0f, right, 4);
        Assert.Equal(-1.1f, bottom, 4);
        Assert.Equal(1.05f, top, 4);
        Assert.Throws<ArgumentException>(() => StereoCull.Tangents(0f, 0f, 1f, 0f));
    }
}
