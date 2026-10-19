using System.Linq;

namespace PeakXR.Core.Tests;

public class PoseFrameTests
{
    static PosePacket WorldPose() => new()
    {
        Flags = PoseFlags.Head | PoseFlags.LeftHand | PoseFlags.RightHand,
        HeadPosition = new Vector3(10.1f, 5.6f, -3f),
        HeadRotation = UnityQuat.Euler(10f, 80f, 5f),
        LeftHandPosition = new Vector3(9.8f, 5.1f, -2.7f),
        LeftHandRotation = UnityQuat.Euler(-30f, 45f, 0f),
        RightHandPosition = new Vector3(10.4f, 5.0f, -2.5f),
        RightHandRotation = UnityQuat.Euler(0f, 120f, 90f),
    };

    [Fact]
    public void ToLocal_ThenToWorld_RoundTrips()
    {
        var root = new Vector3(10f, 5f, -3f);
        var world = WorldPose();
        var local = PoseFrame.ToLocal(world, root, 73f);
        var back = PoseFrame.ToWorld(local, root, 73f);
        TestUtil.Near(world.HeadPosition, back.HeadPosition);
        TestUtil.Near(world.LeftHandPosition, back.LeftHandPosition);
        TestUtil.Near(world.RightHandPosition, back.RightHandPosition);
        TestUtil.SameRotation(world.HeadRotation, back.HeadRotation);
        TestUtil.SameRotation(world.RightHandRotation, back.RightHandRotation);
    }

    [Fact]
    public void ToLocal_UsesYawSoForwardHandIsPositiveZ()
    {
        // Character faces +X (yaw 90). A hand half a metre in front of the hip is local +Z.
        var root = new Vector3(1f, 2f, 3f);
        var world = new PosePacket { Flags = PoseFlags.RightHand, RightHandPosition = root + new Vector3(0.5f, 0f, 0f), RightHandRotation = UnityQuat.Euler(0f, 90f, 0f) };
        var local = PoseFrame.ToLocal(world, root, 90f);
        TestUtil.Near(new Vector3(0f, 0f, 0.5f), local.RightHandPosition);
        TestUtil.SameRotation(Quaternion.Identity, local.RightHandRotation);
    }

    [Fact]
    public void ToWorld_FollowsTheReceiversRoot()
    {
        // The same local pose lands relative to wherever the receiver currently sees the character.
        var local = new PosePacket { Flags = PoseFlags.LeftHand, LeftHandPosition = new Vector3(-0.2f, 0.3f, 0.4f), LeftHandRotation = Quaternion.Identity };
        var world = PoseFrame.ToWorld(local, new Vector3(100f, 0f, 0f), 180f);
        TestUtil.Near(new Vector3(100.2f, 0.3f, -0.4f), world.LeftHandPosition);
    }

    [Fact]
    public void KeepsFlagsSequenceAndFingers()
    {
        var p = WorldPose();
        p.Flags |= PoseFlags.Fingers | PoseFlags.Seated;
        p.Sequence = 77;
        p.LeftFingers = new FingerCurls(0.1f, 0.2f, 0.3f, 0.4f, 0.5f);
        var local = PoseFrame.ToLocal(p, Vector3.Zero, 30f);
        Assert.Equal(p.Flags, local.Flags);
        Assert.Equal(77, local.Sequence);
        Assert.Equal(0.3f, local.LeftFingers.Middle);
    }
}

public class PoseBlendTests
{
    [Fact]
    public void Lerp_InterpolatesPositionsRotationsAndFingers()
    {
        var a = new PosePacket { Flags = PoseFlags.Head | PoseFlags.Fingers, HeadPosition = Vector3.Zero, HeadRotation = Quaternion.Identity, LeftFingers = new FingerCurls(0, 0, 0, 0, 0) };
        var b = new PosePacket { Flags = PoseFlags.Head | PoseFlags.Fingers, HeadPosition = new Vector3(0, 0, 1), HeadRotation = UnityQuat.Euler(0, 90, 0), LeftFingers = new FingerCurls(1, 1, 1, 1, 1) };
        var m = PoseBlend.Lerp(a, b, 0.5f);
        TestUtil.Near(new Vector3(0, 0, 0.5f), m.HeadPosition);
        TestUtil.SameRotation(UnityQuat.Euler(0, 45, 0), m.HeadRotation, 0.01f);
        TestUtil.Near(0.5f, m.LeftFingers.Index);
    }

