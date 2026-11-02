using System.Collections.Generic;
using HarmonyLib;
using PeakXR.VR;
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
            __result.WithControlsExcluding("<XRController>");
    }
}
