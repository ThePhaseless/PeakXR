using System;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using PeakXR.Framework;

namespace PeakXR.UI.Keyboard;

/// <summary>
/// ui.keyboard: an in-headset keyboard for the game's text entry. It opens when a TMP input field on a VR menu
/// is selected (room code on the join page, report reason) and types into that field through the field's own
/// key handling, so validation and character limits stay the game's. Platform text dialogs (Steam's gamepad
/// keyboard, which a headset cannot show) open the same keyboard and return its text to the game's callback.
/// Keys are pressed with the menu laser or by poking them with a controller.
/// </summary>
public sealed class UIKeyboardFeature : Feature
{
    public override string Id => "ui.keyboard";

    public override int Order => 59;

    internal static ConfigEntry<float> Distance;
    internal static ConfigEntry<float> Width;
    internal static ConfigEntry<float> Tilt;
    internal static ConfigEntry<bool> Poke;
    internal static ConfigEntry<float> Haptics;

    protected internal override void Install()
    {
        const string section = "UI.Keyboard";
        Distance = Plugin.Settings.Bind(
            section,
            "KeyboardDistance",
            0.5f,
            Range(
                "Distance of the keyboard from your head, in metres. Within arm's reach you can poke the keys.",
                0.3f,
                1.5f
            )
        );
        Width = Plugin.Settings.Bind(
            section,
            "KeyboardWidth",
            0.6f,
            Range("Width of the keyboard, in metres.", 0.3f, 1.2f)
        );
        Tilt = Plugin.Settings.Bind(
            section,
            "KeyboardTilt",
            30f,
            Range("Degrees below eye level where the keyboard appears.", 0f, 60f)
        );
        Poke = Plugin.Settings.Bind(
            section,
            "PokeKeys",
            true,
            "Press keys by touching them with a controller tip, as well as with the laser."
        );
        Haptics = Plugin.Settings.Bind(
            section,
            "Haptics",
            0.3f,
            Range("Strength of the tick when you poke a key. 0 turns it off.", 0f, 1f)
        );

        Patch(typeof(PlatformDialogPatch));
        VRKeyboard.Create();
    }

    private static ConfigDescription Range(string text, float min, float max) =>
        new(text, new AcceptableValueRange<float>(min, max));
}

/// <summary>
/// Steam's gamepad text dialog (Big Picture, Steam Deck; a no-op on desktop) cannot be seen in a headset. In VR
/// the keyboard takes its place and answers the game's callback with Succeeded or Aborted. Callers:
/// A/MainMenuJoinRoomPage.cs:117, A/Portningsbolaget/ReportPlayerPopup.cs:204,
/// A/Portningsbolaget/ReportPlayerReasonField.cs:47. Target: SteamCommon.dll
/// <c>SteamRuntimeManager.OpenDialog(string title, DialogType dialogType, Action&lt;string, DialogueResult&gt; onDone)</c>.
/// Skipped (logged) when the platform layer is not Steam's.
/// </summary>
[HarmonyPatch]
internal static class PlatformDialogPatch
{
    private static MethodBase target;

    private static bool Prepare()
    {
        // SteamRuntimeManager lives in SteamCommon.dll, which the plugin does not reference.
#pragma warning disable BHA0003
        var type = AccessTools.TypeByName("SteamRuntimeManager");
#pragma warning restore BHA0003
        target = type != null ? AccessTools.Method(type, "OpenDialog") : null;
        if (target == null)
            Plugin.Log.LogWarning(
                "[ui.keyboard] SteamRuntimeManager.OpenDialog not found; platform text dialogs stay on the desktop"
            );
        return target != null;
    }

    private static MethodBase TargetMethod() => target;

    private static bool Prefix(object[] __args)
    {
        if (!VR.VRSession.Active || __args.Length < 3)
            return true;
        var title = __args[0] as string;
        var kind = __args[1]?.ToString();
        var callback = __args[2] as Delegate;
        var limit =
            kind == "Chat" ? 200
            : kind == "JoinRoom" ? JoinCodeLength()
            : 20;
        return !VRKeyboard.TryOpenDialog(title, limit, (text, ok) => Answer(callback, text, ok));
    }

    /// <summary>The join page caps its field at the room code length (A/MainMenuJoinRoomPage.cs:32), the same
    /// cap Steam's dialog uses.</summary>
    private static int JoinCodeLength()
    {
        var page = UnityEngine.Object.FindFirstObjectByType<MainMenuJoinRoomPage>(
            UnityEngine.FindObjectsInactive.Include
        );
        return page != null && page.roomInput != null && page.roomInput.characterLimit > 0
            ? page.roomInput.characterLimit
            : 20;
    }

    private static void Answer(Delegate callback, string text, bool submitted)
    {
        if (callback == null)
            return;
        var args = callback.GetType().GetGenericArguments();
        if (args.Length != 2 || !args[1].IsEnum)
            return;
        var result = Enum.Parse(args[1], submitted ? "Succeeded" : "Aborted");
        callback.DynamicInvoke(submitted ? text : string.Empty, result);
    }
}
