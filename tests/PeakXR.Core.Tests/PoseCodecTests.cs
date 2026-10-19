namespace PeakXR.Core.Tests;

public class PoseCodecTests
{
    static PosePacket Full(Random rng)
    {
        return new PosePacket
        {
            Flags =
                PoseFlags.Head
                | PoseFlags.LeftHand
                | PoseFlags.RightHand
                | PoseFlags.Fingers
                | PoseFlags.VrActive,
            Sequence = (ushort)rng.Next(0, 65536),
            HeadPosition = RandPos(rng, 2f),
            HeadRotation = RandRot(rng),
            LeftHandPosition = RandPos(rng, 2f),
            LeftHandRotation = RandRot(rng),
            RightHandPosition = RandPos(rng, 2f),
            RightHandRotation = RandRot(rng),
            LeftFingers = new FingerCurls(
                rng.NextSingle(),
                rng.NextSingle(),
                rng.NextSingle(),
                rng.NextSingle(),
                rng.NextSingle()
            ),
            RightFingers = new FingerCurls(
                rng.NextSingle(),
                rng.NextSingle(),
                rng.NextSingle(),
                rng.NextSingle(),
                rng.NextSingle()
            ),
        };
    }

    static Vector3 RandPos(Random rng, float range) =>
        new(
            (rng.NextSingle() * 2 - 1) * range,
            (rng.NextSingle() * 2 - 1) * range,
            (rng.NextSingle() * 2 - 1) * range
        );

    static Quaternion RandRot(Random rng) =>
        Quaternion.Normalize(
            new Quaternion(
                rng.NextSingle() * 2 - 1,
                rng.NextSingle() * 2 - 1,
                rng.NextSingle() * 2 - 1,
                rng.NextSingle() * 2 - 1
            )
        );

    [Fact]
    public void FullPacket_FitsIn48Bytes()
    {
        Assert.True(PoseCodec.MaxSize <= 48);
        var buf = new byte[PoseCodec.MaxSize];
        int len = PoseCodec.Encode(Full(new Random(3)), buf);
        Assert.Equal(PoseCodec.MaxSize, len);
    }

    [Fact]
    public void RoundTrip_WithinErrorBounds()
    {
        var rng = new Random(7);
        var buf = new byte[PoseCodec.MaxSize];
        float worstPos = 0f,
            worstRot = 0f,
            worstFinger = 0f;
        for (int i = 0; i < 1000; i++)
        {
            var p = Full(rng);
            int len = PoseCodec.Encode(p, buf);
            Assert.True(PoseCodec.Decode(buf, len, out var d));
            Assert.Equal(p.Flags, d.Flags);
            Assert.Equal(p.Sequence, d.Sequence);
            worstPos = MathF.Max(worstPos, Vector3.Distance(p.HeadPosition, d.HeadPosition));
            worstPos = MathF.Max(
                worstPos,
                Vector3.Distance(p.LeftHandPosition, d.LeftHandPosition)
            );
            worstPos = MathF.Max(
                worstPos,
                Vector3.Distance(p.RightHandPosition, d.RightHandPosition)
            );
            worstRot = MathF.Max(worstRot, TestUtil.AngleDeg(p.HeadRotation, d.HeadRotation));
            worstRot = MathF.Max(
                worstRot,
                TestUtil.AngleDeg(p.LeftHandRotation, d.LeftHandRotation)
            );
            worstRot = MathF.Max(
                worstRot,
                TestUtil.AngleDeg(p.RightHandRotation, d.RightHandRotation)
            );
            for (int f = 0; f < FingerCurls.Count; f++)
            {
                worstFinger = MathF.Max(
                    worstFinger,
                    MathF.Abs(p.LeftFingers[f] - d.LeftFingers[f])
                );
                worstFinger = MathF.Max(
                    worstFinger,
                    MathF.Abs(p.RightFingers[f] - d.RightFingers[f])
                );
            }
            TestUtil.Near(1f, d.HeadRotation.Length(), 1e-4f);
        }
        Assert.True(worstPos <= 0.0015f, $"pos error {worstPos}");
        Assert.True(worstRot <= 0.5f, $"rot error {worstRot}");
        Assert.True(worstFinger <= 0.5f / 255f + 1e-6f, $"finger error {worstFinger}");
    }

