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
    public void HandOffset_OpeningHandIsTheWheelCentre()
    {
        var eye = new Vector3(0f, 1.6f, 0f);
        var hand = new Vector3(0.1f, 1.2f, 0.4f);
        var centre = WheelPlane.Centre(eye, hand, 0.06f);
        var forward = Vector3.Normalize(centre - eye);
        var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
        var up = Vector3.Cross(forward, right);

        var offset = WheelPlane.HandOffset(eye, hand, centre, right, up);
        Assert.True(offset.Length() < Eps);
    }

    [Fact]
    public void HandOffset_IsWhereTheEyeSeesTheHandOnThePlane()
    {
        // Wheel 0.4 m ahead, hand 6 cm behind it and 0.1 m right: seen at 0.1 * 0.4 / 0.46 on the wheel.
        var offset = WheelPlane.HandOffset(
            Vector3.Zero,
            new Vector3(0.1f, 0f, 0.46f),
            new Vector3(0f, 0f, 0.4f),
            Vector3.UnitX,
            Vector3.UnitY
        );
        Assert.Equal(0.1f * 0.4f / 0.46f, offset.X, 4);
        Assert.Equal(0f, offset.Y, 4);
    }

    [Fact]
    public void HandOffset_HandOnThePlaneIsItsOwnOffset()
    {
        var offset = WheelPlane.HandOffset(
            Vector3.Zero,
            new Vector3(-0.05f, 0.08f, 0.4f),
            new Vector3(0f, 0f, 0.4f),
            Vector3.UnitX,
            Vector3.UnitY
        );
        Assert.Equal(-0.05f, offset.X, 4);
        Assert.Equal(0.08f, offset.Y, 4);
    }

    [Fact]
    public void HandOffset_HandBesideOrBehindTheEyeFallsBackToStraightProjection()
    {
        var offset = WheelPlane.HandOffset(
            Vector3.Zero,
            new Vector3(0.3f, 0.1f, -0.05f),
            new Vector3(0f, 0f, 0.4f),
            Vector3.UnitX,
            Vector3.UnitY
        );
        Assert.Equal(0.3f, offset.X, 4);
        Assert.Equal(0.1f, offset.Y, 4);
    }
}
