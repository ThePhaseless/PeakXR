using System.Linq;

namespace PeakXR.Core.Tests;

public class PoseFrameTests
{
    static PosePacket WorldPose() =>
        new()
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
        var world = new PosePacket
        {
            Flags = PoseFlags.RightHand,
            RightHandPosition = root + new Vector3(0.5f, 0f, 0f),
            RightHandRotation = UnityQuat.Euler(0f, 90f, 0f),
        };
        var local = PoseFrame.ToLocal(world, root, 90f);
        TestUtil.Near(new Vector3(0f, 0f, 0.5f), local.RightHandPosition);
        TestUtil.SameRotation(Quaternion.Identity, local.RightHandRotation);
    }

    [Fact]
    public void ToWorld_FollowsTheReceiversRoot()
    {
        // The same local pose lands relative to wherever the receiver currently sees the character.
        var local = new PosePacket
        {
            Flags = PoseFlags.LeftHand,
            LeftHandPosition = new Vector3(-0.2f, 0.3f, 0.4f),
            LeftHandRotation = Quaternion.Identity,
        };
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
        var a = new PosePacket
        {
            Flags = PoseFlags.Head | PoseFlags.Fingers,
            HeadPosition = Vector3.Zero,
            HeadRotation = Quaternion.Identity,
            LeftFingers = new FingerCurls(0, 0, 0, 0, 0),
        };
        var b = new PosePacket
        {
            Flags = PoseFlags.Head | PoseFlags.Fingers,
            HeadPosition = new Vector3(0, 0, 1),
            HeadRotation = UnityQuat.Euler(0, 90, 0),
            LeftFingers = new FingerCurls(1, 1, 1, 1, 1),
        };
        var m = PoseBlend.Lerp(a, b, 0.5f);
        TestUtil.Near(new Vector3(0, 0, 0.5f), m.HeadPosition);
        TestUtil.SameRotation(UnityQuat.Euler(0, 45, 0), m.HeadRotation, 0.01f);
        TestUtil.Near(0.5f, m.LeftFingers.Index);
    }

    [Fact]
    public void Lerp_PartPresentOnlyOnOneSide_UsesThatSide()
    {
        var a = new PosePacket
        {
            Flags = PoseFlags.Head,
            HeadRotation = Quaternion.Identity,
            RightHandRotation = Quaternion.Identity,
        };
        var b = new PosePacket
        {
            Flags = PoseFlags.Head | PoseFlags.RightHand,
            HeadRotation = Quaternion.Identity,
            RightHandPosition = new Vector3(1, 1, 1),
            RightHandRotation = Quaternion.Identity,
        };
        var early = PoseBlend.Lerp(a, b, 0.2f);
        Assert.False(early.HasRightHand);
        var late = PoseBlend.Lerp(a, b, 0.8f);
        Assert.True(late.HasRightHand);
        TestUtil.Near(new Vector3(1, 1, 1), late.RightHandPosition);
    }

    [Fact]
    public void Differs_IgnoresTinyChanges()
    {
        var a = new PosePacket
        {
            Flags = PoseFlags.RightHand,
            RightHandPosition = new Vector3(0, 1, 0),
            RightHandRotation = Quaternion.Identity,
        };
        var b = a;
        b.RightHandPosition += new Vector3(0.001f, 0, 0);
        b.RightHandRotation = UnityQuat.Euler(0.2f, 0, 0);
        Assert.False(PoseBlend.Differs(a, b, 0.005f, 1f, 0.02f));
    }

    [Fact]
    public void Differs_DetectsMoveTurnFingersAndFlags()
    {
        var a = new PosePacket
        {
            Flags = PoseFlags.RightHand | PoseFlags.Fingers,
            RightHandPosition = new Vector3(0, 1, 0),
            RightHandRotation = Quaternion.Identity,
        };
        var moved = a;
        moved.RightHandPosition += new Vector3(0.01f, 0, 0);
        var turned = a;
        turned.RightHandRotation = UnityQuat.Euler(0, 3, 0);
        var curled = a;
        curled.RightFingers.Index = 0.1f;
        var flags = a;
        flags.Flags |= PoseFlags.Head;
        Assert.True(PoseBlend.Differs(a, moved, 0.005f, 1f, 0.02f));
        Assert.True(PoseBlend.Differs(a, turned, 0.005f, 1f, 0.02f));
        Assert.True(PoseBlend.Differs(a, curled, 0.005f, 1f, 0.02f));
        Assert.True(PoseBlend.Differs(a, flags, 0.005f, 1f, 0.02f));
    }
}

