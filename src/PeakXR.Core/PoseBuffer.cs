using System;

namespace PeakXR.Core;

/// <summary>
/// Jitter buffer for one remote peer's pose stream. Packets are stamped with their arrival time and
/// played back <see cref="Delay"/> seconds late, interpolating between the two snapshots around the
/// playback time, so hands move smoothly even though packets arrive at 20-30 Hz with jitter.
/// Out-of-order packets are dropped. Allocation-free after construction.
/// </summary>
public sealed class PoseBuffer
{
    public const int Capacity = 32;

    /// <summary>Silence after which the sequence check restarts (the peer rejoined or re-entered VR).</summary>
    public const double ResetAfter = 1.0;

    /// <summary>Playback delay behind the newest arrival, in seconds.</summary>
    public double Delay { get; set; } = 0.1;

    readonly PosePacket[] poses = new PosePacket[Capacity];
    readonly double[] times = new double[Capacity];
    int start,
        count;

    bool hasLast;
    ushort lastSequence;
    double lastArrival;
    double interval = 0.04;

    public int Count => count;

    /// <summary>Adds a packet that arrived at <paramref name="time"/>. Returns false when it is not newer than the last one.</summary>
    public bool Push(in PosePacket pose, double time)
    {
        if (hasLast && time - lastArrival >= ResetAfter)
            Clear();
        if (hasLast && !PoseCodec.IsNewer(pose.Sequence, lastSequence))
            return false;

        double stamp = time;
        if (count > 0)
        {
            double gap = time - lastArrival;
            if (gap > 0.005 && gap < 0.25)
                interval += (gap - interval) * 0.1;

            double newestStamp = TimeAt(count - 1);
            // The sender skips unchanged poses and only sends keepalives. Hold the previous pose
            // until one interval before this packet instead of blending across the whole idle gap.
            if (gap > 2.5 * interval && time - interval > newestStamp)
            {
                var held = PoseAt(count - 1);
                Add(held, time - interval);
                newestStamp = time - interval;
            }
            // Packets delivered in the same frame get spread out so each one is visible.
            stamp = Math.Max(time, newestStamp + 0.5 * interval);
        }

        Add(pose, stamp);
        hasLast = true;
        lastSequence = pose.Sequence;
        lastArrival = time;
        return true;
    }

    /// <summary>The pose to show at <paramref name="now"/>. False when nothing has been received.</summary>
    public bool Sample(double now, out PosePacket pose)
    {
        if (count == 0)
        {
            pose = default;
            return false;
        }

        double t = now - Delay;
        while (count >= 2 && TimeAt(1) <= t)
        {
            start = (start + 1) % Capacity;
            count--;
        }

        if (count == 1 || t <= TimeAt(0))
        {
            pose = PoseAt(0);
            return true;
        }

        double t0 = TimeAt(0),
            t1 = TimeAt(1);
        float f = (float)((t - t0) / (t1 - t0));
        pose = PoseBlend.Lerp(PoseAt(0), PoseAt(1), f);
        return true;
    }

    public void Clear()
    {
        start = 0;
        count = 0;
        hasLast = false;
    }

    void Add(in PosePacket pose, double stamp)
    {
        if (count == Capacity)
        {
            start = (start + 1) % Capacity;
            count--;
        }
        int i = (start + count) % Capacity;
        poses[i] = pose;
        times[i] = stamp;
        count++;
    }

    PosePacket PoseAt(int i) => poses[(start + i) % Capacity];

    double TimeAt(int i) => times[(start + i) % Capacity];
}
