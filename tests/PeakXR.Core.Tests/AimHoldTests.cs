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

    // How far a turn takes the fist's tube (the grip's +Z) off its line, degrees, either way round.
    static float Tilt(Quaternion turn) =>
        MathF.Acos(MathF.Min(1f, MathF.Abs(UnityQuat.Rotate(turn, Vector3.UnitZ).Z)))
        * UnityQuat.Rad2Deg;

    [Fact]
    public void TheGripLeavesTheFistTheLeastAnyTurnOntoTheAimCould()
    {
        // #25: keeping the item's up turned the grip 40-55 degrees out of the fist, the blowgun about 100.
        var pos = HookPos;
        var rot = HookRot;
        var turn = GripMath.AlongAim(Aim, ref pos, ref rot);
        var swing = GripMath.FromTo(Barrel(HookRot), Aim);
        for (int deg = 0; deg < 360; deg += 2)
        {
            var other = UnityQuat.Mul(
                Quaternion.CreateFromAxisAngle(Vector3.Normalize(Aim), deg * UnityQuat.Deg2Rad),
                swing
            );
            Assert.True(Tilt(turn) <= Tilt(other) + 0.5f, $"{Tilt(turn)} > {Tilt(other)} at {deg}");
        }
        Assert.Equal(Tilt(turn), GripMath.FistTilt(turn), 2);
    }

    [Fact]
    public void ABarrelOutOfTheBottomOfTheFistIsHeldTheOtherWayRound()
    {
        // The blowgun: its barrel leaves the fist on the little finger's side (-Z). Turned the other way round in the
        // fist it runs out by the thumb, and the grip leaves the fist only by about the aim's own angle off the tube.
        var rot = UnityQuat.LookRotation(new Vector3(0f, 0.2f, -1f), Vector3.UnitY);
        var pos = HookPos;
        var turn = GripMath.AlongAim(Aim, ref pos, ref rot);
        TestUtil.Near(Vector3.Normalize(Aim), Barrel(rot), 1e-4f);
        Assert.True(GripMath.FistTilt(turn) < 50f, $"tilt {GripMath.FistTilt(turn)}");
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
