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

/// <summary>
/// The receiver's view of one remote character's look yaw over the last moments, recorded once a frame, so the pose
/// played <see cref="PoseBuffer.Delay"/> late is rebuilt with the yaw of that same moment (<see cref="PoseFrame"/>).
/// Rebuilding it with the current yaw swung the hands ahead on every head turn and jumped them on snap turns.
/// Yaw is lerped plainly, unwrapped, as vanilla's CharacterSyncer does. Allocation-free after construction.
/// </summary>
public sealed class YawHistory
{
    /// <summary>A second at 144 Hz, well past the longest playback delay (0.3 s).</summary>
    public const int Capacity = 160;

    /// <summary>A gap this long (the stream went silent) drops the old samples.</summary>
    public const double ResetAfter = 0.5;

    readonly double[] times = new double[Capacity];
    readonly float[] yaws = new float[Capacity];
    int start,
        count;

    public int Count => count;

    public void Push(double time, float yaw)
    {
        if (count > 0 && time - TimeAt(count - 1) >= ResetAfter)
            count = 0;
        if (count == Capacity)
        {
            start = (start + 1) % Capacity;
            count--;
        }
        int i = (start + count) % Capacity;
        times[i] = time;
        yaws[i] = yaw;
        count++;
    }

    /// <summary>The yaw at <paramref name="time"/>, held at the ends of the history. Samples older than the one
    /// just before <paramref name="time"/> are dropped, so call it with a time that only moves forward.</summary>
    public bool Sample(double time, out float yaw)
    {
        if (count == 0)
        {
            yaw = 0f;
            return false;
        }
        while (count >= 2 && TimeAt(1) <= time)
        {
            start = (start + 1) % Capacity;
            count--;
        }
        if (count == 1 || time <= TimeAt(0))
        {
            yaw = YawAt(0);
            return true;
        }
        double t0 = TimeAt(0);
        float f = (float)((time - t0) / (TimeAt(1) - t0));
        yaw = YawAt(0) + (YawAt(1) - YawAt(0)) * f;
        return true;
    }

    double TimeAt(int i) => times[(start + i) % Capacity];

    float YawAt(int i) => yaws[(start + i) % Capacity];
}
