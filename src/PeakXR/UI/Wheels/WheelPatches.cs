using System.Reflection;
using HarmonyLib;
using PeakXR.Framework;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.UI.Wheels;

/// <summary>
/// The game skips CameraLook while a wheel is open (A/CharacterMovement.cs:201 via CanMoveCamera :174, which reads
/// CharacterData.usingWheel :469). In VR that look is the headset, and freezing it would leave the character's
/// head, aim and the view sent to peers stuck while your real head moves. The local character's wheel flags are
/// cleared for the length of CharacterMovement.Update only and put back afterwards; input stays blocked because
/// CharacterInput.Sample keys off GUIManager.wheelActive (A/Character.cs:1716), not these flags.
/// Patched around Update rather than on CanMoveCamera, which is small enough for Mono to inline.
/// A/CharacterMovement.cs:183 <c>private void Update()</c>.
/// </summary>
[HarmonyPatch]
internal static class HeadLookWhileWheelOpenPatch
{
    private const int Emote = 1,
        Backpack = 2;

    private static MethodBase TargetMethod() =>
        GameMember.Method(typeof(CharacterMovement), "Update");

    private static void Prefix(CharacterMovement __instance, out int __state)
    {
        __state = 0;
        if (!VRSession.Active)
            return;
        var c = UIWheelsFeature.MovementCharacter(__instance);
        if (c == null || !c.IsLocal)
            return;
        var data = c.data;
        if (data.usingEmoteWheel)
            __state |= Emote;
        if (data.usingBackpackWheel)
            __state |= Backpack;
        data.usingEmoteWheel = false;
        data.usingBackpackWheel = false;
    }

    // A finalizer, so the flags come back even if Update throws; a lost flag would keep the wheel open.
    private static void Finalizer(CharacterMovement __instance, int __state)
    {
        if (__state == 0)
            return;
        var data = UIWheelsFeature.MovementCharacter(__instance).data;
        data.usingEmoteWheel = (__state & Emote) != 0;
        data.usingBackpackWheel = (__state & Backpack) != 0;
    }
}

/// <summary>
/// In VR a wheel's <c>TestSelectSliceGamepad</c> is read in the wheel's plane by <see cref="RadialWheels"/>: the game
/// compares the vector with each slice's world-space up, which only fits a wheel facing +Z, and a resting stick
/// would dehover a slice picked by hand every frame. inventory.vr steers the backpack wheel through this same
/// method (<c>BodyInventory.SelectWheelSlice</c>), so one selector serves both. Wheels not on a VR hand run the
/// game's code. A/EmoteWheel.cs:120 <c>protected override void TestSelectSliceGamepad(Vector2)</c>.
/// </summary>
[HarmonyPatch]
internal static class EmoteWheelGamepadPatch
{
    private static MethodBase TargetMethod() =>
        GameMember.Method(typeof(EmoteWheel), "TestSelectSliceGamepad", new[] { typeof(Vector2) });

    private static bool Prefix(EmoteWheel __instance, Vector2 gamepadVector) =>
        !VRSession.Active || !RadialWheels.Steer(__instance, gamepadVector);
}

/// <summary>Same as <see cref="EmoteWheelGamepadPatch"/>. A/BackpackWheel.cs:253 <c>protected override void
/// TestSelectSliceGamepad(Vector2)</c>.</summary>
[HarmonyPatch]
internal static class BackpackWheelGamepadPatch
{
    private static MethodBase TargetMethod() =>
        GameMember.Method(
            typeof(BackpackWheel),
            "TestSelectSliceGamepad",
            new[] { typeof(Vector2) }
        );

    private static bool Prefix(BackpackWheel __instance, Vector2 gamepadVector) =>
        !VRSession.Active || !RadialWheels.Steer(__instance, gamepadVector);
}
