using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using PeakXR.XR;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.XR;

namespace PeakXR.XRInput;

/// <summary>
/// Keeps the game's own Input System actions off the XR controllers while VR runs. PEAK's project-wide action asset
/// ships an "XR" control scheme (data.unity3d: Jump on <c>&lt;XRController&gt;/secondaryButton</c>, UsePrimary and
/// UseSecondary on <c>{PrimaryAction}</c>, Move on <c>{Primary2DAxis}</c>, UI Click on <c>trigger</c>), so either
/// B button jumped through vanilla <c>CharacterInput.Sample</c> on top of the PeakXR layout. PeakXR reads the
/// controllers through <c>UnityEngine.XR.InputDevices</c>, so the Input System copies are disabled in the frontend
/// only (no command reaches the XR provider) and keyboard and gamepad keep working. Binding overrides would do the
/// same but the game saves overrides with its rebinds (Rebinding.cs:25).
/// Disabled in the frontend only stops <c>InputUser</c> switching to the XR scheme: InputManager.OnUpdate drops events
/// only for a device disabled in the runtime or in the background, so state kept flowing, the game's UI module read
/// the trigger, and one pull was two clicks (#133, #134). The event listener drops that state; it never reaches a
/// provider either, and PeakXR's own reads do not go through it.
/// </summary>
internal static class GameInputIsolation
{
    private static readonly List<InputDevice> Disabled = new();

    public static void Install()
    {
        VRSession.Started += Start;
        VRSession.Stopped += Stop;
        if (VRSession.Active)
            Start();
    }

    private static void Start()
    {
        InputSystem.onDeviceChange += OnDeviceChange;
        InputSystem.onEvent += DropControllerState;
        foreach (var device in InputSystem.devices)
            Disable(device);
    }

    private static void Stop()
    {
        InputSystem.onDeviceChange -= OnDeviceChange;
        InputSystem.onEvent -= DropControllerState;
        foreach (var device in Disabled)
        {
            if (device.added)
                InputSystem.EnableDevice(device);
        }
        Disabled.Clear();
    }

    private static void DropControllerState(InputEventPtr eventPtr, InputDevice device)
    {
        if (
            device is XRController
            && (eventPtr.type == StateEvent.Type || eventPtr.type == DeltaStateEvent.Type)
        )
            eventPtr.handled = true;
    }

    private static void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (change is InputDeviceChange.Added or InputDeviceChange.Reconnected)
            Disable(device);
    }

    private static void Disable(InputDevice device)
    {
        if (device is not XRController || !device.enabled)
            return;
        InputSystem.DisableDevice(device, keepSendingEvents: true);
        Disabled.Add(device);
        Plugin.Log.LogInfo($"[xr.input] game actions no longer read {device.name}");
    }
}

/// <summary>
/// Keeps XR controls out of the game's rebinding prompt, which would save them over the player's keyboard or gamepad
/// binding (A/PauseMenuRebindKeyPage.cs:66, saved at :114). The operation is an onEvent listener of its own that
/// ignores <c>handled</c>, so it still gets the state <see cref="GameInputIsolation"/> drops, and it takes a control's
/// start actuation from the control's value, frozen at rest: a finger resting on a control reads as a fresh press (#133).
/// The headset goes too, as <c>TrackedDevice</c> covers both: its synthetic, not noisy <c>isTracked</c> is a button whose
/// return from 0 reads as a press, so a player lifting the headset to reach the keyboard would bind it. On an action of no
/// control type, its <c>trackingState</c> ends the rebind within a few frames of opening.
/// Unity.InputSystem <c>InputActionRebindingExtensions.PerformInteractiveRebinding</c>.
/// </summary>
[HarmonyPatch(
    typeof(InputActionRebindingExtensions),
    nameof(InputActionRebindingExtensions.PerformInteractiveRebinding)
)]
internal static class RebindSkipsControllersPatch
{
    private static void Postfix(InputActionRebindingExtensions.RebindingOperation __result)
    {
        if (VRSession.Active)
            __result.WithControlsExcluding("<TrackedDevice>");
    }
}

/// <summary>
/// <see cref="RebindSkipsControllersPatch"/> leaves a controller press on the rebinding prompt doing nothing, so each
/// one gets a refusal saying why and how to leave. Leaving needs help too: the page has no parent, so vanilla's Menu
/// closes the whole pause menu (A/PauseMenuHandler.cs:45-60) without <c>OnPageExit</c>, the rebind stays live and its
/// action disabled (A/PauseMenuRebindKeyPage.cs:65) until the menu reopens. Here Menu, or the dashboard for a
/// controller whose Menu belongs to the runtime, goes through the page's own cancel (:119-124, :132-139), which enables
/// the action and returns to the Controls page. Keyboard and gamepad cancel the operation themselves before this runs.
/// </summary>
[HarmonyPatch]
internal static class RebindPromptPatch
{
    private const string Why =
        "Keyboard or gamepad only\n<size=72%><color=#d8f4ee>Menu button cancels</color></size>";

    private static readonly RebindPromptInput Input = new();
    private static AccessTools.FieldRef<
        PauseMenuRebindKeyPage,
        InputActionRebindingExtensions.RebindingOperation
    > operation;

    public static void Resolve() =>
        operation = GameMember.Field<
            PauseMenuRebindKeyPage,
            InputActionRebindingExtensions.RebindingOperation
        >("rebindOperation");

    private static MethodBase TargetMethod() =>
        GameMember.Method(typeof(PauseMenuHandler), "Update");

    private static bool Prefix(PauseMenuHandler __instance)
    {
        try
        {
            if (!VRSession.Active)
                return true;
            var c = Character.localCharacter;
            var op = __instance.currentPage is PauseMenuRebindKeyPage page ? operation(page) : null;
            if (op == null || !op.started || op.completed || c == null)
            {
                Input.Reset();
                return true;
            }
            switch (
                Input.Step(
                    c.input.pauseWasPressed,
                    !VRSession.HasFocus,
                    XRBootstrapDriver.HeadsetHidden || XRBootstrapDriver.TakenOff,
                    Down(VRInput.Left),
                    Down(VRInput.Right),
                    Time.unscaledDeltaTime
                )
            )
            {
                case RebindPromptStep.Cancel:
                    Plugin.Log.LogInfo(
                        "[xr.input] rebinding prompt cancelled from the controllers"
                    );
                    Input.Reset();
                    __instance.currentPage.OnPageExit();
                    return false;
                case RebindPromptStep.RefuseLeft:
                    Refuse(true);
                    break;
                case RebindPromptStep.RefuseRight:
                    Refuse(false);
                    break;
                default:
                    break;
            }
            return true;
        }
        catch (System.Exception e)
        {
            PatchGuard.Fail<XRInputFeature>(e);
            return true;
        }
    }

    // Menu is left out: it is the way out, and its press lands as a tap on release.
    private static bool Down(in HandState hand) =>
        hand.Trigger >= 0.55f
        || hand.Grip >= 0.6f
        || hand.Primary
        || hand.Secondary
        || hand.StickClick;

    private static void Refuse(bool left)
    {
        var hand = VRRig.Instance != null ? VRRig.Instance.Hand(left) : null;
        Affordance.DenyWithText(
            left,
            hand != null ? hand.position : Vector3.zero,
            DenyReason.Locked,
            Why
        );
    }
}
