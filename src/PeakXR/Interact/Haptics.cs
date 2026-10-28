using System;
using PeakXR.Core;
using UnityEngine;
using UnityEngine.XR;

namespace PeakXR.Interact;

/// <summary>Short controller vibrations through <see cref="InputDevices"/>. Does nothing when the device or runtime
/// has no impulse support, and never throws; returns whether an impulse went out (the cue capture reads it). The device and its impulse support are cached per hand (the stamina
/// beat pulses every frame) and looked up again once the handle goes invalid. <c>Interaction.Haptics</c> off mutes
/// every PeakXR vibration, refusals and UI ticks alike (decision D9); the cues' rings and sounds still play.
/// <para>One slider, <c>Interaction.HapticStrength</c>, scales every pulse (<see cref="HapticScale"/>). Each hand is
/// one channel (<see cref="HapticChannel"/>): a pulse of lower <see cref="HapticPriority"/> than the one still playing
/// is dropped, and ambient feel ducks under cues, so a heartbeat never cuts a refusal's triple tick.</para></summary>
internal static class Haptics
{
    private static InputDevice leftDevice;
    private static InputDevice rightDevice;
    private static bool leftImpulse;
    private static bool rightImpulse;
    private static bool loggedFalse;
    private static readonly HapticChannel[] Channels = new HapticChannel[2];
    private static readonly float[] SentAt = NewTimes();

    /// <summary>Pulses dropped because a higher-priority one was playing on that hand (self-test).</summary>
    public static int Dropped { get; private set; }

    private static float[] NewTimes()
    {
        var t = new float[2 * ((int)HapticPriority.Alert + 1)];
        for (int i = 0; i < t.Length; i++)
            t[i] = float.NegativeInfinity;
        return t;
    }

    /// <summary>The player turned controller vibration off (<c>Interaction.Haptics</c>).</summary>
    public static bool Muted => !InteractConfig.Instance.Haptics;

    /// <summary>A gameplay cue (contact, a grab, a use).</summary>
    public static bool Pulse(bool left, float amplitude, float seconds) =>
        Send(left, amplitude, seconds, HapticPriority.Cue, HapticScale.Cue);

    /// <summary>A gameplay pulse at <paramref name="priority"/>: Alert for refusals and danger, Ambient for
    /// background feel (heartbeat, wind, texture, shiver, warmth). <paramref name="duckTail"/> false for a tick that
    /// repeats faster than <see cref="HapticChannel.DuckTail"/> (a hold's progress), so background feel resumes
    /// between ticks.</summary>
    public static bool Pulse(
        bool left,
        float amplitude,
        float seconds,
        HapticPriority priority,
        bool duckTail = true
    ) => Send(left, amplitude, seconds, priority, HapticScale.Cue, duckTail);

    /// <summary>A UI tick (menus, wheels, keyboard, wrist status): the same slider and mute, at the UI scale.</summary>
    public static bool Ui(bool left, float amplitude, float seconds) =>
        Send(left, amplitude, seconds, HapticPriority.Cue, HapticScale.Ui);

    /// <summary>Holds that hand for a pattern's whole length (the refusal triple), gaps included, so nothing lower
    /// slips in between its pulses.</summary>
    public static void Hold(bool left, HapticPriority priority, float seconds) =>
        Channels[left ? 0 : 1].Hold(priority, Time.unscaledTime, seconds);

    /// <summary>When a pulse of <paramref name="priority"/> last went out on that hand (unscaled time).</summary>
    public static float LastSent(bool left, HapticPriority priority) =>
        SentAt[(left ? 0 : 1) * ((int)HapticPriority.Alert + 1) + (int)priority];

    private static bool Send(
        bool left,
        float amplitude,
        float seconds,
        HapticPriority priority,
        float scale,
        bool duckTail = true
    )
    {
        var cfg = InteractConfig.Instance;
        if (!cfg.Haptics || amplitude <= 0f || !HapticScale.Plays(cfg.HapticDetail, priority))
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
            float now = Time.unscaledTime;
            if (!Channels[left ? 0 : 1].Allow(priority, now, seconds, duckTail))
            {
                Dropped++;
                return false;
            }
            SentAt[(left ? 0 : 1) * ((int)HapticPriority.Alert + 1) + (int)priority] = now;
            float a = HapticScale.Felt(amplitude, cfg.HapticStrength, scale);
            // Under Unity OpenXR SendHapticImpulse returns false even when the runtime plays the impulse (VR run 30:
            // the vr-test-mcp haptic log had the pulses), so a valid impulse-capable device that took the call counts
            // as delivered. The quirk is logged once.
            if (!device.SendHapticImpulse(0, a, seconds) && !loggedFalse)
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