public class PoseBufferTests
{
    static PosePacket At(ushort seq, float z) =>
        new()
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
            if (bucket.TryTake(100.0))
                taken++;
        Assert.Equal(2, taken);
    }
}

public class PoseSendGateTests
{
    static PosePacket Hand(float x) =>
        new()
        {
            Flags = PoseFlags.RightHand,
            RightHandPosition = new Vector3(x, 0, 0),
            RightHandRotation = Quaternion.Identity,
        };

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
            if (gate.ShouldSend(t, p))
            {
                gate.MarkSent(t, p);
                sent++;
            }
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
        Assert.Equal(PoseCodec.Version, Handshake.Protocol);
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
        var p = new PosePacket
        {
            Flags = PoseFlags.RightHand | PoseFlags.VrActive,
            Sequence = seq,
            RightHandPosition = hand,
            RightHandRotation = Quaternion.Identity,
        };
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
        int accepted = 0,
            limited = 0;
        for (int i = 0; i < 1000; i++)
        {
            var d = Encoded((ushort)(i + 1), Vector3.Zero);
            var r = s.Accept(d, d.Length, 1.0 + i * 0.001); // 1000 packets in one second
            if (r == PoseAccept.Accepted)
                accepted++;
            if (r == PoseAccept.RateLimited)
                limited++;
        }
        Assert.InRange(
            accepted,
            1,
            RemotePoseStream.MaxPacketsPerSecond + RemotePoseStream.Burst + 1
        );
        Assert.True(limited > 900);
    }

    [Fact]
    public void RandomBytes_NeverThrow()
    {
        var rng = new Random(5);
        var s = new RemotePoseStream();
        var thrown = Record.Exception(() =>
        {
            for (int i = 0; i < 2000; i++)
            {
                var d = new byte[rng.Next(0, 80)];
                rng.NextBytes(d);
                if (d.Length > 0)
                    d[0] = PoseCodec.Version;
                s.Accept(d, rng.Next(-5, d.Length + 5), i * 0.05);
            }
        });
        Assert.Null(thrown);
    }
}

public class PoseRateTests
{
    [Theory]
    [InlineData(0, 25f)]
    [InlineData(1, 25f)]
    [InlineData(2, 25f)]
    [InlineData(3, 15.625f)]
    public void SmallLobbies_KeepTheConfiguredRate(int peers, float expected)
    {
        TestUtil.Near(expected, PoseRate.ForPeers(25, peers), 1e-3f);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void AllVrLobbies_StayWellUnderThePhotonRoomGuideline(int peers)
    {
        // Every VR player sends to every other PeakXR peer: rate * (1 + peers) messages each.
        int players = peers + 1;
        float total = players * PoseRate.ForPeers(30, peers) * (1 + peers);
        Assert.True(total <= 350f, $"{players} players: {total} msg/s");
    }

    [Fact]
    public void RateNeverDropsBelowTheFloor()
    {
        Assert.Equal(PoseRate.MinRate, PoseRate.ForPeers(30, 50));
        // PoseBuffer only learns packet gaps under 0.25 s.
        Assert.True(1f / PoseRate.MinRate < 0.25f);
    }

    [Fact]
    public void InterpolationDelay_CoversOneAndAHalfSendIntervals()
    {
        Assert.Equal(0.1, PoseRate.Delay(0.1, 0), 6);
        Assert.Equal(1.5 / PoseRate.MinRate, PoseRate.Delay(0.1, 7), 6);
    }
}

public class YawHistoryTests
{
    [Fact]
    public void Sample_Empty_ReturnsFalse()
    {
        Assert.False(new YawHistory().Sample(1.0, out _));
    }

