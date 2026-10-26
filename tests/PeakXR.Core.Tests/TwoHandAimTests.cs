namespace PeakXR.Core.Tests;

public class TwoHandAimTests
{
    static readonly Vector3 Hold = new(0f, 1f, 0f);

    [Fact]
    public void SupportAlongTheBarrel_LeavesTheHoldAlone()
    {
        var rot = UnityQuat.Euler(0f, 20f, 0f);
        var barrel = UnityQuat.Rotate(rot, Vector3.UnitZ);
        var turned = GripMath.TwoHandAim(rot, barrel, Hold, Hold + barrel * 0.4f, 0.15f);
        TestUtil.SameRotation(rot, turned, 0.5f);
    }

    [Fact]
    public void SupportOffToTheSide_TurnsTheBarrelOntoTheHands()
    {
        var turned = GripMath.TwoHandAim(
            Quaternion.Identity,
            Vector3.UnitZ,
            Hold,
            Hold + new Vector3(0.3f, 0f, 0.3f),
            0.15f
        );
        var barrel = UnityQuat.Rotate(turned, Vector3.UnitZ);
        TestUtil.Near(Vector3.Normalize(new Vector3(1f, 0f, 1f)), barrel, 1e-3f);
    }

    [Fact]
    public void SupportBehind_OrTooClose_NeverFlipsTheBarrel()
    {
        var behind = GripMath.TwoHandAim(
            Quaternion.Identity,
            Vector3.UnitZ,
            Hold,
            Hold - new Vector3(0f, 0f, 0.4f),
            0.15f
        );
        TestUtil.Near(Vector3.UnitZ, UnityQuat.Rotate(behind, Vector3.UnitZ), 1e-3f);
        var close = GripMath.TwoHandAim(
            Quaternion.Identity,
            Vector3.UnitZ,
            Hold,
            Hold + new Vector3(0.1f, 0f, 0f),
            0.15f
        );
        TestUtil.SameRotation(Quaternion.Identity, close, 0.5f);
    }
}
