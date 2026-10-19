using System;

namespace PeakXR.Core
{
    /// <summary>
    /// Decides when the local pose goes out: at most <see cref="Rate"/> times a second on an even grid
    /// (so 25 Hz stays 25 Hz at any frame rate), only when the pose changed meaningfully, plus a
    /// keepalive that is shorter than the remote driver's stale time so an idle player keeps their pose.
    /// </summary>
    public sealed class PoseSendGate
    {
        public float Rate = 25f;
        public float KeepAlive = 0.25f;
        public float PositionEpsilon = 0.005f;
        public float RotationEpsilon = 1f;
        public float FingerEpsilon = 0.02f;

        bool hasSent;
        double lastSent,
            nextSend;
        PosePacket last;

        public bool ShouldSend(double now, in PosePacket pose)
        {
            if (!hasSent)
                return true;
            if (now < nextSend - 1e-6)
                return false;
            if (now - lastSent >= KeepAlive)
                return true;
            return PoseBlend.Differs(last, pose, PositionEpsilon, RotationEpsilon, FingerEpsilon);
        }

        public void MarkSent(double now, in PosePacket pose)
        {
            double interval = 1.0 / Math.Clamp(Rate, 1f, 120f);
            nextSend = hasSent && now - nextSend < interval ? nextSend + interval : now + interval;
            hasSent = true;
            lastSent = now;
            last = pose;
        }

        /// <summary>Forget the last sent pose so the next one goes out at once (new peer, new room).</summary>
        public void Reset() => hasSent = false;
    }

    /// <summary>Token bucket rate limiter: allows bursts up to the capacity, then a steady rate.</summary>
    public struct TokenBucket
    {
        readonly double capacity,
            perSecond;
        double tokens,
            lastTime;
        bool started;

        public TokenBucket(float capacity, float perSecond)
        {
            this.capacity = capacity;
            this.perSecond = perSecond;
            tokens = capacity;
            lastTime = 0;
            started = false;
        }

        public bool TryTake(double now)
        {
            if (!started)
            {
                started = true;
                lastTime = now;
            }
            else if (now > lastTime)
            {
                tokens = Math.Min(capacity, tokens + (now - lastTime) * perSecond);
                lastTime = now;
            }

            if (tokens < 1.0 - 1e-9)
                return false;
            tokens -= 1.0;
            return true;
        }
    }
}