    [Fact]
    public void Sample_InterpolatesBetweenFrames()
    {
        var h = new YawHistory();
        h.Push(1.0, 10f);
        h.Push(1.1, 30f);
        Assert.True(h.Sample(1.05, out float yaw));
        TestUtil.Near(20f, yaw);
    }

    [Fact]
    public void Sample_OutsideTheRange_HoldsTheNearestEnd()
    {
        var h = new YawHistory();
        h.Push(1.0, 10f);
        h.Push(1.1, 30f);
        Assert.True(h.Sample(0.5, out float early));
        TestUtil.Near(10f, early);
        Assert.True(h.Sample(2.0, out float late));
        TestUtil.Near(30f, late);
    }

    [Fact]
    public void Sample_KeepsUnwrappedYaw()
    {
        // lookValues.x accumulates past 360 like vanilla mouse look; a plain lerp is what the body sees too.
        var h = new YawHistory();
        h.Push(1.0, 350f);
        h.Push(1.1, 370f);
        Assert.True(h.Sample(1.05, out float yaw));
        TestUtil.Near(360f, yaw);
    }

    [Fact]
    public void Push_AfterAGap_ForgetsTheOldTurn()
    {
        var h = new YawHistory();
        h.Push(1.0, 0f);
        h.Push(1.0 + YawHistory.ResetAfter + 0.1, 90f);
        Assert.True(h.Sample(1.5, out float yaw));
        TestUtil.Near(90f, yaw);
    }

    [Fact]
    public void ManyFrames_StayBoundedAndSampleRecentHistory()
    {
        var h = new YawHistory();
        int frames = 10 * YawHistory.Capacity;
        for (int i = 0; i < frames; i++)
            h.Push(i / 144.0, i);
        Assert.True(h.Count <= YawHistory.Capacity);
        double now = (frames - 1) / 144.0;
        Assert.True(h.Sample(now - 0.1, out float yaw));
        TestUtil.Near((float)((now - 0.1) * 144.0), yaw, 1e-2f);
    }

    [Fact]
    public void HeadTurn_WithStillHands_KeepsTheRemoteHandStill()
    {
        // The VR sender turns its head (and so lookValues.x) 90 degrees while its hand stays put in the world. The
        // receiver plays the pose 0.2 s late; rebuilt with the yaw of that same moment, the hand does not swing.
        // The receiver's yaw is vanilla's: CharacterSyncer.Update (CharacterSyncer.cs:132-141) lerps from the yaw
        // shown at each package to the received one over one serialization interval, so it trails the sender.
        const double delay = 0.2;
        const double interval = 1.0 / 30.0;
        var root = new Vector3(0f, 1f, 0f);
        var hand = new Vector3(0.3f, 1.2f, 0.4f);
        var poses = new PoseBuffer { Delay = delay };
        var yaws = new YawHistory();
        var uncompensated = new YawHistory();
        float shown = 0f,
            from = 0f,
            to = 0f;
        double since = 0.0;
        float swingWithCurrentYaw = 0f,
            swingUncompensated = 0f;
        for (int frame = 0; frame <= 90; frame++)
        {
            double t = frame / 90.0;
            float yaw = (float)Math.Min(90.0, t * 360.0); // a quarter turn in 0.25 s
            // The package is read before the frame's Update (OnDeserialize resets the clock, Update then adds dt).
            if (frame % 3 == 0)
            {
                var world = new PosePacket
                {
                    Flags = PoseFlags.RightHand | PoseFlags.VrActive,
                    Sequence = (ushort)(frame + 1),
                    RightHandPosition = hand,
                    RightHandRotation = Quaternion.Identity,
                };
                poses.Push(PoseFrame.ToLocal(world, root, yaw), t);
                from = shown;
                to = yaw;
                since = 0.0;
            }
            since += 1.0 / 90.0;
            shown = from + (to - from) * (float)Math.Min(1.0, since / interval);
            yaws.Push(t, shown);
            uncompensated.Push(t, shown);
            if (t < delay)
                continue;
            Assert.True(poses.Sample(t, out var local));
            Assert.True(
                yaws.Sample(
                    PoseRate.YawSampleTime(t, delay, PoseRate.SyncerLag(interval, 1.0 / 90.0)),
                    out float pastYaw
                )
            );
            TestUtil.Near(hand, PoseFrame.ToWorld(local, root, pastYaw).RightHandPosition, 0.03f);
            Assert.True(uncompensated.Sample(t - delay, out float staleYaw));
            swingUncompensated = Math.Max(
                swingUncompensated,
                Vector3.Distance(hand, PoseFrame.ToWorld(local, root, staleYaw).RightHandPosition)
            );
            swingWithCurrentYaw = Math.Max(
                swingWithCurrentYaw,
                Vector3.Distance(hand, PoseFrame.ToWorld(local, root, shown).RightHandPosition)
            );
        }
        // The old rebuild (the receiver's current yaw) swung the hand by tens of centimetres, and reading the yaw at
        // the playback moment without the syncer's lag left it a serialization interval (12 degrees here) behind.
        Assert.True(swingWithCurrentYaw > 0.2f, $"swing {swingWithCurrentYaw}");
        Assert.True(swingUncompensated > 0.06f, $"uncompensated swing {swingUncompensated}");
    }
}

public class HandshakeCompatTests
{
    const int VrBit = 1 << 16;

