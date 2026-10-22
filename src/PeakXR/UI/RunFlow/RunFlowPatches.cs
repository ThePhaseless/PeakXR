using System;
using System.Reflection;
using HarmonyLib;
using Peak;
using PeakXR.Framework;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace PeakXR.UI.RunFlow;

/// <summary>Every teleport of the local character goes through one method: warp items
/// (Action_WarpToRandomPlayer.cs:33 WarpPlayerRPC), segment jumps and biome warps
/// (MapHandler.cs:1094), revives (Character.cs:1803), checkpoints (Character.cs:661), throw warps.
/// Blink to black around it. A/Character.cs:1849 <c>internal void WarpPlayer(Vector3, bool)</c>.</summary>
[HarmonyPatch]
internal static class WarpPlayerPatch
{
    private static MethodBase TargetMethod() =>
        GameMember.Method(typeof(Character), "WarpPlayer", new[] { typeof(Vector3), typeof(bool) });

    private static void Prefix(Character __instance, Vector3 position)
    {
        if (__instance.IsLocal)
            RunFlowRuntime.OnLocalWarp(__instance, position);
    }
}

/// <summary>Campfire segment advance: arm the fade; it starts once the fog has caught up, 1 s before
/// the old segment is swapped out (MapHandler.cs:703-714). A/MapHandler.cs:680
/// <c>public void GoToSegment(Segment s)</c>, advancing only when <c>s &gt; currentSegment</c> (L682).</summary>
[HarmonyPatch]
internal static class GoToSegmentPatch
{
    private static MethodBase TargetMethod() =>
        GameMember.Method(typeof(MapHandler), "GoToSegment", new[] { typeof(Segment) });

    private static void Prefix(Segment s, int ___currentSegment)
    {
        if ((int)s > ___currentSegment)
            RunFlowRuntime.OnSegmentAdvance();
    }
}

/// <summary>The segment swap's wait for the fog (MapHandler.cs:594, the only caller): its enumerator is wrapped so
/// run flow learns the moment it ends, 1 s before the swap, however long the fog takes.
/// A/OrbFogHandler.cs:145 <c>public IEnumerator WaitForFogCatchUp()</c>.</summary>
[HarmonyPatch]
internal static class FogCatchUpPatch
{
    private static MethodBase TargetMethod() =>
        GameMember.Method(typeof(OrbFogHandler), "WaitForFogCatchUp", Type.EmptyTypes);

    private static void Postfix(ref System.Collections.IEnumerator __result) =>
        __result = Then(__result);

    private static System.Collections.IEnumerator Then(System.Collections.IEnumerator wait)
    {
        while (wait.MoveNext())
            yield return wait.Current;
        RunFlowRuntime.OnFogCaughtUp();
    }
}

/// <summary>The segment swap itself: the coroutine shows the new segment here right after hiding the old one
/// (MapHandler.cs:596-598); its only other caller is the same coroutine's re-activation (:631), by then disarmed.
/// A/MapHandler.cs:573 <c>private void ActivateCurrentSegment()</c>.</summary>
[HarmonyPatch]
internal static class SegmentSwapPatch
{
    private static MethodBase TargetMethod() =>
        GameMember.Method(typeof(MapHandler), "ActivateCurrentSegment", Type.EmptyTypes);

    private static void Prefix() => RunFlowRuntime.OnSegmentSwap();
}

/// <summary>Title card of the new segment, the end of the segment swap (MapHandler.cs:830):
/// A/MountainProgressHandler.cs:67 <c>public void SetSegmentComplete(int segment)</c>.</summary>
[HarmonyPatch]
internal static class SegmentTitlePatch
{
    private static MethodBase TargetMethod() =>
        GameMember.Method(
            typeof(MountainProgressHandler),
            "SetSegmentComplete",
            new[] { typeof(int) }
        );

    private static void Postfix() => RunFlowRuntime.OnSegmentTitle();
}

/// <summary>Run end on every client, before MenuWindow.CloseAllWindows (L911) and the switch to the end
/// screen or cutscene (L912-938); also reached through RPCEndGame_ForceWin (L882).
/// A/Character.cs:890 <c>private void RPCEndGame()</c>.</summary>
[HarmonyPatch]
internal static class EndGamePatch
{
    private static MethodBase TargetMethod() =>
        GameMember.Method(typeof(Character), "RPCEndGame", Type.EmptyTypes);

