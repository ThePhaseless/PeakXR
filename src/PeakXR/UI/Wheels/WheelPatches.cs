using System.Reflection;
using HarmonyLib;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.UI.Wheels;

/// <summary>
/// The game skips CameraLook while a wheel is open (A/CharacterMovement.cs:201 via CanMoveCamera :174, which reads
/// CharacterData.usingWheel :470). In VR that look is the headset, and freezing it would leave the character's
/// head, aim and the view sent to peers stuck while your real head moves. This postfix runs the skipped CameraLook
/// for the local VR character, so LookPatches' hooks write the headset look as usual. Nothing in Update between the
/// gate and the end sets the wheel flags (only GUIManager does, :550/:557/:568/:574), so look runs once a frame.
/// The stick stays out: Sample zeroed lookInput (A/CharacterInput.cs:214, ResetInput :304) and LookPatches zeroes it
/// again. The wheel flags stay untouched, so inventory.vr's Sample postfix reads the real backpack-wheel state.
/// Patched on Update rather than on CanMoveCamera, which is small enough for Mono to inline.
/// A/CharacterMovement.cs:183 <c>private void Update()</c>, :283 <c>private void CameraLook()</c>.
/// </summary>
[HarmonyPatch]
internal static class HeadLookWhileWheelOpenPatch
{
    internal static System.Action<CharacterMovement> CameraLook;

    private static MethodBase TargetMethod() =>
        GameMember.Method(typeof(CharacterMovement), "Update");

    private static void Postfix(CharacterMovement __instance)
    {
        if (!VRSession.Active)
            return;
        var c = UIWheelsFeature.MovementCharacter(__instance);
        if (c != null && c.IsLocal && c.data.usingWheel)
            CameraLook(__instance);
    }
}

/// <summary>
/// In VR a wheel's <c>TestSelectSliceGamepad</c> is read in the wheel's plane by <see cref="RadialWheels"/>: the game
/// compares the vector with each slice's world-space up, which only fits a wheel facing +Z, and a resting stick
/// would dehover a slice picked by hand every frame. inventory.vr steers the backpack wheel through this same
/// method (<c>BodyInventory.SelectWheelSlice</c>), so one selector serves both. Wheels not on a VR hand run the
/// game's code. A/EmoteWheel.cs:106 <c>protected override void TestSelectSliceGamepad(Vector2)</c>.
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

/// <summary>
/// A menu-button pause the game refuses (an open wheel, the end screen, loading: A/GUIManager.cs:420-427
/// <c>public void UpdatePaused()</c>) gets the refusal cue at the hand that pressed it instead of nothing.
/// Same gate as vanilla, read before it runs; the press itself is left alone.
/// </summary>
[HarmonyPatch]
internal static class PauseRefusedPatch
{
    private static MethodBase TargetMethod() =>
        GameMember.Method(typeof(GUIManager), "UpdatePaused");

    private static void Prefix(GUIManager __instance)
    {
        var c = Character.localCharacter;
        if (!VRSession.Active || c == null || !c.input.pauseWasPressed || GUIManager.InPauseMenu)
            return;
        bool refused =
            LoadingScreenHandler.loading
            || __instance.wheelActive
            || (__instance.endScreen != null && __instance.endScreen.isOpen);
        if (refused)
            Affordance.DenyAtHand(RadialWheels.MenuLeft, DenyReason.NotNow);
    }
}
