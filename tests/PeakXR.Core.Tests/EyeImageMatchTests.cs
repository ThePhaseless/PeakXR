namespace PeakXR.Core.Tests;

public class EyeImageMatchTests
{
    private const int W = 96;
    private const int H = 40;

    /// <summary>Zero-mean noise: for it, an uncorrelated pair reads sqrt 2.</summary>
    private static float[] Noise(int seed)
    {
        var random = new Random(seed);
        var field = new float[W * H];
        for (int i = 0; i < field.Length; i++)
            field[i] = (float)(random.NextDouble() * 2.0 - 1.0);
        return field;
    }

    /// <summary>The field as the other eye sees it: a point at column x shows at column x + k.</summary>
    private static float[] Offset(float[] l, int k)
    {
        var r = Noise(99);
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
            if (x + k >= 0 && x + k < W)
                r[y * W + x + k] = l[y * W + x];
        return r;
    }

    private static byte[] Rgba(int w, int h, byte value)
    {
        var data = new byte[w * h * 4];
        Array.Fill(data, value);
        return data;
    }

    private static void Paint(byte[] rgba, int w, int row, int from, int to)
    {
        for (int x = from; x <= to; x++)
            rgba[(row * w + x) * 4] = 200;
    }

    private static EyeView Eye(float left, float right) =>
        new(Vector3.Zero, Quaternion.Identity, left, right, -1.47f, 1.47f);

    [Fact]
    public void ResidualIsZeroAtTheOffsetAndSqrtTwoAwayFromIt()
    {
        var l = Noise(1);
        var r = Offset(l, 7);
        Assert.InRange(EyeImageMatch.Residual(l, r, W, H, 7), 0f, 0.001f);
        Assert.InRange(EyeImageMatch.Residual(l, r, W, H, 0), 1.3f, 1.5f);
    }

    [Fact]
    public void ResidualOfUncorrelatedFieldsIsSqrtTwo()
    {
        Assert.InRange(EyeImageMatch.Residual(Noise(2), Noise(3), W, H, 0), 1.3f, 1.5f);
    }

    [Fact]
    public void ResidualOfUncorrelatedAllPositiveFieldsIsAboutPointSeven()
    {
        var l = Array.ConvertAll(Noise(2), v => v + 1f);
        var r = Array.ConvertAll(Noise(3), v => v + 1f);
        Assert.InRange(EyeImageMatch.Residual(l, r, W, H, 0), 0.65f, 0.75f);
    }

    [Fact]
    public void AxisShiftOfMirroredEyesIsTheirTextureCentreOffset()
    {
        var shift = EyeImageMatch.AxisShift(384, Eye(-1.43f, 1.19f), Eye(-1.19f, 1.43f));
        Assert.Equal(-35.2f, shift, 0.05f);
    }

    [Fact]
    public void MassRatioOfAFieldAgainstEightTenthsOfItIsTwoTenths()
    {
        var a = Noise(4);
        var b = new float[a.Length];
        for (int i = 0; i < a.Length; i++)
            b[i] = a[i] * 0.8f;
        Assert.Equal(0.2f, EyeImageMatch.MassRatio(a, b), 0.001f);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(-12)]
    public void BestShiftFindsTheOffset(int k)
    {
        var l = Noise(5);
        Assert.Equal(k, EyeImageMatch.BestShift(l, Offset(l, k), W, H, -20, 20));
    }

    [Fact]
    public void BestShiftRefusesARangeWithNoOverlap()
    {
        var l = Noise(6);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EyeImageMatch.BestShift(l, l, W, H, -W, 0)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() => EyeImageMatch.BestShift(l, l, W, H, 0, W));
    }

    [Fact]
    public void LumaBoxesDownByStep()
    {
        var rgba = Rgba(4, 2, 0);
        Paint(rgba, 4, 0, 0, 0); // first block: one red pixel of four
        rgba[(1 * 4 + 2) * 4] = 255; // second block: one white pixel of four
        rgba[(1 * 4 + 2) * 4 + 1] = 255;
        rgba[(1 * 4 + 2) * 4 + 2] = 255;
        var luma = EyeImageMatch.Luma(rgba, 4, 2, 2);
        Assert.Equal(2, luma.Length);
        Assert.Equal(200f * 0.2126f / 4f, luma[0], 0.01f);
        Assert.Equal(255f / 4f, luma[1], 0.01f);
    }

    [Fact]
    public void RmsIsTheRootMeanSquareDifference()
    {
        Assert.Equal(2f, EyeImageMatch.Rms(new[] { 1f, 5f }, new[] { 3f, 3f }), 0.001f);
    }

    [Fact]
    public void HoleEdgesReturnsTheHolesColumns()
    {
        // The flash is everywhere on the row but the hole, columns 40-59.
        const int width = 100;
        var off = Rgba(width, 3, 10);
        var on = Rgba(width, 3, 10);
        Paint(on, width, 1, 0, 39);
        Paint(on, width, 1, 60, 99);
        Assert.Equal((39, 60), EyeImageMatch.HoleEdges(off, on, width, 1, 50, 30));
    }

    [Fact]
    public void HoleEdgesIgnoresASingleChangedPixelInsideTheHole()
    {
        const int width = 100;
        var off = Rgba(width, 3, 10);
        var on = Rgba(width, 3, 10);
        Paint(on, width, 1, 0, 39);
        Paint(on, width, 1, 60, 99);
        Paint(on, width, 1, 45, 45);
        Paint(on, width, 1, 55, 55);
        Assert.Equal((39, 60), EyeImageMatch.HoleEdges(off, on, width, 1, 50, 30));
    }

    [Fact]
    public void HoleEdgesReturnsMinusOneOnIdenticalImages()
    {
        var image = Rgba(100, 3, 10);
        Assert.Equal((-1, -1), EyeImageMatch.HoleEdges(image, image, 100, 1, 50, 30));
    }

    [Fact]
    public void HoleEdgesReturnsMinusOneForASideWithNoEdge()
    {
        const int width = 100;
        var off = Rgba(width, 3, 10);
        var on = Rgba(width, 3, 10);
        Paint(on, width, 1, 70, 99);
        Assert.Equal((-1, 70), EyeImageMatch.HoleEdges(off, on, width, 1, 50, 30));
    }
}
