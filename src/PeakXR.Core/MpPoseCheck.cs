using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// The two-client test's pose comparison (docs/testing.md, "Modded B"): A writes its tracked hands and B writes
/// A's avatar bones, each in A's body frame as that client sees it, so tools/mp-check.sh compares them without the
/// network lag in A's position and turn. The frame is <see cref="PoseFrame"/>'s: origin at the physical hip,
/// turned by the look yaw. Standalone (System.Numerics only) because the observer links it as a source file.
/// </summary>
public static class MpPoseFrame
{
    public static Vector3 ToFrame(Vector3 world, Vector3 root, float yawDeg) =>
        Vector3.Transform(
            world - root,
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, -yawDeg * MathF.PI / 180f)
        );
}

/// <summary>
/// Packet rate of one stream for the two-client test: the most packets in any sliding window (the receiver's
/// limit is per second, <c>RemotePoseStream.MaxPacketsPerSecond</c>), and the mean over the seconds that
/// had any packet, so the gaps while the sender is ragdolled or loading do not dilute it. Allocation-free.
/// </summary>
public sealed class RateWindow
{
    readonly double window;
    readonly double[] ring;
    int head,
        size;
    long lastSecond = long.MinValue;

    public RateWindow(double windowSeconds, int capacity = 256)
    {
        window = windowSeconds;
        ring = new double[capacity];
    }

    public int Count { get; private set; }

    /// <summary>Most packets inside one window; saturates at the capacity.</summary>
    public int Peak { get; private set; }

    public int BusySeconds { get; private set; }

    public double MeanBusy => BusySeconds == 0 ? 0.0 : (double)Count / BusySeconds;

    /// <param name="t">Arrival time in seconds, never decreasing.</param>
    public void Add(double t)
    {
        Count++;
        long second = (long)Math.Floor(t);
        if (second != lastSecond)
        {
            lastSecond = second;
            BusySeconds++;
        }
        while (size > 0 && t - ring[head] >= window)
        {
            head = (head + 1) % ring.Length;
            size--;
        }
        if (size == ring.Length)
        {
            head = (head + 1) % ring.Length;
            size--;
        }
        ring[(head + size) % ring.Length] = t;
        size++;
        Peak = Math.Max(Peak, size);
    }
}
