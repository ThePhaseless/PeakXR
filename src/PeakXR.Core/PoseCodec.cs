using System;
using System.Numerics;

namespace PeakXR.Core
{
    [Flags]
    public enum PoseFlags : byte
    {
        None = 0,
        Head = 1 << 0,
        LeftHand = 1 << 1,
        RightHand = 1 << 2,
        Fingers = 1 << 3,
        Seated = 1 << 4,
        VrActive = 1 << 5,
    }

    /// <summary>
    /// Networked avatar pose. Positions are relative to the character root in metres; rotations are
    /// relative to the character root too. Flags say which parts are present.
    /// </summary>
    public struct PosePacket
    {
        public PoseFlags Flags;
        public ushort Sequence;
        public Vector3 HeadPosition;
        public Quaternion HeadRotation;
        public Vector3 LeftHandPosition;
        public Quaternion LeftHandRotation;
        public Vector3 RightHandPosition;
        public Quaternion RightHandRotation;
        public FingerCurls LeftFingers;
        public FingerCurls RightFingers;

        public bool HasHead => (Flags & PoseFlags.Head) != 0;
        public bool HasLeftHand => (Flags & PoseFlags.LeftHand) != 0;
        public bool HasRightHand => (Flags & PoseFlags.RightHand) != 0;
        public bool HasFingers => (Flags & PoseFlags.Fingers) != 0;
        public bool Seated => (Flags & PoseFlags.Seated) != 0;
        public bool VrActive => (Flags & PoseFlags.VrActive) != 0;
    }

    /// <summary>
    /// Compact little-endian binary codec for <see cref="PosePacket"/>.
    /// Layout: version (1), flags (1), sequence (2), then only the parts present, in order:
    /// head, left hand, right hand (each: 3 x int16 millimetres + 4-byte smallest-three rotation),
    /// fingers (5 bytes left, 5 bytes right, 0..255). Full packet = 44 bytes.
    /// </summary>
    public static class PoseCodec
    {
        public const byte Version = 1;
        const int HeaderSize = 4;
        const int PartSize = 10;
        const int FingersSize = 2 * FingerCurls.Count;
        public const int MaxSize = HeaderSize + 3 * PartSize + FingersSize;

        /// <summary>Largest encodable position component, in metres.</summary>
        public const float MaxPosition = 32.767f;

        const float InvSqrt2 = 0.70710678f;
        const int RotBits = 10;
        const int RotMax = (1 << RotBits) - 1;

        /// <summary>Encodes into <paramref name="buffer"/> at index 0. Returns the length, or 0 if the buffer is too small.</summary>
        public static int Encode(in PosePacket p, byte[] buffer)
        {
            int size = SizeFor(p.Flags);
            if (buffer == null || buffer.Length < size)
                return 0;

            buffer[0] = Version;
            buffer[1] = (byte)p.Flags;
            WriteU16(buffer, 2, p.Sequence);
            int o = HeaderSize;
            if (p.HasHead)
                o = WritePart(buffer, o, p.HeadPosition, p.HeadRotation);
            if (p.HasLeftHand)
                o = WritePart(buffer, o, p.LeftHandPosition, p.LeftHandRotation);
            if (p.HasRightHand)
                o = WritePart(buffer, o, p.RightHandPosition, p.RightHandRotation);
            if (p.HasFingers)
            {
                o = WriteFingers(buffer, o, p.LeftFingers);
                o = WriteFingers(buffer, o, p.RightFingers);
            }
            return o;
        }

        /// <summary>
        /// Decodes the first <paramref name="length"/> bytes. Returns false for a null or short buffer or a
        /// different protocol version. Absent parts decode as zero position and identity rotation. Never throws.
        /// </summary>
        public static bool Decode(byte[] data, int length, out PosePacket p)
        {
            p = new PosePacket
            {
                HeadRotation = Quaternion.Identity,
                LeftHandRotation = Quaternion.Identity,
                RightHandRotation = Quaternion.Identity,
            };
            if (data == null || length < HeaderSize || length > data.Length || data[0] != Version)
                return false;

            var flags = (PoseFlags)data[1];
            if (length < SizeFor(flags))
                return false;

            p.Flags = flags;
            p.Sequence = ReadU16(data, 2);
            int o = HeaderSize;
            if (p.HasHead)
                o = ReadPart(data, o, out p.HeadPosition, out p.HeadRotation);
            if (p.HasLeftHand)
                o = ReadPart(data, o, out p.LeftHandPosition, out p.LeftHandRotation);
            if (p.HasRightHand)
                o = ReadPart(data, o, out p.RightHandPosition, out p.RightHandRotation);
            if (p.HasFingers)
            {
                o = ReadFingers(data, o, out p.LeftFingers);
                ReadFingers(data, o, out p.RightFingers);
            }
            return true;
        }

        /// <summary>True when sequence <paramref name="a"/> is newer than <paramref name="b"/>, allowing for wraparound.</summary>
        public static bool IsNewer(ushort a, ushort b) => (short)(a - b) > 0;

