using System;

namespace PeakXR.VR;

public enum VRRuntime
{
    Disabled,
    OpenXR,
    OpenVR,
}

/// <summary>Process-wide VR state. The XR bootstrap sets it; everything else only reads it.</summary>
public static class VRSession
{
    /// <summary>Runtime chosen by the preloader (env PEAKXR_RUNTIME). Disabled means flat play.</summary>
    public static VRRuntime Requested { get; internal set; }

    /// <summary>True once an XR display subsystem is running and rendering to the headset.</summary>
    public static bool Active { get; internal set; }

    /// <summary>Runtime that actually started (may differ from <see cref="Requested"/> after the
    /// OpenXR to OpenVR fallback). Disabled while flat.</summary>
    public static VRRuntime Running { get; internal set; }

    /// <summary>False while the runtime holds input focus (dashboard open, headset off). Game input
    /// is paused then, rendering continues.</summary>
    public static bool HasFocus { get; internal set; } = true;

    /// <summary>Launch option <c>-peakxr-compat</c>: install every feature, never start XR. Checks that all
    /// patch targets still resolve (after a game update) without a headset or SteamVR.</summary>
    public static bool CompatProbe { get; internal set; }

    // Raised one handler at a time through GetInvocationList in SetActive, which Sonar does not follow.
#pragma warning disable S3264
    public static event Action Started;
    public static event Action Stopped;
#pragma warning restore S3264

    internal static void SetActive(bool active)
    {
        if (Active == active)
            return;
        Active = active;
        var handlers = active ? Started : Stopped;
        if (handlers == null)
            return;
        // One handler at a time with a line before each, so a native crash in one is attributable; a throwing
        // handler must not skip the later ones (rig, render, input restore).
        foreach (var handler in handlers.GetInvocationList())
        {
            var name =
                $"VRSession.{(active ? "Started" : "Stopped")} -> {handler.Method.DeclaringType?.FullName}.{handler.Method.Name}";
            Plugin.Log.LogDebug(name);
            try
            {
                ((Action)handler)();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"{name} threw: {e}");
            }
        }
    }

    internal static VRRuntime ReadRequestedRuntime()
    {
        var value = Environment.GetEnvironmentVariable("PEAKXR_RUNTIME");
        if (value == null)
            Plugin.Log.LogWarning(
                "PEAKXR_RUNTIME is not set: the PeakXR preloader (BepInEx/patchers/PeakXR) is missing, so VR stays "
                    + "off. Install the whole mod, including the patchers folder."
            );
        return Enum.TryParse(value, true, out VRRuntime runtime) ? runtime : VRRuntime.Disabled;
    }
}
