namespace PeakXR.Core.Tests;

public class MpPoseFrameTests
{
    [Fact]
    public void ToFrame_MatchesTheWireFrame()
    {
        var root = new Vector3(10f, 5f, -3f);
        var hand = new Vector3(10.4f, 5.0f, -2.5f);
        var packet = new PosePacket
        {
            Flags = PoseFlags.RightHand,
            RightHandPosition = hand,
            RightHandRotation = Quaternion.Identity,
        };

        var wire = PoseFrame.ToLocal(packet, root, 73f).RightHandPosition;

        TestUtil.Near(wire, MpPoseFrame.ToFrame(hand, root, 73f));
    }

    [Fact]
    public void ToFrame_CancelsTheBodysWalkAndTurn()
    {
        // The same hand, seen by two clients that place the body at different spots and yaws.
        var offset = new Vector3(0.3f, 0.4f, 0.5f);
        var rootA = new Vector3(1f, 2f, 3f);
        var rootB = new Vector3(1.6f, 2.1f, 2.2f);
        var onA = rootA + UnityQuat.Rotate(UnityQuat.Euler(0f, 30f, 0f), offset);
        var onB = rootB + UnityQuat.Rotate(UnityQuat.Euler(0f, 95f, 0f), offset);

        TestUtil.Near(MpPoseFrame.ToFrame(onA, rootA, 30f), MpPoseFrame.ToFrame(onB, rootB, 95f));
    }
}

public class RateWindowTests
{
    [Fact]
    public void Peak_IsTheMostPacketsInAnyWindow()
    {
        var w = new RateWindow(1.0);
        for (int i = 0; i < 25; i++)
            w.Add(i * 0.04); // 25 Hz for one second
        for (int i = 0; i < 10; i++)
            w.Add(1.0 + i * 0.01); // a burst of 10 in 0.1 s

        Assert.Equal(35, w.Count);
        // From t = 0.10 to t = 1.09: 22 regular packets plus the 10 of the burst.
        Assert.Equal(32, w.Peak);
    }

    [Fact]
    public void Peak_ForgetsPacketsOlderThanTheWindow()
    {
        var w = new RateWindow(1.0);
        for (int i = 0; i < 30; i++)
            w.Add(i * 0.01);
        for (int i = 0; i < 5; i++)
            w.Add(10 + i * 0.01);

        Assert.Equal(30, w.Peak);
    }

    [Fact]
    public void MeanBusy_CountsOnlySecondsWithPackets()
    {
        var w = new RateWindow(1.0);
        for (int i = 0; i < 20; i++)
            w.Add(100 + i * 0.05); // second 100: 20 packets
        for (int i = 0; i < 20; i++)
            w.Add(200 + i * 0.05); // second 200: 20 packets, a long gap before

        Assert.Equal(2, w.BusySeconds);
        Assert.Equal(20.0, w.MeanBusy, 6);
    }

    [Fact]
    public void Empty_HasNoRate()
    {
        var w = new RateWindow(1.0);

        Assert.Equal(0, w.Peak);
        Assert.Equal(0.0, w.MeanBusy);
    }

    [Fact]
    public void Peak_SaturatesAtCapacity()
    {
        var w = new RateWindow(1.0, capacity: 8);
        for (int i = 0; i < 20; i++)
            w.Add(i * 0.001);

        Assert.Equal(8, w.Peak);
        Assert.Equal(20, w.Count);
    }
}