    private static void Prefix() => RunFlowRuntime.OnRunEnd();
}

/// <summary>Credits start after the end screen (win path, MapHandler.cs:643).
/// A/PeakHandler.cs:223 <c>public void EndScreenComplete()</c>.</summary>
[HarmonyPatch]
internal static class CreditsStartPatch
{
    private static MethodBase TargetMethod() =>
        GameMember.Method(typeof(PeakHandler), "EndScreenComplete", Type.EmptyTypes);

    private static void Postfix(PeakHandler __instance) =>
        RunFlowRuntime.OnCreditsStart(__instance);
}

/// <summary>The credits wait for the "AnyKey" action (A/PeakHandler.cs:231-238
/// <c>anyKeyAction.WasPerformedThisFrame()</c>), which never sees XR controllers. While the credits
/// wait, a VR button press counts as that action. Unity.InputSystem
/// <c>InputAction.WasPerformedThisFrame()</c>; every other action and every other moment is untouched.</summary>
[HarmonyPatch]
internal static class CreditsAnyKeyPatch
{
    private static InputAction anyKey;
    private static bool resolved;

    private static MethodBase TargetMethod() =>
        GameMember.Method(typeof(InputAction), "WasPerformedThisFrame", Type.EmptyTypes);

    private static void Postfix(InputAction __instance, ref bool __result)
    {
        if (__result || !RunFlowRuntime.CreditsWaiting || !VRSession.Active)
            return;
        if (!resolved)
        {
            // Same lookup as the game; a rename would silently disable the VR skip, so say so once.
            resolved = InputSystem.actions != null;
            anyKey = resolved ? InputSystem.actions.FindAction("AnyKey") : null;
            if (resolved && anyKey == null)
                Plugin.Log.LogWarning(
                    "[ui.runflow] input action 'AnyKey' not found; VR buttons cannot skip the credits"
                );
        }
        if (anyKey != null && __instance == anyKey && RunFlowRuntime.AnyButtonPressedThisFrame)
            __result = true;
    }
}

/// <summary>Summit portal: in VR the lens-warp volumes stay off and a fade to black follows the hold
/// progress instead (the camera shake at L127 is neutralized by rig.camera). A/Peak/PeakGatePortal.cs:95
/// <c>private void Update()</c> (volumes L97-107, progress L108-117, Interact_CastFinished L76).</summary>
[HarmonyPatch]
internal static class PortalPatch
{
    private static MethodBase TargetMethod() => GameMember.Method(typeof(PeakGatePortal), "Update");

    // The shake at L127 is run flow's while it drives the portal rumble.
    private static void Prefix()
    {
        if (VRSession.Active && RunFlowFeature.Settings.PortalFade.Value)
            RunFlowFeature.ShakeOwnedFrame = Time.frameCount;
    }

    private static void Postfix(PeakGatePortal __instance, float ___progress, bool ___finished)
    {
        RunFlowFeature.ShakeOwnedFrame = -1;
        if (!RunFlowRuntime.PortalProgress(___progress, ___finished))
            return;
        __instance.warpPost.weight = 0f;
        __instance.warpPost.enabled = false;
        __instance.warpPost2.weight = 0f;
        __instance.warpPost2.enabled = false;
    }
}

/// <summary>Rising lava / gloom banners, shown together with <c>AddPerlinShake(5f, 3f)</c>
/// (A/LavaRising.cs:185-191). A/GUIManager.cs:1206 <c>TheLavaRises()</c>, :1217 <c>TheGloomRises()</c>.</summary>
[HarmonyPatch]
internal static class RisingShakePatch
{
    private static MethodBase[] TargetMethods() =>
        new MethodBase[]
        {
            GameMember.Method(typeof(GUIManager), nameof(GUIManager.TheLavaRises)),
            GameMember.Method(typeof(GUIManager), nameof(GUIManager.TheGloomRises)),
        };

    // The game adds the shake right after the banner (A/LavaRising.cs:186, :191), in the same frame.
    private static void Postfix()
    {
        RunFlowFeature.ShakeOwnedFrame = Time.frameCount;
        RunFlowRuntime.Rumble(5f, 3f);
    }
}

/// <summary>Freed soul: <c>AddPerlinShake(6f, 6f)</c> 3 s into the break routine
/// (A/Peak/ScoutmasterSoulPillar.cs:172-173). A/Peak/ScoutmasterSoulPillar.cs:148
/// <c>private IEnumerator BreakTriggeredRoutine(PhotonView)</c>; the postfix runs when the routine starts.</summary>
[HarmonyPatch]
internal static class SoulShakePatch
{
    private static MethodBase TargetMethod() =>
        GameMember.Method(typeof(ScoutmasterSoulPillar), "BreakTriggeredRoutine");

