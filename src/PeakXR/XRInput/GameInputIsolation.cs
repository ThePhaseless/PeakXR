using System.Collections.Generic;
using PeakXR.VR;
using UnityEngine.InputSystem;
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
        foreach (var device in InputSystem.devices)
            Disable(device);
    }

    private static void Stop()
    {
        InputSystem.onDeviceChange -= OnDeviceChange;
        foreach (var device in Disabled)
        {
            if (device.added)
                InputSystem.EnableDevice(device);
        }
        Disabled.Clear();
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
