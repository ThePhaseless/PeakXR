using System;
using UnityEngine.XR;

namespace PeakXR.Interact;

/// <summary>Short controller vibrations through <see cref="InputDevices"/>. Does nothing when the device or runtime
/// has no impulse support, and never throws; returns whether an impulse went out (the cue capture reads it). The device and its impulse support are cached per hand (the stamina
/// beat pulses every frame) and looked up again once the handle goes invalid. <c>Interaction.Haptics</c> off mutes
/// every PeakXR vibration, refusals and UI ticks alike (decision D9); the cues' rings and sounds still play.</summary>
internal static class Haptics
{
    private static InputDevice leftDevice;
    private static InputDevice rightDevice;
    private static bool leftImpulse;
    private static bool rightImpulse;
    private static bool loggedFalse;

    /// <summary>The player turned controller vibration off (<c>Interaction.Haptics</c>).</summary>
    public static bool Muted => !InteractConfig.Instance.Haptics;

    public static bool Pulse(bool left, float amplitude, float seconds)
    {
        var cfg = InteractConfig.Instance;
        if (!cfg.Haptics || amplitude <= 0f)
            return false;
        try
        {
            ref var device = ref left ? ref leftDevice : ref rightDevice;
            ref bool impulse = ref left ? ref leftImpulse : ref rightImpulse;
            if (!device.isValid)
            {
                device = InputDevices.GetDeviceAtXRNode(left ? XRNode.LeftHand : XRNode.RightHand);
                impulse =
                    device.isValid
                    && device.TryGetHapticCapabilities(out var caps)
                    && caps.supportsImpulse;
            }
            if (!impulse)
                return false;
            float a = amplitude * cfg.HapticStrength;
            // Under Unity OpenXR SendHapticImpulse returns false even when the runtime plays the impulse (VR run 30:
            // the vr-test-mcp haptic log had the pulses), so a valid impulse-capable device that took the call counts
            // as delivered. The quirk is logged once.
            if (!device.SendHapticImpulse(0, a > 1f ? 1f : a, seconds) && !loggedFalse)
            {
                loggedFalse = true;
                Plugin.Log.LogInfo(
                    "[interact] SendHapticImpulse returned false on an impulse-capable device; counted as sent"
                );
            }
            return true;
        }
        catch (Exception)
        {
            // Haptics are a nicety; some runtimes throw from the legacy XR input path.
            return false;
        }
    }
}