    private static void Postfix() => RunFlowRuntime.RumbleAfter(3f, 6f, 6f);
}

/// <summary>Marks the routine's shake (A/Peak/ScoutmasterSoulPillar.cs:173) as run flow's while each step of the
/// routine runs, so comfort.events does not rumble it a second time. The MoveNext of the iterator behind
/// A/Peak/ScoutmasterSoulPillar.cs:148 <c>private IEnumerator BreakTriggeredRoutine(PhotonView)</c>.</summary>
[HarmonyPatch]
internal static class SoulShakeOwnerPatch
{
    private static MethodBase TargetMethod() =>
        AccessTools.EnumeratorMoveNext(
            GameMember.Method(typeof(ScoutmasterSoulPillar), "BreakTriggeredRoutine")
        );

    private static void Prefix() => RunFlowFeature.ShakeOwnedFrame = Time.frameCount;

    private static void Finalizer() => RunFlowFeature.ShakeOwnedFrame = -1;
}

/// <summary>Run-start wake-up on the beach: Fade mode holds black until the character is up, Skip
/// mode drops the Fall(7) ragdoll the way the game's SkipWakeup tag does (L2032; Fall sends
/// RPCA_Fall, so skipping it sends nothing new). A/Character.cs:2030
/// <c>internal void StartPassedOutOnTheBeach()</c>, called for the local character only (CharacterSpawner.cs:249).</summary>
[HarmonyPatch]
internal static class BeachWakeupPatch
{
    /// <summary>True only inside StartPassedOutOnTheBeach in Skip mode; read by <see cref="BeachFallPatch"/>.</summary>
    internal static bool SkipFall;

    private static MethodBase TargetMethod() =>
        GameMember.Method(typeof(Character), "StartPassedOutOnTheBeach");

    private static void Prefix(Character __instance)
    {
        if (!VRSession.Active || !__instance.photonView.IsMine)
            return;
        var mode = RunFlowFeature.Settings.BeachWakeup.Value;
        if (mode == BeachWakeupMode.Skip)
            SkipFall = true;
        else if (mode == BeachWakeupMode.Fade)
            RunFlowRuntime.OnBeachWakeup(__instance);
    }

    private static void Postfix(Character __instance)
    {
        if (!SkipFall)
            return;
        SkipFall = false;
        __instance.data.passedOutOnTheBeach = 0f;
    }
}

/// <summary>Skip mode only: drop the beach Fall(7). A/Character.cs:1732
/// <c>internal void Fall(float seconds, float screenShake = 0f)</c>.</summary>
[HarmonyPatch]
internal static class BeachFallPatch
{
    private static MethodBase TargetMethod() =>
        GameMember.Method(typeof(Character), "Fall", new[] { typeof(float), typeof(float) });

    private static bool Prefix() => !BeachWakeupPatch.SkipFall;
}

/// <summary>The eyelid full-screen renderer feature is a flat overlay; in VR (Fade or Skip mode) it
/// stays off and the run-flow fade covers the wake-up. A/EyeBlinkController.cs:56
/// <c>private void Update()</c> (feature toggled at L73/L82, found in Start L32-39).</summary>
[HarmonyPatch]
internal static class EyeBlinkPatch
{
    private static AccessTools.FieldRef<EyeBlinkController, ScriptableRendererFeature> feature;

    private static MethodBase TargetMethod() =>
        GameMember.Method(typeof(EyeBlinkController), "Update");

    /// <summary>Resolve the private feature field (A/EyeBlinkController.cs:20) inside Install.</summary>
    internal static void Resolve() =>
        feature = GameMember.Field<EyeBlinkController, ScriptableRendererFeature>(
            "rendererFeature"
        );

    private static bool Prefix(EyeBlinkController __instance)
    {
        if (
            !VRSession.Active
            || RunFlowFeature.Settings.BeachWakeup.Value == BeachWakeupMode.Vanilla
        )
            return true;
        var f = feature(__instance);
        if (f != null && f.isActive)
        {
            f.SetActive(false);
            __instance.eyeBlinkMaterial.SetFloat("_EyeOpen", 1f);
        }
        return false;
    }
}