    [Fact]
    public void Lerp_PartPresentOnlyOnOneSide_UsesThatSide()
    {
        var a = new PosePacket { Flags = PoseFlags.Head, HeadRotation = Quaternion.Identity, RightHandRotation = Quaternion.Identity };
        var b = new PosePacket { Flags = PoseFlags.Head | PoseFlags.RightHand, HeadRotation = Quaternion.Identity, RightHandPosition = new Vector3(1, 1, 1), RightHandRotation = Quaternion.Identity };
        var early = PoseBlend.Lerp(a, b, 0.2f);
        Assert.False(early.HasRightHand);
        var late = PoseBlend.Lerp(a, b, 0.8f);
        Assert.True(late.HasRightHand);
        TestUtil.Near(new Vector3(1, 1, 1), late.RightHandPosition);
    }

    [Fact]
    public void Differs_IgnoresTinyChanges()
    {
        var a = new PosePacket { Flags = PoseFlags.RightHand, RightHandPosition = new Vector3(0, 1, 0), RightHandRotation = Quaternion.Identity };
        var b = a;
        b.RightHandPosition += new Vector3(0.001f, 0, 0);
        b.RightHandRotation = UnityQuat.Euler(0.2f, 0, 0);
        Assert.False(PoseBlend.Differs(a, b, 0.005f, 1f, 0.02f));
    }

    [Fact]
    public void Differs_DetectsMoveTurnFingersAndFlags()
    {
        var a = new PosePacket { Flags = PoseFlags.RightHand | PoseFlags.Fingers, RightHandPosition = new Vector3(0, 1, 0), RightHandRotation = Quaternion.Identity };
        var moved = a; moved.RightHandPosition += new Vector3(0.01f, 0, 0);
        var turned = a; turned.RightHandRotation = UnityQuat.Euler(0, 3, 0);
        var curled = a; curled.RightFingers.Index = 0.1f;
        var flags = a; flags.Flags |= PoseFlags.Head;
        Assert.True(PoseBlend.Differs(a, moved, 0.005f, 1f, 0.02f));
        Assert.True(PoseBlend.Differs(a, turned, 0.005f, 1f, 0.02f));
        Assert.True(PoseBlend.Differs(a, curled, 0.005f, 1f, 0.02f));
        Assert.True(PoseBlend.Differs(a, flags, 0.005f, 1f, 0.02f));
    }
}

public class PoseBufferTests
{
    static PosePacket At(ushort seq, float z) => new()
    {
        Flags = PoseFlags.Head,
        Sequence = seq,
        HeadPosition = new Vector3(0, 0, z),
        HeadRotation = Quaternion.Identity,
    };

    [Fact]
    public void Sample_EmptyBuffer_ReturnsFalse()
    {
        Assert.False(new PoseBuffer().Sample(1.0, out _));
    }

    [Fact]
    public void Sample_InterpolatesBehindByDelay()
    {
        var buf = new PoseBuffer { Delay = 0.1 };
        buf.Push(At(1, 0f), 1.00);
        buf.Push(At(2, 1f), 1.04);
        buf.Push(At(3, 2f), 1.08);
        Assert.True(buf.Sample(1.12, out var p)); // render time 1.02: halfway between seq 1 and 2
        TestUtil.Near(0.5f, p.HeadPosition.Z, 1e-3f);
    }

    [Fact]
    public void Sample_PastNewest_HoldsNewest()
    {
        var buf = new PoseBuffer { Delay = 0.1 };
        buf.Push(At(1, 0f), 1.00);
        buf.Push(At(2, 1f), 1.04);
        Assert.True(buf.Sample(5.0, out var p));
        TestUtil.Near(1f, p.HeadPosition.Z);
    }

    [Fact]
    public void Sample_BeforeOldest_HoldsOldest()
    {
        var buf = new PoseBuffer { Delay = 0.1 };
        buf.Push(At(1, 3f), 1.00);
        Assert.True(buf.Sample(1.0, out var p));
        TestUtil.Near(3f, p.HeadPosition.Z);
    }

