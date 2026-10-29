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
    private static void Postfix(MenuWindow __instance)
    {
        var canvas = __instance.GetComponentInParent<Canvas>();
        if (canvas == null)
            canvas = __instance.GetComponentInChildren<Canvas>(true);
        WorldCanvases.Reopen(canvas);
    }
}

/// <summary>Nested canvases spawned at runtime (dropdown lists and their blockers) must raycast through
/// the laser camera too. UnityEngine.UI GraphicRaycaster.eventCamera getter.</summary>
[HarmonyPatch(typeof(GraphicRaycaster), nameof(GraphicRaycaster.eventCamera), MethodType.Getter)]
internal static class EventCameraPatch
{
    // m_Canvas: the raycaster's own cached canvas, filled by the getter before this postfix runs.
    private static void Postfix(Canvas ___m_Canvas, ref Camera __result)
    {
        if (!UIWorldRuntime.Ready || __result == LaserPointer.EventCamera)
            return;
        if (___m_Canvas != null && WorldCanvases.IsConverted(___m_Canvas.rootCanvas))
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
        // The rig's head can be gone for a frame around a scene load: vanilla places the quad then.
        var rig = VRRig.Instance;
        if (!UIWorldRuntime.Ready || rig == null || rig.Head == null)
            return true;
        try
        {
            StatusOverlays.Place(
                __instance.transform,
                rig,
                UIWorldFeature.Settings.StatusOverlays.Value
            );
            return false;
        }
        catch (System.Exception e)
        {
            PatchGuard.Fail<UIWorldFeature>(e);
            return true;
        }
    }
}

/// <summary>Name tags are placed by WorldToScreenPoint, meaningless in stereo; put each tag at the
/// player's head in the world instead. A/UIPlayerNames.cs:46 <c>UpdateName</c> (screen projection at L57), called from
/// A/IsLookedAt.cs:35-49 <c>Update</c>.</summary>
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

        // IsLookedAt.Update calls this before UIWorldRuntime refreshes Ready in LateUpdate; VR may have stopped since.
        var rig = VRRig.Instance;
        var head = rig != null ? rig.Head : null;
        if (head == null)
            return;
        var tag = names[index].transform;
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

/// <summary>In VR no keyboard key glyph shows: the game keeps its KeyboardMouse scheme under VR
/// (ZC/InputHandler.cs:36, default InputScheme.cs:5), so every <c>InputIcon</c> draws a keycap sprite, and only the HUD
/// parts ui.spatial moves hid theirs (SpatialBox). The dying bar's give-up "E", the hotbar's 1-4 and the guidebook's
/// [INTERACT] tags still drew. A/InputIcon.cs:54 <c>OnDeviceChange</c> (on enable and every scheme change, sets the
/// glyph through SetText :103), A/InLineInputPrompts.cs:45 <c>UpdateText</c> (writes sprite tags into the text, :67).</summary>
[HarmonyPatch]
internal static class InputGlyphPatch
{
    private static MethodInfo iconChange;
    private static MethodInfo inlineText;

    private static System.Collections.Generic.IEnumerable<MethodBase> TargetMethods()
    {
        yield return iconChange = GameMember.Method(typeof(InputIcon), "OnDeviceChange");
        yield return inlineText = GameMember.Method(typeof(InLineInputPrompts), "UpdateText");
    }

    private static void Postfix(MonoBehaviour __instance)
    {
        if (!UIWorldRuntime.Ready || !__instance.TryGetComponent(out TMPro.TMP_Text text))
            return;
        if (__instance is InputIcon)
        {
            text.enabled = false;
            return;
        }
        var s = text.text;
        if (s == null)
            return;
        int at;
        bool cut = false;
        while ((at = s.IndexOf("<sprite", System.StringComparison.Ordinal)) >= 0)
        {
            int end = s.IndexOf('>', at);
            s = end < 0 ? s.Remove(at) : s.Remove(at, end - at + 1);
            cut = true;
        }
        if (cut)
            text.text = s;
    }

    /// <summary>Runs every active glyph's own update again when the VR UI starts or stops: hidden for the headset,
    /// the game's own keycaps back for the monitor.</summary>
    internal static void Refresh()
    {
        if (iconChange == null || inlineText == null)
            return;
        var scheme = new object[]
        {
            Zorro.ControllerSupport.InputHandler.GetCurrentUsedInputScheme(),
        };
        foreach (var icon in Object.FindObjectsByType<InputIcon>(FindObjectsSortMode.None))
            iconChange.Invoke(icon, scheme);
        foreach (
            var inline in Object.FindObjectsByType<InLineInputPrompts>(FindObjectsSortMode.None)
        )
            inlineText.Invoke(inline, scheme);
    }
}
