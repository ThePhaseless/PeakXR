using System;
using System.Runtime.CompilerServices;
using PeakXR.VR;

namespace PeakXR.XR;

/// <summary>Options the bootstrap passes to a backend, in plain types so this file never touches the
/// XR packages.</summary>
sealed class XRStartOptions
{
    /// <summary>0 = none, 1 = 16-bit, 2 = 24-bit (OpenXRSettings.DepthSubmissionMode order).</summary>
    public int DepthSubmission;
    public bool UseRuntimePredictedTime;
}

/// <summary>
/// One XR provider (OpenXR or OpenVR). Concrete backends live in their own files and are the only
/// code that names types from the XR packages; this base and <see cref="XRBackends"/> do not, so a
/// missing RuntimeDeps DLL surfaces as a catchable exception from <see cref="XRBackends.Create"/>
/// instead of breaking the plugin assembly.
/// </summary>
abstract class XRBackend
{
    public abstract VRRuntime Runtime { get; }

    /// <summary>Creates the XR Management objects and the loader and initializes it (synchronous).</summary>
    public abstract bool Initialize(XRStartOptions options);

    /// <summary>Starts the display and input subsystems.</summary>
    public abstract void StartSubsystems();

    /// <summary>Stops subsystems and deinitializes the loader. Safe to call more than once.</summary>
    public abstract void Shutdown();

    /// <summary>Called once the display runs: provider-specific settings that only apply while running.</summary>
    public virtual void OnRunning() { }

    /// <summary>One line for the log: runtime name and version where the provider exposes them.</summary>
    public virtual string Describe() => Runtime.ToString();
}

/// <summary>Factory that keeps every XR-typed constructor behind a non-inlined call.</summary>
static class XRBackends
{
    /// <summary>Returns null when this build has no such backend; throws when its DLLs are missing.</summary>
    public static XRBackend Create(VRRuntime runtime) =>
        runtime switch
        {
            VRRuntime.OpenXR => CreateOpenXR(),
            VRRuntime.OpenVR => CreateOpenVR(),
            _ => null,
        };

    [MethodImpl(MethodImplOptions.NoInlining)]
    static XRBackend CreateOpenXR()
    {
#if PEAKXR_OPENXR
        return new OpenXRBackend();
#else
        Plugin.Log.LogWarning("[xr.bootstrap] This build has no OpenXR backend (built without `mise run deps`)");
        return null;
#endif
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static XRBackend CreateOpenVR()
    {
#if PEAKXR_OPENVR
        return new OpenVRBackend();
#else
        Plugin.Log.LogWarning("[xr.bootstrap] This build has no OpenVR backend (built without `mise run deps`)");
        return null;
#endif
    }

    /// <summary>Assemblies a backend needs, in load order.</summary>
    public static string[] RequiredAssemblies(VRRuntime runtime) =>
        runtime switch
        {
            VRRuntime.OpenXR => [XRAssemblyLoader.Management, XRAssemblyLoader.OpenXR],
            VRRuntime.OpenVR => [XRAssemblyLoader.Management, XRAssemblyLoader.OpenVR],
            _ => Array.Empty<string>(),
        };
}

/// <summary>
/// Session state reported by the provider, written from the backends and read by the bootstrap
/// driver. Plain fields so readers never touch XR types.
/// </summary>
static class XRSessionState
{
    /// <summary>Latest OpenXR XrSessionState (1 idle .. 5 focused .. 8 exiting), 0 when unknown.</summary>
    public static int OpenXRState;

    /// <summary>Set when the runtime asked the app to quit or lost the instance.</summary>
    public static bool ExitRequested;

    public static void Reset()
    {
        OpenXRState = 0;
        ExitRequested = false;
    }
}
