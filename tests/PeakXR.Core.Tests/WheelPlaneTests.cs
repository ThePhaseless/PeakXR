using System.Numerics;

namespace PeakXR.Core.Tests;

public class WheelPlaneTests
{
    private const float Eps = 1e-4f;

    [Fact]
    public void Centre_SitsOnTheEyeToHandLine_OffsetTowardTheEye()
    {
        var eye = new Vector3(0f, 1.6f, 0f);
        var hand = new Vector3(0.1f, 1.2f, 0.4f);
        var centre = WheelPlane.Centre(eye, hand, 0.06f);

        Assert.Equal(0.06f, Vector3.Distance(centre, hand), 4);
        var toHand = Vector3.Normalize(hand - eye);
        var toCentre = Vector3.Normalize(centre - eye);
        Assert.True(Vector3.Dot(toHand, toCentre) > 1f - Eps);
        Assert.True(Vector3.Distance(eye, centre) < Vector3.Distance(eye, hand));
    }

    [Fact]
    public void Centre_OffsetIsCappedForAHandCloseToTheEye()
    {
        var eye = Vector3.Zero;
        var hand = new Vector3(0f, 0f, 0.2f);
        var centre = WheelPlane.Centre(eye, hand, 0.06f);
        Assert.Equal(0.2f * (1f - WheelPlane.MaxOffsetFraction), centre.Z, 4);
    }

    [Fact]
    public void Centre_HandAtTheEyeStaysAtTheHand()
    {
        var p = new Vector3(1f, 2f, 3f);
        Assert.Equal(p, WheelPlane.Centre(p, p, 0.06f));
    }

    [Fact]
    public void DrawScale_WheelLooksAsBigAsOneAtTheHand()
    {
        // Wheel 0.4 m from the eye, hand 0.46 m: drawn at 0.4 / 0.46 so it covers the same angle.
        var eye = Vector3.Zero;
        var hand = new Vector3(0f, 0f, 0.46f);
        var centre = WheelPlane.Centre(eye, hand, 0.06f);
        Assert.Equal(0.4f / 0.46f, WheelPlane.DrawScale(eye, hand, centre), 4);
    }

    [Fact]
    public void DrawScale_HandAtTheEyeIsOne()
    {
        var p = new Vector3(1f, 2f, 3f);
        Assert.Equal(1f, WheelPlane.DrawScale(p, p, p));
    }
}
