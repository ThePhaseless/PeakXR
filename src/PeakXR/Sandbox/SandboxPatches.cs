using System.Reflection;
using HarmonyLib;
using PeakXR.Framework;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>
/// Skips fall damage for the local character while god mode is on.
/// Target: <c>CharacterMovement.CheckFallDamage()</c> (private, no arguments), decomp Assembly-CSharp/CharacterMovement.cs:831,
/// called from <c>Land()</c> at :821. It is where a landing adds Injury. Skipping it also skips
/// the "Fall" reaction at :843, which is wanted for a no-damage mode.
/// </summary>
[HarmonyPatch]
static class FallDamagePatch
{
    internal static AccessTools.FieldRef<CharacterMovement, Character> CharacterRef;

    static MethodBase TargetMethod() =>
        GameMember.Method(typeof(CharacterMovement), "CheckFallDamage", System.Type.EmptyTypes);

    static bool Prefix(CharacterMovement __instance)
    {
        if (!SandboxActions.GodMode || !SandboxActions.Active)
            return true;
        var character = CharacterRef(__instance);
        return character == null || !character.IsLocal;
    }
}

/// <summary>
/// While the sandbox window is open the local character gets no input, the way the game's own menus do it.
/// Target: <c>Character.CanDoInput()</c> (internal, bool), decomp Assembly-CSharp/Character.cs:1714, which feeds
/// <c>character.input.Sample(...)</c> at CharacterMovement.cs:198.
/// </summary>
[HarmonyPatch]
static class WindowInputPatch
{
    static MethodBase TargetMethod() =>
        GameMember.Method(typeof(Character), "CanDoInput", System.Type.EmptyTypes);

    static void Postfix(Character __instance, ref bool __result)
    {
        if (SandboxWindow.IsOpen && __instance.IsLocal)
            __result = false;
    }
}

/// <summary>
/// Frees the mouse while the sandbox window is open. The game's <c>CursorHandler.Update</c> (decomp
/// Assembly-CSharp/CursorHandler.cs:11) re-locks the cursor every frame unless one of its own windows is open.
/// </summary>
[HarmonyPatch]
static class WindowCursorPatch
{
    static MethodBase TargetMethod() =>
        GameMember.Method(typeof(CursorHandler), "Update", System.Type.EmptyTypes);

    static void Postfix()
    {
        if (!SandboxWindow.IsOpen)
            return;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
