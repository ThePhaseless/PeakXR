namespace PeakXR.Core.Tests;

public class RopePumpTests
{
    [Fact]
    public void StillHead_DoesNothing() =>
        Assert.Equal(Vector3.Zero, RopePump.Push(Vector3.Zero, 0.25f, 4f, 3f));

    [Fact]
    public void SwayBelowDeadzone_DoesNothing() =>
        Assert.Equal(Vector3.Zero, RopePump.Push(new Vector3(0.2f, 0f, 0f), 0.25f, 4f, 3f));

    [Fact]
    public void Lean_PushesTheSameWay()
    {
        var a = RopePump.Push(new Vector3(0.5f, 0f, 0f), 0.25f, 4f, 3f);
        TestUtil.Near(2f, a.X);
        Assert.Equal(0f, a.Z);
    }

    [Fact]
    public void VerticalHeadMotion_IsIgnored()
    {
        // A nod or a crouch is not a pump: only the level part of the head's motion swings the rope.
        var a = RopePump.Push(new Vector3(0f, 2f, 0.5f), 0.25f, 4f, 3f);
        Assert.Equal(0f, a.Y);
        TestUtil.Near(2f, a.Z);
    }

    [Fact]
    public void HardLean_IsCapped()
    {
        var a = RopePump.Push(new Vector3(3f, 0f, 4f), 0.25f, 4f, 3f);
        TestUtil.Near(3f, a.Length());
    }

    [Fact]
    public void AStillRope_HasNoAmplitude() =>
        TestUtil.Near(0f, RopePump.Amplitude(new(1f, 2f, 3f), new(1f, 4.5f, 3f), Vector3.Zero));

    [Fact]
    public void Amplitude_FromOffsetAndSpeed()
    {
        // 2.5 m below the anchor: at the bottom of a swing, v·sqrt(ℓ/g); at its end, the offset.
        var anchor = new Vector3(0f, 2.5f, 0f);
        TestUtil.Near(
            0.8f * MathF.Sqrt(2.5f / 9.81f),
            RopePump.Amplitude(Vector3.Zero, anchor, new(0.8f, -5f, 0f))
        );
        TestUtil.Near(0.3f, RopePump.Amplitude(new(0.3f, 0f, 0f), anchor, Vector3.Zero));
    }

    [Fact]
    public void AtTheCeiling_APushThatAddsIsDropped()
    {
        var push = new Vector3(2f, 0f, 0f);
        Assert.Equal(Vector3.Zero, RopePump.Limit(push, new Vector3(0.3f, -3f, 0f), 0.4f, 0.4f));
        // Standing at the end of the swing: any push would start the next one bigger.
        Assert.Equal(Vector3.Zero, RopePump.Limit(push, Vector3.Zero, 0.5f, 0.4f));
    }

    [Fact]
    public void BelowTheCeiling_KeepsThePush()
    {
        var push = new Vector3(2f, 0f, 0f);
        Assert.Equal(push, RopePump.Limit(push, new Vector3(1.5f, 0f, 0f), 0.3f, 0.4f));
    }

    [Fact]
    public void LeanAgainstABigSwing_StillBrakesIt()
    {
        var push = new Vector3(-2f, 0f, 0f);
        Assert.Equal(push, RopePump.Limit(push, new Vector3(1.5f, 0f, 0f), 1f, 0.4f));
    }

    /// <summary>A rope pumped as hard as the cap allows, in phase on every frame, for two minutes: a pendulum stepped
    /// at the physics rate (50 Hz) under 90 Hz frames that read the body's velocity, starting from the sway a grab
    /// leaves. The old 0.8 m/s speed ceiling let pushes near the ends of each swing through and reached 2.5 m.</summary>
    [Theory]
    [InlineData(2.5f, 0f)]
    [InlineData(2.5f, 0.3f)]
    [InlineData(10f, 0f)]
    [InlineData(1f, 0f)]
    public void PumpingFlatOut_NeverSwingsPastTheCeiling(float length, float damping)
    {
        const float max = 0.4f,
            frame = 1f / 90f,
            step = 0.02f;
        var anchor = new Vector3(0f, length, 0f);
        float x = 0.2f,
            v = 0f,
            acc = 0f,
            peak = 0f,
            w2 = 9.81f / length;
        for (float t = 0f; t < 120f; t += frame)
        {
            var velocity = new Vector3(v, 0f, 0f);
            var lean = new Vector3(v >= 0f ? 1f : -1f, 0f, 0f);
            var push = RopePump.Limit(
                RopePump.Push(lean, 0.25f, 4f, 2f),
                velocity,
                RopePump.Amplitude(new(x, 0f, 0f), anchor, velocity),
                max
            );
            v += push.X * frame;
            for (acc += frame; acc >= step; acc -= step)
            {
                v += (-w2 * x - damping * v) * step;
                x += v * step;
            }
            if (t > 20f)
                peak = MathF.Max(peak, MathF.Abs(x));
        }
        Assert.InRange(peak, max * 0.9f, max * 1.05f);
    }

    [Fact]
    public void PumpOnSomeoneElsesRope_RefusedOncePerHold()
    {
        bool refused = false;
        Assert.True(RopePump.RefuseOnce(false, 1f, ref refused));
        Assert.False(RopePump.RefuseOnce(false, 1f, ref refused));
        Assert.True(refused);
    }

    [Fact]
    public void OwnRopeOrNoPump_NeverRefused()
    {
        bool refused = false;
        Assert.False(RopePump.RefuseOnce(true, 1f, ref refused));
        Assert.False(RopePump.RefuseOnce(false, 0f, ref refused));
        Assert.False(refused);
    }
}
