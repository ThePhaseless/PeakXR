using System.Reflection;
using HarmonyLib;
using PeakXR.Framework;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.UI;

namespace PeakXR.UI.World;

/// <summary>Every canvas the game authors has a CanvasScaler, so its OnEnable is a generic
/// "canvas became active" hook. UnityEngine.UI CanvasScaler.OnEnable (protected override).</summary>
[HarmonyPatch(typeof(CanvasScaler), "OnEnable")]
internal static class CanvasScalerEnablePatch
{
    private static void Postfix(CanvasScaler __instance) =>
        WorldCanvases.Track(__instance.GetComponent<Canvas>());
}

/// <summary>Windows inside an always-active canvas (passport, boarding pass) re-spawn in front of the
/// head when opened. A/MenuWindow.cs:80 <c>internal virtual void Open()</c>.</summary>
[HarmonyPatch]
internal static class MenuWindowOpenPatch
{
    private static MethodBase TargetMethod() => GameMember.Method(typeof(MenuWindow), "Open");

    // The window may sit on a parent of its canvas (PassportManager) or inside it (EndScreen).
    private static void Postfix(MenuWindow __instance) =>
        WorldCanvases.Reopen(
            __instance.GetComponentInParent<Canvas>()
                ?? __instance.GetComponentInChildren<Canvas>(true)
        );
}

/// <summary>Nested canvases spawned at runtime (dropdown lists and their blockers) must raycast through
/// the laser camera too. UnityEngine.UI GraphicRaycaster.eventCamera getter.</summary>
[HarmonyPatch(typeof(GraphicRaycaster), nameof(GraphicRaycaster.eventCamera), MethodType.Getter)]
internal static class EventCameraPatch
{
    private static void Postfix(GraphicRaycaster __instance, ref Camera __result)
    {
        if (!UIWorldRuntime.Ready || __result == LaserPointer.EventCamera)
            return;
        var canvas = __instance.GetComponent<Canvas>();
        if (canvas != null && WorldCanvases.IsConverted(canvas.rootCanvas))
            __result = LaserPointer.EventCamera;
    }
}

/// <summary>Status-effect quads, made head-locked per eye. A/CameraQuad.cs:11 <c>private void LateUpdate()</c>.</summary>
[HarmonyPatch]
internal static class CameraQuadPatch
{
    private static MethodBase TargetMethod() => GameMember.Method(typeof(CameraQuad), "LateUpdate");

    private static bool Prefix(CameraQuad __instance)
    {
        if (!UIWorldRuntime.Ready)
            return true;
        StatusOverlays.Place(
            __instance.transform,
            VRRig.Instance,
            UIWorldFeature.Settings.StatusOverlays.Value
        );
        return false;
    }
}

/// <summary>Name tags are placed by WorldToScreenPoint, meaningless in stereo; put each tag at the
/// player's head in the world instead. A/UIPlayerNames.cs:46 <c>UpdateName</c> (screen projection at L57).</summary>
[HarmonyPatch]
internal static class PlayerNamePatch
{
    private static MethodBase TargetMethod() =>
        GameMember.Method(typeof(UIPlayerNames), nameof(UIPlayerNames.UpdateName));

    private static void Postfix(UIPlayerNames __instance, int index, Vector3 position)
    {
        if (!UIWorldRuntime.Ready || Character.localCharacter == null)
            return;
        var names = __instance.playerNameText;
        if (index < 0 || index >= names.Length || names[index] == null)
            return;

        var tag = names[index].transform;
        var head = VRRig.Instance.Head;
        var toTag = position - head.position;
        var distance = toTag.magnitude;
        if (distance < 0.01f)
            return;
        tag.SetPositionAndRotation(position, Quaternion.LookRotation(toTag, Vector3.up));

        // Roughly constant angular size: grows with distance beyond a metre.
        var size = UIWorldFeature.Settings.NameTagScale.Value * 0.0006f * Mathf.Max(1f, distance);
        var parent = tag.parent != null ? tag.parent.lossyScale.x : 1f;
        var s = Mathf.Abs(parent) > 1e-6f ? size / parent : size;
        tag.localScale = new Vector3(s, s, s);
    }
}