    [Fact]
    public void SameMajor_AnyMinor_IsAPeer()
    {
        var peers = new PeerTable();
        Assert.True(peers.Set(5, Handshake.Protocol | (3 << 8) | VrBit));
        Assert.True(peers.IsVr(5));
    }

    [Fact]
    public void ReservedHighBits_AreIgnored()
    {
        var peers = new PeerTable();
        Assert.True(peers.Set(5, Handshake.Encode(true) | (1 << 24)));
        Assert.True(peers.IsVr(5));
    }

    [Fact]
    public void OtherMajor_IsAMismatchWithTheProtocolSeen()
    {
        Assert.True(Handshake.Mismatch(2 | VrBit, out int seen));
        Assert.Equal(2, seen);
        Assert.False(new PeerTable().Set(5, 2 | VrBit));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("1")]
    [InlineData(0)]
    public void VanillaOrGarbage_IsNoMismatch(object value)
    {
        Assert.False(Handshake.Mismatch(value, out _));
    }

    [Fact]
    public void Compatible_IsNoMismatch()
    {
        Assert.False(Handshake.Mismatch(Handshake.Encode(true), out _));
        Assert.False(Handshake.Mismatch(Handshake.Protocol | (7 << 8), out _));
    }
}

public class PoseSignatureTests
{
    static byte[] Encoded(PoseFlags flags, int extra = 0)
    {
        var p = new PosePacket
        {
            Flags = flags,
            Sequence = 1,
            RightHandPosition = new Vector3(0.2f, 0.3f, 0.4f),
            RightHandRotation = Quaternion.Identity,
        };
        var buf = new byte[PoseCodec.MaxSize + extra];
        int len = PoseCodec.Encode(p, buf);
        Array.Resize(ref buf, len + extra);
        return buf;
    }

    [Fact]
    public void TrailingExtensionBytes_AreIgnored()
    {
        // A later minor version appends data after the known parts; this build still reads the parts.
        var data = Encoded(PoseFlags.RightHand | PoseFlags.VrActive, extra: 6);
        var s = new RemotePoseStream();
        Assert.Equal(PoseAccept.Accepted, s.Accept(data, data.Length, 1.0));
    }

    [Fact]
    public void WithoutTheVrActiveBit_IsNotAPosePacket()
    {
        // Every sender sets VrActive: another mod's byte[] on event 151 that starts with 1 rarely does.
        var data = Encoded(PoseFlags.RightHand);
        Assert.Equal(PoseAccept.Malformed, new RemotePoseStream().Accept(data, data.Length, 1.0));
    }

    [Fact]
    public void Oversized_IsNotAPosePacket()
    {
        var data = Encoded(
            PoseFlags.RightHand | PoseFlags.VrActive,
            extra: PoseCodec.MaxPacketSize
        );
        Assert.Equal(PoseAccept.Malformed, new RemotePoseStream().Accept(data, data.Length, 1.0));
    }

    [Fact]
    public void MaxPacketSize_LeavesRoomForExtensions()
    {
        Assert.True(PoseCodec.MaxPacketSize > PoseCodec.MaxSize);
    }
}