    [Fact]
    public void Push_DropsOutOfOrderAndDuplicates()
    {
        var buf = new PoseBuffer();
        Assert.True(buf.Push(At(10, 0f), 1.00));
        Assert.False(buf.Push(At(9, 5f), 1.01));
        Assert.False(buf.Push(At(10, 5f), 1.02));
        Assert.True(buf.Push(At(11, 1f), 1.03));
        Assert.True(buf.Sample(10.0, out var p));
        TestUtil.Near(1f, p.HeadPosition.Z);
    }

    [Fact]
    public void Push_AcceptsSequenceWraparound()
    {
        var buf = new PoseBuffer();
        Assert.True(buf.Push(At(65535, 0f), 1.00));
        Assert.True(buf.Push(At(0, 1f), 1.04));
    }

    [Fact]
    public void Push_AfterLongSilence_AcceptsAnySequence()
    {
        // A peer that rejoins restarts its sequence; do not drop it for half the sequence range.
        var buf = new PoseBuffer();
        buf.Push(At(40000, 0f), 1.0);
        Assert.True(buf.Push(At(3, 7f), 1.0 + PoseBuffer.ResetAfter + 0.01));
        Assert.True(buf.Sample(100.0, out var p));
        TestUtil.Near(7f, p.HeadPosition.Z);
    }

    [Fact]
    public void Push_BunchedArrivals_AreSpreadSoInterpolationStaysSmooth()
    {
        // Two packets delivered in the same frame must not both get the same timestamp.
        var buf = new PoseBuffer { Delay = 0.1 };
        buf.Push(At(1, 0f), 1.00);
        buf.Push(At(2, 1f), 1.04);
        buf.Push(At(3, 2f), 1.12);
        buf.Push(At(4, 3f), 1.12);
        Assert.True(buf.Sample(1.22, out var a));
        Assert.True(buf.Sample(1.24, out var b));
        Assert.True(b.HeadPosition.Z > a.HeadPosition.Z);
    }

    [Fact]
    public void Push_AfterIdleGap_HoldsOldPoseUntilJustBeforeNewOne()
    {
        // A sender that stood still sends keepalives only; when it moves again the pose should not
        // crawl across the whole idle gap.
        var buf = new PoseBuffer { Delay = 0.1 };
        for (int i = 0; i < 5; i++)
            buf.Push(At((ushort)(i + 1), 0f), 1.00 + i * 0.04);
        buf.Push(At(6, 1f), 1.40);
        Assert.True(buf.Sample(1.40, out var mid)); // render time 1.30, well inside the idle gap
        TestUtil.Near(0f, mid.HeadPosition.Z, 1e-3f);
    }

    [Fact]
    public void Clear_Empties()
    {
        var buf = new PoseBuffer();
        buf.Push(At(1, 0f), 1.0);
        buf.Clear();
        Assert.Equal(0, buf.Count);
        Assert.False(buf.Sample(2.0, out _));
    }

    [Fact]
    public void ManyPushes_KeepBoundedSize()
    {
        var buf = new PoseBuffer { Delay = 0.1 };
        for (int i = 0; i < 1000; i++)
        {
            buf.Push(At((ushort)i, i), i * 0.04);
            buf.Sample(i * 0.04, out _);
        }
        Assert.True(buf.Count <= PoseBuffer.Capacity);
    }
}

public class TokenBucketTests
{
    [Fact]
    public void AllowsBurstThenRefillRate()
    {
        var bucket = new TokenBucket(3f, 10f);
        Assert.True(bucket.TryTake(0.0));
        Assert.True(bucket.TryTake(0.0));
        Assert.True(bucket.TryTake(0.0));
        Assert.False(bucket.TryTake(0.0));
        Assert.False(bucket.TryTake(0.05));
        Assert.True(bucket.TryTake(0.1));
        Assert.False(bucket.TryTake(0.1));
    }