    [Fact]
    public void Positions_ClampTo32Metres()
    {
        var p = new PosePacket
        {
            Flags = PoseFlags.Head,
            HeadPosition = new Vector3(100f, -100f, 5f),
            HeadRotation = Quaternion.Identity,
        };
        var buf = new byte[PoseCodec.MaxSize];
        int len = PoseCodec.Encode(p, buf);
        Assert.True(PoseCodec.Decode(buf, len, out var d));
        TestUtil.Near(new Vector3(32.767f, -32.767f, 5f), d.HeadPosition, 0.002f);
    }

    [Fact]
    public void AbsentParts_AreOmittedAndDecodeAsDefaults()
    {
        var p = new PosePacket
        {
            Flags = PoseFlags.Head | PoseFlags.Seated,
            Sequence = 9,
            HeadPosition = new Vector3(0, 1.6f, 0),
            HeadRotation = Quaternion.Identity,
        };
        var buf = new byte[PoseCodec.MaxSize];
        int len = PoseCodec.Encode(p, buf);
        Assert.Equal(4 + 10, len);
        Assert.True(PoseCodec.Decode(buf, len, out var d));
        Assert.True(d.Seated);
        Assert.False(d.HasLeftHand);
        Assert.Equal(Quaternion.Identity, d.LeftHandRotation);
        TestUtil.Near(new Vector3(0, 1.6f, 0), d.HeadPosition, 0.001f);
    }

    [Fact]
    public void Encode_TooSmallBuffer_ReturnsZero()
    {
        Assert.Equal(0, PoseCodec.Encode(Full(new Random(1)), new byte[10]));
        Assert.Equal(0, PoseCodec.Encode(Full(new Random(1)), null));
    }

    [Fact]
    public void Decode_RejectsWrongVersion()
    {
        var buf = new byte[PoseCodec.MaxSize];
        int len = PoseCodec.Encode(Full(new Random(2)), buf);
        buf[0] = (byte)(PoseCodec.Version + 1);
        Assert.False(PoseCodec.Decode(buf, len, out _));
    }

    [Fact]
    public void Decode_RejectsTruncatedOrNull()
    {
        var buf = new byte[PoseCodec.MaxSize];
        int len = PoseCodec.Encode(Full(new Random(2)), buf);
        Assert.False(PoseCodec.Decode(buf, len - 1, out _));
        Assert.False(PoseCodec.Decode(buf, 3, out _));
        Assert.False(PoseCodec.Decode(null, 10, out _));
        Assert.False(PoseCodec.Decode(buf, buf.Length + 5, out _));
    }

    [Fact]
    public void Decode_RandomBuffers_NeverThrowAndGiveUnitRotations()
    {
        var rng = new Random(11);
        for (int i = 0; i < 500; i++)
        {
            var buf = new byte[rng.Next(0, 64)];
            rng.NextBytes(buf);
            if (buf.Length > 0 && rng.Next(2) == 0)
                buf[0] = PoseCodec.Version;
            if (PoseCodec.Decode(buf, buf.Length, out var d))
            {
                Assert.False(float.IsNaN(d.HeadRotation.W));
                TestUtil.Near(1f, d.HeadRotation.Length(), 1e-3f);
                TestUtil.Near(1f, d.RightHandRotation.Length(), 1e-3f);
            }
        }
    }

    [Theory]
    [InlineData(1, 0, true)]
    [InlineData(0, 1, false)]
    [InlineData(5, 5, false)]
    [InlineData(0, 65535, true)]
    [InlineData(10, 65000, true)]
    [InlineData(65000, 10, false)]
    [InlineData(32767, 0, true)]
    [InlineData(32768, 0, false)]
    public void IsNewer_HandlesWraparound(int a, int b, bool expected)
    {
        Assert.Equal(expected, PoseCodec.IsNewer((ushort)a, (ushort)b));
    }
}
