namespace PeakXR.Core.Tests;

public class EyeOverlayFitTests
{
    /// <summary>Today's distance ahead of the head, near 0.1 + 0.03.</summary>
    private const float D = 0.13f;

    /// <summary>Half-size in tangents, which the quad keeps so the overlays keep today's density.</summary>
    private const float T = 3f;

    private const float Eye = 0.0315f;
    private static readonly Vector3 Origin = new(10f, 2f, -3f);
    private static readonly Quaternion Head = Quaternion.CreateFromYawPitchRoll(0.7f, -0.2f, 0.1f);

    private static Vector3 InHead(Vector3 v) => Vector3.Transform(v, Head);

    private static Vector3 EyeAt(float x) => Origin + InHead(new Vector3(x, 0f, 0f));

    private static Quaternion Canted(float degrees) =>
        Head * Quaternion.CreateFromAxisAngle(Vector3.UnitY, degrees * MathF.PI / 180f);

    /// <summary>Where a world ray from an eye meets a quad, in UV.</summary>
    private static (float U, float V) Hit(
        Vector3 eye,
        Vector3 direction,
        Vector3 centre,
        Quaternion rotation
    )
    {
        var normal = Vector3.Transform(Vector3.UnitZ, rotation);
        float along = Vector3.Dot(centre - eye, normal) / Vector3.Dot(direction, normal);
        var offset = eye + direction * along - centre;
        float half = T * D;
        return (
            0.5f + Vector3.Dot(offset, Vector3.Transform(Vector3.UnitX, rotation)) / (2f * half),
            0.5f + Vector3.Dot(offset, Vector3.Transform(Vector3.UnitY, rotation)) / (2f * half)
        );
    }

    private static (float U, float V) OnFittedQuad(Vector3 eye, Vector3 inHead) =>
        Hit(eye, InHead(inHead), EyeOverlayFit.Position(eye, Head, D), Head);

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.5f, 0.3f)]
    [InlineData(-1.2f, -0.9f)]
    public void TheSameHeadSpaceDirectionHitsTheSameUvInBothEyes(float tanX, float tanY)
    {
        var direction = new Vector3(tanX, tanY, 1f);
        var left = OnFittedQuad(EyeAt(-Eye), direction);
        var right = OnFittedQuad(EyeAt(Eye), direction);
        Assert.Equal(left.U, right.U, 1e-4f);
        Assert.Equal(left.V, right.V, 1e-4f);
        Assert.Equal(tanX / (2f * T), left.U - 0.5f, 1e-4f);
    }

    [Theory]
    [InlineData(-0.034f, 0.002f, 0.004f, 0.029f, -0.003f, -0.006f, 0.8f, -0.5f)]
    [InlineData(-0.029f, -0.004f, -0.002f, 0.036f, 0.001f, 0.007f, -1.1f, 0.7f)]
    public void UnmirroredEyesHitTheSameUv(
        float leftX,
        float leftY,
        float leftZ,
        float rightX,
        float rightY,
        float rightZ,
        float tanX,
        float tanY
    )
    {
        // The eye's rotation is not an input and its offset cancels, so real displays' asymmetry changes nothing.
        var direction = new Vector3(tanX, tanY, 1f);
        var left = OnFittedQuad(Origin + InHead(new Vector3(leftX, leftY, leftZ)), direction);
        var right = OnFittedQuad(Origin + InHead(new Vector3(rightX, rightY, rightZ)), direction);
        Assert.Equal(left.U, right.U, 1e-4f);
        Assert.Equal(left.V, right.V, 1e-4f);
    }

    [Fact]
    public void ReferenceAQuadAlignedToACantedEyeDiffersByThreeHundredths()
    {
        var direction = InHead(Vector3.UnitZ);
        var left = Canted(5f);
        var right = Canted(-5f);
        var eyeLeft = EyeAt(-Eye);
        var eyeRight = EyeAt(Eye);
        var uvLeft = Hit(
            eyeLeft,
            direction,
            eyeLeft + Vector3.Transform(Vector3.UnitZ, left) * D,
            left
        );
        var uvRight = Hit(
            eyeRight,
            direction,
            eyeRight + Vector3.Transform(Vector3.UnitZ, right) * D,
            right
        );
        Assert.Equal(0.029f, MathF.Abs(uvLeft.U - uvRight.U), 0.002f);
    }

    [Fact]
    public void ReferenceTodaysHeadCentredQuadDiffersByEightHundredths()
    {
        var centre = Origin + InHead(Vector3.UnitZ) * D;
        var left = Hit(EyeAt(-Eye), InHead(Vector3.UnitZ), centre, Head);
        var right = Hit(EyeAt(Eye), InHead(Vector3.UnitZ), centre, Head);
        Assert.Equal(0.081f, MathF.Abs(left.U - right.U), 0.001f);
    }

    [Theory]
    [InlineData(-Eye, -1.43f, 1.19f, 0f)]
    [InlineData(Eye, -1.19f, 1.43f, 0f)]
    [InlineData(-Eye, -1.43f, 1.19f, 5f)]
    [InlineData(Eye, -1.19f, 1.43f, -5f)]
    public void EveryFrustumCornerLandsInsideTheQuad(
        float eyeX,
        float tanLeft,
        float tanRight,
        float cant
    )
    {
        var eye = EyeAt(eyeX);
        var centre = EyeOverlayFit.Position(eye, Head, D);
        foreach (var tanX in new[] { tanLeft, tanRight })
        foreach (var tanY in new[] { -1.47f, 1.47f })
        {
            // Along the eye's own axes, which a cant turns away from the head's.
            var direction = Vector3.Transform(new Vector3(tanX, tanY, 1f), Canted(cant));
            var (u, v) = Hit(eye, direction, centre, Head);
            Assert.InRange(u, 0f, 1f);
            Assert.InRange(v, 0f, 1f);
        }
    }
}
