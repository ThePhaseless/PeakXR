using System;
using UnityEngine.XR;

namespace PeakXR.UI.Common;

/// <summary>Short UI ticks through <see cref="InputDevices"/>, independent of the interaction feature so
/// menus still tick when it is disabled. Muted with every other vibration by <c>Interaction.Haptics</c> off (one gate,
/// decision D9). Does nothing without impulse support and never throws.</summary>
internal static class UIHaptics
{
    public static void Tick(bool left, float amplitude, float seconds)
    {
        if (amplitude <= 0f || Interact.Haptics.Muted)
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
            device.SendHapticImpulse(0, amplitude > 1f ? 1f : amplitude, seconds);
        }
        catch (Exception)
        {
            // A missed tick is harmless; some runtimes throw from the legacy XR input path.
        }
    }
}
