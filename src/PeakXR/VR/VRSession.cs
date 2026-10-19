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

    public static event Action Started;
    public static event Action Stopped;

    internal static void SetActive(bool active)
    {
        if (Active == active)
            return;
        Active = active;
        (active ? Started : Stopped)?.Invoke();
    }

    internal static VRRuntime ReadRequestedRuntime()
    {
        var value = Environment.GetEnvironmentVariable("PEAKXR_RUNTIME");
        return Enum.TryParse(value, true, out VRRuntime runtime) ? runtime : VRRuntime.Disabled;
    }
}
