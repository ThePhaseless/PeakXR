namespace PeakXR.Core.Tests;

public class HandScriptTests
{
    static readonly Vector3 A = new(0f, 1f, 0f);
    static readonly Vector3 B = new(0f, 1f, 2f);

    [Fact]
    public void Along_StartsAndEndsOnTheEndpoints()
    {
        TestUtil.Near(A, HandScript.Along(A, B, 0f));
        TestUtil.Near(B, HandScript.Along(A, B, 1f));
        TestUtil.Near(B, HandScript.Along(A, B, 3f));
        TestUtil.Near(A, HandScript.Along(A, B, -1f));
    }

    [Fact]
    public void Along_EasesInAndOut()
    {
        // Smoothstep: halfway at the midpoint, slower than linear near the ends.
        TestUtil.Near(new Vector3(0f, 1f, 1f), HandScript.Along(A, B, 0.5f));
        Assert.True(HandScript.Along(A, B, 0.1f).Z < 0.2f);
        Assert.True(HandScript.Along(A, B, 0.9f).Z > 1.8f);
    }

    [Fact]
    public void Approach_MovesThePointOntoTheTargetWithoutOvershoot()
    {
        var p = A;
        for (int i = 0; i < 200; i++)
        {
            p = HandScript.Approach(p, B, 8f, 1f / 90f);
            Assert.True(p.Z <= B.Z + 1e-5f);
        }
        TestUtil.Near(B, p, 1e-3f);
    }

    [Theory]
    [InlineData(350f, 20f, 30f)]
    [InlineData(20f, 350f, -30f)]
    [InlineData(90f, 60f, -30f)]
    [InlineData(0f, 180f, 180f)]
    public void YawDelta_WrapsAroundNorth(float from, float to, float expected)
    {
        TestUtil.Near(expected, SelfTestChecks.YawDelta(from, to));
    }

    [Fact]
    public void MaxThrowRange_FlatGroundIsVSquaredOverG()
    {
        TestUtil.Near(10f, SelfTestChecks.MaxThrowRange(10f, 0f, 10f));
    }

    [Fact]
    public void MaxThrowRange_GrowsWithLaunchHeight()
    {
        Assert.True(
            SelfTestChecks.MaxThrowRange(6f, 1.5f, 9.81f)
                > SelfTestChecks.MaxThrowRange(6f, 0f, 9.81f)
        );
        TestUtil.Near(0f, SelfTestChecks.MaxThrowRange(0f, 1.5f, 9.81f));
    }
}
