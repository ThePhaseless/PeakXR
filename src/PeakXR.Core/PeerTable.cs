using System;
using System.Collections.Generic;

namespace PeakXR.Core
{
    /// <summary>
    /// The <c>PeakXR</c> Photon player property. Its value is an int: the low 16 bits hold the pose
    /// protocol version, bit 16 says the player is in VR right now. Vanilla clients ignore unknown
    /// player properties, so this is how modded peers find each other without touching vanilla traffic.
    /// </summary>
    public static class Handshake
    {
        public const string Key = "PeakXR";
        public const int Protocol = PoseCodec.Version;
        const int VrBit = 1 << 16;

        public static int Encode(bool vrActive) => Protocol | (vrActive ? VrBit : 0);

        public static bool TryParse(object value, out int protocol, out bool vrActive)
        {
            protocol = 0;
            vrActive = false;
            if (!(value is int raw) || raw <= 0)
                return false;
            protocol = raw & 0xFFFF;
            vrActive = (raw & VrBit) != 0;
            return protocol > 0;
        }
    }

    /// <summary>
    /// Remote players that announced a compatible PeakXR protocol. <see cref="Targets"/> is the actor
    /// list for <c>RaiseEventOptions.TargetActors</c>; it is rebuilt only when membership changes.
    /// </summary>
    public sealed class PeerTable
    {
        readonly Dictionary<int, bool> peers = new Dictionary<int, bool>();

        public int[] Targets { get; private set; } = Array.Empty<int>();

        public int Count => peers.Count;

        public bool IsPeer(int actor) => peers.ContainsKey(actor);

        public bool IsVr(int actor) => peers.TryGetValue(actor, out bool vr) && vr;

        /// <summary>Applies an actor's <see cref="Handshake.Key"/> property value (null when absent). Returns true when anything changed.</summary>
        public bool Set(int actor, object propertyValue)
        {
            if (!Handshake.TryParse(propertyValue, out int protocol, out bool vr) || protocol != Handshake.Protocol)
                return Remove(actor);

            if (peers.TryGetValue(actor, out bool oldVr))
            {
                if (oldVr == vr)
                    return false;
                peers[actor] = vr;
                return true;
            }

            peers.Add(actor, vr);
            Rebuild();
            return true;
        }

        public bool Remove(int actor)
        {
            if (!peers.Remove(actor))
                return false;
            Rebuild();
            return true;
        }

        public void Clear()
        {
            peers.Clear();
            Rebuild();
        }

        void Rebuild()
        {
            var targets = new int[peers.Count];
            int i = 0;
            foreach (var actor in peers.Keys)
                targets[i++] = actor;
            Targets = targets;
        }
    }

    public enum PoseAccept
    {
        Accepted,
        RateLimited,
        Malformed,
        OutOfOrder,
    }

    /// <summary>
    /// Receive side of one remote peer's pose stream: per-sender rate limit, decode, sanity check and
    /// jitter buffer. Bad input is rejected with a reason; nothing here throws.
    /// </summary>
    public sealed class RemotePoseStream
    {
        /// <summary>Senders are capped at 30 Hz; this leaves room for jitter bunching.</summary>
        public const int MaxPacketsPerSecond = 40;
        public const int Burst = 10;

        /// <summary>Head and hands farther than this from the hip (metres) are treated as garbage.</summary>
        public const float MaxPartDistance = 4f;

        TokenBucket bucket = new TokenBucket(Burst, MaxPacketsPerSecond);

        public PoseBuffer Buffer { get; } = new PoseBuffer();

        public double LastAcceptTime { get; private set; } = double.NegativeInfinity;

        public PoseAccept Accept(byte[] data, int length, double now)
        {
            if (!bucket.TryTake(now))
                return PoseAccept.RateLimited;
            if (!PoseCodec.Decode(data, length, out var pose) || !Plausible(pose))
                return PoseAccept.Malformed;
            if (!Buffer.Push(pose, now))
                return PoseAccept.OutOfOrder;
            LastAcceptTime = now;
            return PoseAccept.Accepted;
        }

        static bool Plausible(in PosePacket p)
        {
            const float max = MaxPartDistance * MaxPartDistance;
            if (p.HasHead && p.HeadPosition.LengthSquared() > max)
                return false;
            if (p.HasLeftHand && p.LeftHandPosition.LengthSquared() > max)
                return false;
            if (p.HasRightHand && p.RightHandPosition.LengthSquared() > max)
                return false;
            return true;
        }
    }
}
