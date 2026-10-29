namespace PeakXR.Core.Tests;

public class AimHoldTests
{
    // The RescueHook as the game's Hand_R puts it in a right controller (offline, from the prefab): its barrel about
    // 64 degrees off the fingers, toward the back of the hand.
    static readonly Quaternion HookRot = UnityQuat.LookRotation(
        new Vector3(0.894f, -0.441f, -0.078f),
        new Vector3(0.3f, 0.6f, 0.1f)
    );
    static readonly Vector3 HookPos = new(0.05f, -0.12f, 0.03f);
    static readonly Vector3 Aim = UnityQuat.Rotate(GripFrame.AimFromGrip, Vector3.UnitZ);

    static Vector3 Barrel(Quaternion rot) => UnityQuat.Rotate(rot, Vector3.UnitZ);

    static Vector3 Up(Quaternion rot) => UnityQuat.Rotate(rot, Vector3.UnitY);

    [Fact]
    public void TheBarrelEndsAlongTheAim()
    {
        var pos = HookPos;
        var rot = HookRot;
        GripMath.AlongAim(Aim * 3f, ref pos, ref rot);
        TestUtil.Near(Vector3.Normalize(Aim), Barrel(rot), 1e-4f);
    }

    [Fact]
    public void TheItemTurnsAboutTheFist()
    {
        var pos = HookPos;
        var rot = HookRot;
        var turn = GripMath.AlongAim(Aim, ref pos, ref rot);
        TestUtil.Near(UnityQuat.Rotate(turn, HookPos), pos, 1e-5f);
        TestUtil.SameRotation(UnityQuat.Mul(turn, HookRot), rot, 0.01f);
        TestUtil.Near(HookPos.Length(), pos.Length(), 1e-5f);
    }

    [Fact]
    public void TheRollKeepsTheItemsUpNearestItsOldUp()
    {
        var pos = HookPos;
        var rot = HookRot;
        GripMath.AlongAim(Aim, ref pos, ref rot);
        var old = Up(HookRot);
        var want = Vector3.Normalize(old - Vector3.Dot(old, Aim) * Aim);
        TestUtil.Near(want, Up(rot), 1e-4f);
    }

    [Fact]
    public void ABarrelAlreadyAlongTheAimStays()
    {
        var rot = UnityQuat.LookRotation(Aim, Vector3.UnitX);
        var pos = HookPos;
        var turn = GripMath.AlongAim(Aim, ref pos, ref rot);
        TestUtil.SameRotation(Quaternion.Identity, turn, 0.01f);
        TestUtil.Near(HookPos, pos, 1e-5f);
    }

    [Fact]
    public void AnUpAlongTheAimTakesTheLeastTurn()
    {
        // The barrel along +Z, its up straight down the aim: no roll can keep that up, so the least turn decides.
        var rot = UnityQuat.LookRotation(Vector3.UnitZ, Vector3.UnitY);
        var pos = HookPos;
        var turn = GripMath.AlongAim(Vector3.UnitY, ref pos, ref rot);
        TestUtil.Near(Vector3.UnitY, Barrel(rot), 1e-4f);
        TestUtil.SameRotation(GripMath.FromTo(Vector3.UnitZ, Vector3.UnitY), turn, 0.01f);
    }

    [Fact]
    public void NoAimLeavesThePoseAlone()
    {
        var rot = HookRot;
        var pos = HookPos;
        GripMath.AlongAim(Vector3.Zero, ref pos, ref rot);
        TestUtil.SameRotation(HookRot, rot, 0.01f);
        TestUtil.Near(HookPos, pos, 1e-6f);
    }
}