        /// <summary>Encoded size for a packet with these flags.</summary>
        public static int SizeFor(PoseFlags flags)
        {
            int size = HeaderSize;
            if ((flags & PoseFlags.Head) != 0) size += PartSize;
            if ((flags & PoseFlags.LeftHand) != 0) size += PartSize;
            if ((flags & PoseFlags.RightHand) != 0) size += PartSize;
            if ((flags & PoseFlags.Fingers) != 0) size += FingersSize;
            return size;
        }

        static int WritePart(byte[] b, int o, Vector3 pos, Quaternion rot)
        {
            WriteU16(b, o, (ushort)ToMillimetres(pos.X));
            WriteU16(b, o + 2, (ushort)ToMillimetres(pos.Y));
            WriteU16(b, o + 4, (ushort)ToMillimetres(pos.Z));
            WriteU32(b, o + 6, PackRotation(rot));
            return o + PartSize;
        }

        static int ReadPart(byte[] b, int o, out Vector3 pos, out Quaternion rot)
        {
            pos = new Vector3(
                (short)ReadU16(b, o) * 0.001f,
                (short)ReadU16(b, o + 2) * 0.001f,
                (short)ReadU16(b, o + 4) * 0.001f);
            rot = UnpackRotation(ReadU32(b, o + 6));
            return o + PartSize;
        }

        static int WriteFingers(byte[] b, int o, FingerCurls c)
        {
            for (int i = 0; i < FingerCurls.Count; i++)
                b[o + i] = (byte)(FingerCurlMapper.Clamp01(c[i]) * 255f + 0.5f);
            return o + FingerCurls.Count;
        }

        static int ReadFingers(byte[] b, int o, out FingerCurls c)
        {
            c = default;
            for (int i = 0; i < FingerCurls.Count; i++)
                c[i] = b[o + i] / 255f;
            return o + FingerCurls.Count;
        }

        static short ToMillimetres(float metres)
        {
            if (float.IsNaN(metres))
                return 0;
            float mm = MathF.Round(metres * 1000f);
            return (short)Math.Clamp(mm, -32767f, 32767f);
        }

        /// <summary>
        /// Smallest-three: 2 bits for the index of the largest component (dropped, made positive),
        /// then the other three in order, 10 bits each over [-1/sqrt2, 1/sqrt2].
        /// </summary>
        static uint PackRotation(Quaternion q)
        {
            float lenSq = q.LengthSquared();
            if (!(lenSq > 1e-12f) || float.IsInfinity(lenSq))
                q = Quaternion.Identity;
            else
                q = Quaternion.Normalize(q);

            float x = q.X, y = q.Y, z = q.Z, w = q.W;
            int largest = 0;
            float max = MathF.Abs(x);
            if (MathF.Abs(y) > max) { largest = 1; max = MathF.Abs(y); }
            if (MathF.Abs(z) > max) { largest = 2; max = MathF.Abs(z); }
            if (MathF.Abs(w) > max) { largest = 3; }

            float sign = Component(q, largest) < 0f ? -1f : 1f;
            float a, b, c;
            switch (largest)
            {
                case 0: a = y; b = z; c = w; break;
                case 1: a = x; b = z; c = w; break;
                case 2: a = x; b = y; c = w; break;
                default: a = x; b = y; c = z; break;
            }
            return ((uint)largest << 30)
                | (Quantize(a * sign) << 20)
                | (Quantize(b * sign) << 10)
                | Quantize(c * sign);
        }

        static Quaternion UnpackRotation(uint bits)
        {
            int largest = (int)(bits >> 30);
            float a = Dequantize((bits >> 20) & RotMax);
            float b = Dequantize((bits >> 10) & RotMax);
            float c = Dequantize(bits & RotMax);
            float d = MathF.Sqrt(MathF.Max(0f, 1f - (a * a + b * b + c * c)));
            Quaternion q;
            switch (largest)
            {
                case 0: q = new Quaternion(d, a, b, c); break;
                case 1: q = new Quaternion(a, d, b, c); break;
                case 2: q = new Quaternion(a, b, d, c); break;
                default: q = new Quaternion(a, b, c, d); break;
            }
            return Quaternion.Normalize(q);
        }

        static float Component(Quaternion q, int i) => i == 0 ? q.X : i == 1 ? q.Y : i == 2 ? q.Z : q.W;

        static uint Quantize(float v)
        {
            float t = (v / InvSqrt2 + 1f) * 0.5f * RotMax + 0.5f;
            return (uint)Math.Clamp((int)t, 0, RotMax);
        }

        static float Dequantize(uint u) => ((float)u / RotMax * 2f - 1f) * InvSqrt2;

        static void WriteU16(byte[] b, int o, ushort v)
        {
            b[o] = (byte)v;
            b[o + 1] = (byte)(v >> 8);
        }

        static ushort ReadU16(byte[] b, int o) => (ushort)(b[o] | (b[o + 1] << 8));

        static void WriteU32(byte[] b, int o, uint v)
        {
            b[o] = (byte)v;
            b[o + 1] = (byte)(v >> 8);
            b[o + 2] = (byte)(v >> 16);
            b[o + 3] = (byte)(v >> 24);
        }

        static uint ReadU32(byte[] b, int o) =>
            (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
    }
}
