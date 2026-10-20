using System;
using UnityEngine.XR;

namespace PeakXR.Interact;

/// <summary>Short controller vibrations through <see cref="InputDevices"/>. Does nothing when the device or runtime
/// has no impulse support, and never throws.</summary>
internal static class Haptics
{
    public static void Pulse(bool left, float amplitude, float seconds)
    {
        var cfg = InteractConfig.Instance;
        if (!cfg.Haptics || amplitude <= 0f)
            return;
        try
        {
            var device = InputDevices.GetDeviceAtXRNode(left ? XRNode.LeftHand : XRNode.RightHand);
            if (
                !device.isValid
                || !device.TryGetHapticCapabilities(out var caps)
                || !caps.supportsImpulse
            )
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
