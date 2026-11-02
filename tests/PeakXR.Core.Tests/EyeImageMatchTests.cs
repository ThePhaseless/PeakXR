using System.Collections.Generic;
using System.IO;

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

    /// <summary>Summed RGB change per pixel as images: off all zero, on carrying the change across its channels.</summary>
    private static (byte[] Off, byte[] On) Images(int[][] rows)
    {
        int w = rows[0].Length;
        var on = new byte[rows.Length * w * 4];
        for (int y = 0; y < rows.Length; y++)
        for (int x = 0; x < w; x++)
        {
            int left = rows[y][x];
            for (int c = 0; c < 3; c++)
            {
                int part = Math.Min(left, 255);
                on[(y * w + x) * 4 + c] = (byte)part;
                left -= part;
            }
        }
        return (new byte[on.Length], on);
    }

    private static (int Left, int Right) Hole(int[][] rows)
    {
        var (off, on) = Images(rows);
        var profile = EyeImageMatch.ChangeProfile(
            off,
            on,
            rows[0].Length,
            0,
            rows.Length - 1,
            0,
            rows[0].Length - 1
        );
        return EyeImageMatch.HoleInProfile(profile, EyeImageMatch.MinHoleContrast);
    }

    /// <summary>The quad's change is everywhere but the hole, columns 40-59 unless said otherwise.</summary>
    private static int[][] QuadWithHole(int rows, int change, int holeFrom = 40, int holeTo = 59)
    {
        var field = new int[rows][];
        for (int y = 0; y < rows; y++)
        {
            field[y] = new int[100];
            for (int x = 0; x < 100; x++)
                field[y][x] = x >= holeFrom && x <= holeTo ? 0 : change;
        }
        return field;
    }

    [Fact]
    public void HoleInProfileFindsTheColumnsOfTheHole()
    {
        Assert.Equal((40, 59), Hole(QuadWithHole(5, 300)));
    }

    [Fact]
    public void AHoleBesideTheHeadsAxisIsFound()
    {
        // Today's quad: the hole sits beside the head's axis, which is outside it.
        Assert.Equal((70, 89), Hole(QuadWithHole(5, 300, 70, 89)));
    }

    [Fact]
    public void AHaloInsideTheHoleIsBelowTheThreshold()
    {
        // Bloom from the quad: change rising toward the hole's edge, far under the quad's own.
        var rows = QuadWithHole(5, 340);
        foreach (var row in rows)
            for (int x = 40; x <= 59; x++)
                row[x] = 3 + (int)(Math.Pow(Math.Abs(x - 49.5) / 10.0, 3) * 37);
        Assert.Equal((40, 59), Hole(rows));
    }

    [Fact]
    public void ARowWithFalseEdgesDoesNotMoveTheMedian()
    {
        // Scene detail on one of five rows: it breaks that row's hole in two, as the first run's single row did.
        var rows = QuadWithHole(5, 340);
        for (int x = 45; x <= 48; x++)
            rows[2][x] = 300;
        // And a stretch of scene like the quad's own colour beside the hole, on another row.
        for (int x = 60; x <= 69; x++)
            rows[1][x] = 0;
        Assert.Equal((40, 59), Hole(rows));
    }

    [Fact]
    public void NoHoleWhenTheQuadDidNotDraw()
    {
        var image = Rgba(100, 3, 10);
        var profile = EyeImageMatch.ChangeProfile(image, image, 100, 0, 2, 0, 99);
        Assert.Equal((-1, -1), EyeImageMatch.HoleInProfile(profile, EyeImageMatch.MinHoleContrast));
    }

    [Fact]
    public void NoHoleWhenTheQuadCoversTheWholeRow()
    {
        var rows = QuadWithHole(5, 300, 200, 201);
        Assert.Equal((-1, -1), Hole(rows));
    }

    [Theory]
    [InlineData(0, 40)]
    [InlineData(60, 99)]
    public void NoHoleWhenItRunsOffTheImage(int from, int to)
    {
        // Only one edge in view says nothing about the centre.
        Assert.Equal((-1, -1), Hole(QuadWithHole(5, 300, from, to)));
    }

    [Fact]
    public void ChangeProfileReadsOnlyTheViewportsColumns()
    {
        // A target wider than its viewport (dynamic resolution): the unused columns must not read as a hole.
        const int width = 100;
        var off = Rgba(width, 3, 10);
        var on = Rgba(width, 3, 10);
        for (int y = 0; y < 3; y++)
        {
            Paint(on, width, y, 0, 29);
            Paint(on, width, y, 50, 79);
        }
        var profile = EyeImageMatch.ChangeProfile(off, on, width, 0, 2, 0, 79);
        Assert.Equal(80, profile.Length);
        Assert.Equal((30, 49), EyeImageMatch.HoleInProfile(profile, EyeImageMatch.MinHoleContrast));
    }

    /// <summary>tests/PeakXR.Core.Tests/Fixtures/eye-hole-rows.txt, written from the saved eye images of vr.480's first
    /// in-game run.</summary>
    private static Dictionary<string, int[][]> MeasuredRows()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PeakXR.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var cases = new Dictionary<string, int[][]>();
        var rows = new List<int[]>();
        string current = null;
        foreach (
            var line in File.ReadLines(
                Path.Combine(
                    dir.FullName,
                    "tests",
                    "PeakXR.Core.Tests",
                    "Fixtures",
                    "eye-hole-rows.txt"
                )
            )
        )
        {
            if (line[0] == '#')
                continue;
            if (char.IsLetter(line[0]))
            {
                var name = line.Split(' ');
                rows = new List<int[]>();
                cases[name[0] + " " + name[1]] = null;
                current = name[0] + " " + name[1];
                continue;
            }
            rows.Add(Array.ConvertAll(line.Split(' '), int.Parse));
            cases[current] = rows.ToArray();
        }
        return cases;
    }

    // The frustums of the run the rows come from; the hole is 0.5 tan in radius on any quad.
    [Theory]
    [InlineData("off left", -1.43f, 1.19f, 0.528f)]
    [InlineData("off right", -1.19f, 1.43f, -0.519f)]
    [InlineData("on left", -1.43f, 1.19f, -0.004f)]
    [InlineData("on right", -1.19f, 1.43f, -0.008f)]
    public void MeasuredRowsGiveTheHoleInEachEye(
        string which,
        float left,
        float right,
        float centre
    )
    {
        var rows = MeasuredRows()[which];
        var (a, b) = Hole(rows);
        Assert.True(a > 0, $"no hole: {a}-{b}");
        float perColumn = (right - left) / rows[0].Length;
        float found = left + ((a + b) * 0.5f + 0.5f) * perColumn;
        Assert.Equal(centre, found, 0.004f);
        Assert.InRange((b - a + 1) * perColumn, EyeImageMatch.MinHoleTan, EyeImageMatch.MaxHoleTan);
    }

    [Fact]
    public void MeasuredRowsKeepTheEyesVerdicts()
    {
        var measured = MeasuredRows();
        float Centre(string which, float left, float right)
        {
            var rows = measured[which];
            var (a, b) = Hole(rows);
            return left + ((a + b) * 0.5f + 0.5f) * (right - left) / rows[0].Length;
        }
        float off = Centre("off left", -1.43f, 1.19f) - Centre("off right", -1.19f, 1.43f);
        float on = Centre("on left", -1.43f, 1.19f) - Centre("on right", -1.19f, 1.43f);
        // 0.0315 m each side on a quad 0.06 m away; the step's bounds are 0.63 and 0.01.
        Assert.Equal(1.05f, off, 0.02f);
        Assert.InRange(MathF.Abs(on), 0f, 0.01f);
    }

    private static float[] Texture(int w, int h, int seed, float amplitude)
    {
        var random = new Random(seed);
        var field = new float[w * h];
        for (int i = 0; i < field.Length; i++)
            field[i] = 100f + (float)(random.NextDouble() * 2.0 - 1.0) * amplitude;
        return field;
    }

    /// <summary>Adds to one square of tiles what the other eye's image would: real parallax against a shifted image
    /// reads about 1.6 of 255 in a typical tile of the saved pairs.</summary>
    private static float[] WithStale(
        float[] field,
        int w,
        int fromX,
        int fromY,
        int size,
        float amplitude
    )
    {
        var stale = (float[])field.Clone();
        var noise = Texture(w, field.Length / w, 77, amplitude);
        for (int y = fromY; y < fromY + size; y++)
        for (int x = fromX; x < fromX + size; x++)
            stale[y * w + x] += noise[y * w + x] - 100f;
        return stale;
    }

    private const int EyeW = 269;
    private const int EyeH = 301;

    private static EyeSwapReading Swap(float[] swapped, float[] normal, float[] later) =>
        EyeImageMatch.ReadSwap(swapped, normal, later, EyeW, EyeH, 0f, 0f);

    [Fact]
    public void AQuietSwapPassesAgainstItsOwnNoise()
    {
        var normal = Texture(EyeW, EyeH, 1, 30f);
        var swapped = (float[])normal.Clone();
        for (int i = 0; i < swapped.Length; i += 7)
            swapped[i] += 0.3f;
        var reading = Swap(swapped, normal, normal);
        Assert.True(reading.Matches, reading.ToString());
    }

    [Fact]
    public void AStaleTextureInASmallPatchFailsThoughTheWholeEyeRmsIsSmall()
    {
        // One tile of 64 px, 0.3 % of the eye, wrong by about what the other eye's image would be.
        var normal = Texture(EyeW, EyeH, 2, 30f);
        var swapped = WithStale(normal, EyeW, 96, 160, 16, 3.5f);
        var reading = Swap(swapped, normal, normal);
        Assert.InRange(reading.Rms, 0f, EyeImageMatch.SwapRmsFloor);
        Assert.Equal(1, reading.TilesPastNoise);
        Assert.False(reading.Matches);
    }

    [Fact]
    public void AStaleTextureOverTheWholeEyeFails()
    {
        var normal = Texture(EyeW, EyeH, 3, 30f);
        var swapped = Texture(EyeW, EyeH, 4, 30f);
        var reading = Swap(swapped, normal, normal);
        Assert.False(reading.Matches);
        Assert.True(reading.Rms > EyeImageMatch.SwapRmsCap);
    }

    [Fact]
    public void SpecklesPastTheChannelStepFailOnShareAlone()
    {
        var normal = Texture(EyeW, EyeH, 5, 30f);
        var reading = EyeImageMatch.ReadSwap(normal, normal, normal, EyeW, EyeH, 0.001f, 0f);
        Assert.False(reading.Matches);
    }

    [Fact]
    public void WhatMovesAnywayIsExcusedOnlyInItsOwnTile()
    {
        // Something still moving: the same tile changes between the two normal reads as well.
        var normal = Texture(EyeW, EyeH, 6, 30f);
        var later = WithStale(normal, EyeW, 16, 16, 16, 3.5f);
        var swapped = WithStale(normal, EyeW, 16, 16, 16, 3.5f);
        Assert.True(Swap(swapped, normal, later).Matches);

        // It does not excuse a stale patch elsewhere.
        var stale = WithStale(swapped, EyeW, 160, 160, 16, 3.5f);
        var reading = Swap(stale, normal, later);
        Assert.Equal(1, reading.TilesPastNoise);
        Assert.False(reading.Matches);
    }

    [Fact]
    public void ANoisyFrameCannotRaiseTheLimitsPastTheirCaps()
    {
        var normal = Texture(EyeW, EyeH, 7, 30f);
        var later = (float[])normal.Clone();
        var swapped = (float[])normal.Clone();
        for (int i = 0; i < later.Length; i++)
        {
            later[i] += i % 2 == 0 ? 0.9f : -0.9f;
            swapped[i] += i % 2 == 0 ? 1.4f : -1.4f;
        }
        var reading = Swap(swapped, normal, later);
        Assert.Equal(0, reading.TilesPastNoise);
        Assert.True(reading.StillRms > EyeImageMatch.SwapStillLimit);
        Assert.True(reading.Rms > EyeImageMatch.SwapRmsCap);
        Assert.False(reading.Matches);
    }

    [Fact]
    public void ReadSwapRefusesFieldsOfDifferentSizes()
    {
        var normal = Texture(EyeW, EyeH, 8, 30f);
        Assert.Throws<ArgumentException>(() =>
            EyeImageMatch.ReadSwap(new float[10], normal, normal, EyeW, EyeH, 0f, 0f)
        );
    }
}
