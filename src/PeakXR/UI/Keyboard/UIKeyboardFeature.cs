using System;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using PeakXR.Framework;
using PeakXR.UI.World;

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

    public override int Order => 57;

    internal static ConfigEntry<float> Distance;
    internal static ConfigEntry<float> Width;
    internal static ConfigEntry<float> Tilt;
    internal static ConfigEntry<bool> Poke;
    internal static ConfigEntry<float> Haptics;

    /// <summary>Room code length for the join dialog (Portningsbolaget Utilities.ROOM_NAME_LENGTH, read by
    /// A/MainMenuJoinRoomPage.cs:32); Steam's own default of 20 when it cannot be read.</summary>
    internal static int RoomCodeLength = 20;

    protected internal override void Install()
    {
        const string section = "UI.Keyboard";
        Distance = Plugin.Settings.Bind(
            section,
            "KeyboardDistance",
            0.5f,
            Range("Distance of the keyboard from your head, in metres. Within arm's reach you can poke the keys.", 0.3f, 1.5f)
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

        RoomCodeLength = ReadRoomCodeLength() ?? RoomCodeLength;
        CanvasRules.AddCustom(typeof(VRKeyboardPanel).FullName, KeyboardPlacement.Instance);
        Patch(typeof(PlatformDialogPatch));
        VRKeyboard.Create();
    }

    private static int? ReadRoomCodeLength()
    {
        var type = AccessTools.TypeByName("Portningsbolaget.Utilities.Utilities");
        var field = type != null ? AccessTools.Field(type, "ROOM_NAME_LENGTH") : null;
        if (field == null)
            return null;
        var value = field.IsLiteral ? field.GetRawConstantValue() : field.GetValue(null);
        return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
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
        var type = AccessTools.TypeByName("SteamRuntimeManager");
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
            : kind == "JoinRoom" ? UIKeyboardFeature.RoomCodeLength
            : 20;
        return !VRKeyboard.TryOpenDialog(title, limit, (text, ok) => Answer(callback, text, ok));
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
