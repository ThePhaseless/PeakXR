using System.Reflection;
using HarmonyLib;
using Peak.Network;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.UI.Affordances;
using PeakXR.UI.World;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.UI;
using Zorro.UI.Modal;

namespace PeakXR.UI.Menu;

/// <summary>
/// Menu presses the game ignores while it connects or loads get the wait cue at the laser hand, with the pressed
/// control turned red, instead of nothing.
/// </summary>
internal static class MenuWait
{
    public static void Refuse()
    {
        if (!VRSession.Active)
            return;
        var control = LaserPointer.PressedControl;
        var rig = VRRig.Instance;
        var hand = rig != null ? rig.Hand(LaserPointer.PressLeft) : null;
        var at =
            control != null ? control.transform.position
            : hand != null ? hand.position
            : Vector3.zero;
        Affordance.Deny(
            LaserPointer.PressLeft,
            at,
            DenyReason.NotNow,
            default,
            control != null ? control.targetGraphic : null
        );
    }
}

/// <summary>
/// Title buttons that do nothing while a room is being entered (<c>NetworkingUtilities.EnteringRoom</c>):
/// A/MainMenuMainPage.cs:218-221 SettingsClicked, :237-239 ContinueFromQuicksaveClicked, :253-258
/// StartMultiplayerLobby (Play), :344-349 JoinClicked; A/MainMenuJoinRoomPage.cs:104-114 OnJoinButtonClicked,
/// :116-126 OnBackButtonClicked. Read-only: the vanilla early return still runs.
/// </summary>
[HarmonyPatch]
internal static class EnteringRoomWaitPatch
{
    private static MethodBase[] TargetMethods() =>
        new[]
        {
            GameMember.Method(typeof(MainMenuMainPage), "SettingsClicked"),
            GameMember.Method(typeof(MainMenuMainPage), "ContinueFromQuicksaveClicked"),
            GameMember.Method(typeof(MainMenuMainPage), "StartMultiplayerLobby"),
            GameMember.Method(typeof(MainMenuMainPage), "JoinClicked"),
            GameMember.Method(typeof(MainMenuJoinRoomPage), "OnJoinButtonClicked"),
            GameMember.Method(typeof(MainMenuJoinRoomPage), "OnBackButtonClicked"),
        };

    private static void Prefix()
    {
        if (NetworkingUtilities.EnteringRoom)
            MenuWait.Refuse();
    }
}

/// <summary>
/// The boarding pass's Start while a load already runs: A/AirportCheckInKiosk.cs:117-123 <c>StartGame(int)</c> does
/// nothing when <c>LoadingScreenHandler.loading</c>. Read-only.
/// </summary>
[HarmonyPatch]
internal static class LoadingWaitPatch
{
    private static MethodBase TargetMethod() =>
        GameMember.Method(typeof(AirportCheckInKiosk), "StartGame", new[] { typeof(int) });

    private static void Prefix()
    {
        if (LoadingScreenHandler.loading)
            MenuWait.Refuse();
    }
}

/// <summary>
/// A game modal opening (A/Zorro.UI.Modal/Modal.cs:64-75 <c>Open</c>, behind every <c>OpenModal</c>) rings the notice
/// bell once the panel stands in the world, with the would-work ring on its first button, so a question that pops
/// up beside or behind the player is heard and found.
/// </summary>
[HarmonyPatch]
internal static class ModalBellPatch
{
    private static Modal pending;
    private static int openedFrame;

    // A modal not placed by ui.world within this many frames rings where it is.
    private const int PlaceFrames = 30;

    private static MethodBase TargetMethod() => GameMember.Method(typeof(Modal), "Open");

    private static void Postfix(Modal __instance)
    {
        if (!VRSession.Active)
            return;
        pending = __instance;
        openedFrame = Time.frameCount;
    }

    /// <summary>Called every frame by ui.menu's runtime; does nothing unless a modal just opened.</summary>
    public static void Tick()
    {
        // Open's ClearChildren destroys the previous modal's buttons only at frame end (Zorro.Core
        // TransformExtensions.cs:56-61), and ui.world places the panel after this runs, so wait a frame.
        if (pending == null || Time.frameCount == openedFrame)
            return;
        var canvas = pending.GetComponentInChildren<Canvas>(true);
        if (canvas == null)
            canvas = pending.GetComponentInParent<Canvas>();
        if (
            canvas != null
            && !WorldCanvases.IsConverted(canvas.rootCanvas)
            && Time.frameCount - openedFrame < PlaceFrames
        )
            return;
        var button =
            pending.bodyParent != null ? pending.bodyParent.GetComponentInChildren<Button>() : null;
        var at =
            button != null ? button.transform.position
            : pending.headerParent != null ? pending.headerParent.position
            : pending.transform.position;
        pending = null;
        Affordance.Inform(
            LaserPointer.PressLeft,
            at,
            DenyReason.NotNow,
            button != null ? DenyHint.On(button.transform) : default
        );
    }
}