    [Fact]
    public void RefillCapsAtCapacity()
    {
        var bucket = new TokenBucket(2f, 10f);
        bucket.TryTake(0.0);
        int taken = 0;
        for (int i = 0; i < 10; i++)
            if (bucket.TryTake(100.0)) taken++;
        Assert.Equal(2, taken);
    }
}

public class PoseSendGateTests
{
    static PosePacket Hand(float x) => new() { Flags = PoseFlags.RightHand, RightHandPosition = new Vector3(x, 0, 0), RightHandRotation = Quaternion.Identity };

    [Fact]
    public void FirstPose_Sends()
    {
        var gate = new PoseSendGate { Rate = 20f };
        Assert.True(gate.ShouldSend(0.0, Hand(0)));
    }

    [Fact]
    public void RespectsRate()
    {
        var gate = new PoseSendGate { Rate = 20f };
        gate.MarkSent(0.0, Hand(0));
        Assert.False(gate.ShouldSend(0.02, Hand(1)));
        Assert.True(gate.ShouldSend(0.05, Hand(1)));
    }

    [Fact]
    public void AverageRateMatchesSettingAt90Fps()
    {
        var gate = new PoseSendGate { Rate = 25f };
        int sent = 0;
        for (int frame = 0; frame < 900; frame++)
        {
            double t = frame / 90.0;
            var p = Hand(frame);
            if (gate.ShouldSend(t, p)) { gate.MarkSent(t, p); sent++; }
        }
        Assert.InRange(sent, 245, 255);
    }

    [Fact]
    public void UnchangedPose_SendsOnlyKeepAlive()
    {
        var gate = new PoseSendGate { Rate = 30f, KeepAlive = 0.25f };
        gate.MarkSent(0.0, Hand(0));
        Assert.False(gate.ShouldSend(0.1, Hand(0)));
        Assert.False(gate.ShouldSend(0.2, Hand(0)));
        Assert.True(gate.ShouldSend(0.26, Hand(0)));
    }

    [Fact]
    public void KeepAlive_IsShorterThanDriverStaleTime()
    {
        Assert.True(new PoseSendGate().KeepAlive < 0.5f);
    }

    [Fact]
    public void Reset_SendsImmediately()
    {
        var gate = new PoseSendGate { Rate = 20f };
        gate.MarkSent(0.0, Hand(0));
        gate.Reset();
        Assert.True(gate.ShouldSend(0.001, Hand(0)));
    }
}

public class HandshakeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EncodeParse_RoundTrips(bool vr)
    {
        object value = Handshake.Encode(vr);
        Assert.True(Handshake.TryParse(value, out int protocol, out bool parsedVr));
        Assert.Equal(Handshake.Protocol, protocol);
        Assert.Equal(vr, parsedVr);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("1")]
    [InlineData(1.5f)]
    [InlineData(0)]
    [InlineData(-5)]
    public void TryParse_RejectsGarbage(object value)
    {
        Assert.False(Handshake.TryParse(value, out _, out _));
    }

    [Fact]
    public void ProtocolMatchesCodecVersion()
    {
        Assert.Equal((int)PoseCodec.Version, Handshake.Protocol);
    }
}

public class PeerTableTests
{
    [Fact]
    public void TracksCompatiblePeersAsTargets()
    {
        var peers = new PeerTable();
        Assert.True(peers.Set(5, Handshake.Encode(true)));
        Assert.True(peers.Set(7, Handshake.Encode(false)));
        Assert.Equal(2, peers.Count);
        Assert.Equal(new[] { 5, 7 }, peers.Targets.OrderBy(x => x));
        Assert.True(peers.IsVr(5));
        Assert.False(peers.IsVr(7));
        Assert.True(peers.IsPeer(7));
    }

    [Fact]
    public void VanillaOrIncompatible_IsNotAPeer()
    {
        var peers = new PeerTable();
        Assert.False(peers.Set(3, null));
        Assert.False(peers.Set(4, Handshake.Protocol + 1));
        Assert.Equal(0, peers.Count);
        Assert.Empty(peers.Targets);
        Assert.False(peers.IsPeer(3));
    }

