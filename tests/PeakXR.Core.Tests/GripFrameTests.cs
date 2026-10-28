namespace PeakXR.Core.Tests;

public class GripFrameTests
{
    private static void Near(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"{expected} vs {actual}");

    [Fact]
    public void FromHand_PointsTheFingersOutOfTheFistNotAlongTheHandle()
    {
        // OpenXR grip: the handle (little finger to thumb) is Unity +Z, so the fingers run along -Y.
        Near(GripFrame.Fingers, UnityQuat.Rotate(GripFrame.FromHand, Vector3.UnitZ));
        Near(GripFrame.ThumbSide, UnityQuat.Rotate(GripFrame.FromHand, Vector3.UnitY));
        Near(new Vector3(0f, -1f, 0f), GripFrame.Fingers);
        Near(Vector3.UnitZ, GripFrame.ThumbSide);
    }

    [Fact]
    public void FromHand_KeepsThePalmSide()
    {
        Near(Vector3.UnitX, UnityQuat.Rotate(GripFrame.FromHand, Vector3.UnitX));
        Near(new Vector3(-1f, 0f, 0f), GripFrame.Palm(left: false));
        Near(Vector3.UnitX, GripFrame.Palm(left: true));
        Near(-GripFrame.Palm(true), GripFrame.BackOfHand(true));
    }

    [Fact]
    public void Forearm_IsOppositeTheFingers() => Near(-GripFrame.Fingers, GripFrame.Forearm);

    [Fact]
    public void FromHandVector_MatchesTheRotation()
    {
        var v = new Vector3(0.1f, 0.2f, 0.3f);
        Near(UnityQuat.Rotate(GripFrame.FromHand, v), GripFrame.FromHandVector(v));
    }

    [Fact]
    public void AimFromGrip_PointsBetweenTheHandleAndTheFingers()
    {
        var aim = UnityQuat.Rotate(GripFrame.AimFromGrip, Vector3.UnitZ);
        Near(Vector3.Normalize(GripFrame.ThumbSide + GripFrame.Fingers), aim);
    }
}
