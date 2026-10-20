using System;
using UnityEngine.XR;

namespace PeakXR.Interact;

/// <summary>Short controller vibrations through <see cref="InputDevices"/>. Does nothing when the device or runtime
/// has no impulse support, and never throws. The device and its impulse support are cached per hand (the stamina
/// beat pulses every frame) and looked up again once the handle goes invalid.</summary>
internal static class Haptics
{
    private static InputDevice leftDevice;
    private static InputDevice rightDevice;
    private static bool leftImpulse;
    private static bool rightImpulse;

    public static void Pulse(bool left, float amplitude, float seconds)
    {
        var cfg = InteractConfig.Instance;
        if (!cfg.Haptics || amplitude <= 0f)
            return;
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
                return;
            float a = amplitude * cfg.HapticStrength;
            device.SendHapticImpulse(0, a > 1f ? 1f : a, seconds);
        }
        catch (Exception)
        {
            // Haptics are a nicety; some runtimes throw from the legacy XR input path.
        }
    }
}