    [Fact]
    public void Set_SameValue_ReportsNoChange()
    {
        var peers = new PeerTable();
        peers.Set(5, Handshake.Encode(true));
        var targets = peers.Targets;
        Assert.False(peers.Set(5, Handshake.Encode(true)));
        Assert.Same(targets, peers.Targets);
        Assert.True(peers.Set(5, Handshake.Encode(false)));
    }

    [Fact]
    public void Set_PropertyCleared_RemovesPeer()
    {
        var peers = new PeerTable();
        peers.Set(5, Handshake.Encode(true));
        Assert.True(peers.Set(5, null));
        Assert.Equal(0, peers.Count);
    }

    [Fact]
    public void RemoveAndClear()
    {
        var peers = new PeerTable();
        peers.Set(5, Handshake.Encode(true));
        peers.Set(6, Handshake.Encode(true));
        Assert.True(peers.Remove(5));
        Assert.False(peers.Remove(5));
        Assert.Equal(new[] { 6 }, peers.Targets);
        peers.Clear();
        Assert.Empty(peers.Targets);
    }
}

public class RemotePoseStreamTests
{
    static byte[] Encoded(ushort seq, Vector3 hand)
    {
        var p = new PosePacket { Flags = PoseFlags.RightHand | PoseFlags.VrActive, Sequence = seq, RightHandPosition = hand, RightHandRotation = Quaternion.Identity };
        var buf = new byte[PoseCodec.MaxSize];
        int len = PoseCodec.Encode(p, buf);
        Array.Resize(ref buf, len);
        return buf;
    }

    [Fact]
    public void Accepts_ValidPacket()
    {
        var s = new RemotePoseStream();
        var data = Encoded(1, new Vector3(0.3f, 0.4f, 0.5f));
        Assert.Equal(PoseAccept.Accepted, s.Accept(data, data.Length, 1.0));
        Assert.True(s.Buffer.Sample(2.0, out var p));
        TestUtil.Near(new Vector3(0.3f, 0.4f, 0.5f), p.RightHandPosition, 0.002f);
        Assert.Equal(1.0, s.LastAcceptTime);
    }

    [Fact]
    public void Rejects_Malformed()
    {
        var s = new RemotePoseStream();
        Assert.Equal(PoseAccept.Malformed, s.Accept(null, 0, 1.0));
        Assert.Equal(PoseAccept.Malformed, s.Accept(new byte[] { 99, 0, 0, 0 }, 4, 1.1));
    }

    [Fact]
    public void Rejects_ImplausibleReach()
    {
        var s = new RemotePoseStream();
        var data = Encoded(1, new Vector3(0f, 0f, 20f));
        Assert.Equal(PoseAccept.Malformed, s.Accept(data, data.Length, 1.0));
    }

    [Fact]
    public void Rejects_OutOfOrder()
    {
        var s = new RemotePoseStream();
        var a = Encoded(5, Vector3.Zero);
        var b = Encoded(4, Vector3.Zero);
        Assert.Equal(PoseAccept.Accepted, s.Accept(a, a.Length, 1.0));
        Assert.Equal(PoseAccept.OutOfOrder, s.Accept(b, b.Length, 1.01));
    }

    [Fact]
    public void RateLimits_Flood()
    {
        var s = new RemotePoseStream();
        int accepted = 0, limited = 0;
        for (int i = 0; i < 1000; i++)
        {
            var d = Encoded((ushort)(i + 1), Vector3.Zero);
            var r = s.Accept(d, d.Length, 1.0 + i * 0.001); // 1000 packets in one second
            if (r == PoseAccept.Accepted) accepted++;
            if (r == PoseAccept.RateLimited) limited++;
        }
        Assert.InRange(accepted, 1, RemotePoseStream.MaxPacketsPerSecond + RemotePoseStream.Burst + 1);
        Assert.True(limited > 900);
    }

    [Fact]
    public void RandomBytes_NeverThrow()
    {
        var rng = new Random(5);
        var s = new RemotePoseStream();
        for (int i = 0; i < 2000; i++)
        {
            var d = new byte[rng.Next(0, 80)];
            rng.NextBytes(d);
            if (d.Length > 0) d[0] = PoseCodec.Version;
            s.Accept(d, rng.Next(-5, d.Length + 5), i * 0.05);
        }
    }
}
