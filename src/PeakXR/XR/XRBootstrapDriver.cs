using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PeakXR.Core;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace PeakXR.XR;

/// <summary>Engine-level XR objects of the running session, for other features (plain UnityEngine.XR
/// types only, never package types).</summary>
static class XRRuntimeInfo
{
    public static XRDisplaySubsystem Display;
    public static XRInputSubsystem Input;

    /// <summary>True when the tracking space has its origin on the floor (eye height is measurable).</summary>
    public static bool FloorTracking;

    /// <summary>The running runtime's name and version (<c>XRBackend.Describe</c>), for bug reports.</summary>
    public static string Description = "none";
}

/// <summary>
/// Runs the start sequence one frame after load (XR Management needs the graphics device ready),
/// then watches the session: display running, runtime focus, devices connecting.
/// </summary>
sealed class XRBootstrapDriver : MonoBehaviour
{
    const string Tag = "[xr.bootstrap]";

    readonly List<XRDisplaySubsystem> displays = new();
    readonly List<XRInputSubsystem> inputs = new();
    XRBackend backend;
    bool started;
    bool lastFocus = true;

    /// <summary>This launch's crash guard is written and not yet cleared (<see cref="XRStartSentinel"/>).</summary>
    bool guarded;

    IEnumerator Start()
    {
        yield return null;
        XRSessionState.Reset();
        LogDescriptors();
        WarnGraphicsApi();

        var order = new List<VRRuntime> { VRSession.Requested };
        if (VRSession.Requested == VRRuntime.OpenXR && BootstrapFeature.FallbackToOpenVR.Value)
        {
            if (HasDescriptor("OpenVR Display"))
                order.Add(VRRuntime.OpenVR);
            else
                Plugin.Log.LogInfo(
                    $"{Tag} OpenVR fallback not registered by the preloader; no fallback after OpenXR"
                );
        }

        foreach (var runtime in order)
        {
            Plugin.Log.LogInfo($"{Tag} Trying {runtime}");
            var candidate = Prepare(runtime);
            if (candidate == null)
                continue;

            // One crash guard per launch, from the first native init until a runtime runs (or all failed cleanly), so
            // OpenXR failing cleanly and OpenVR then crashing still counts as one crashed start.
            if (!guarded)
            {
                guarded = true;
                XRStartSentinel.Starting();
            }
            bool up = TryInitialize(candidate) && TryStartSubsystems(candidate);
            if (!up)
            {
                SafeShutdown(candidate);
                continue;
            }

            var deadline = Time.realtimeSinceStartup + BootstrapFeature.StartTimeout.Value;
            while (FindRunningDisplay() == null && Time.realtimeSinceStartup < deadline)
                yield return null;

            if (FindRunningDisplay() == null)
            {
                Plugin.Log.LogError(
                    $"{Tag} {runtime}: no running XR display after {BootstrapFeature.StartTimeout.Value:0} s "
                        + "(is the headset on and the runtime (SteamVR) started?)"
                );
                SafeShutdown(candidate);
                continue;
            }

            if (!Activate(candidate))
                continue;
            float since = Time.realtimeSinceStartup;
            int frames = 0;
            XRStartGuard.Close close;
            string signal;
            while (
                (
                    close = XRStartGuard.CloseAt(
                        candidate.FramesShown(out signal),
                        frames,
                        Time.realtimeSinceStartup - since
                    )
                ) == XRStartGuard.Close.Open
            )
            {
                yield return null;
                frames++;
            }
            float waited = Time.realtimeSinceStartup - since;
            if (close == XRStartGuard.Close.Shown)
                Plugin.Log.LogInfo(
                    $"{Tag} Crash guard closed: {signal} after {frames} frames ({waited:0.0} s)"
                );
            else
                Plugin.Log.LogWarning(
                    $"{Tag} Crash guard closed after {frames} rendered frames ({waited:0.0} s) without the runtime "
                        + $"reporting a frame shown ({signal}); the process survived the start"
                );
            EndGuard();
            yield break;
        }

        EndGuard();
        Plugin.Log.LogError($"{Tag} No XR runtime started; VR stays off and the game runs flat");
        VRSession.Running = VRRuntime.Disabled;
    }

    /// <summary>The start came back (a running session's frames reported shown or rendered past the timeout, or
    /// every runtime failed cleanly, or the game quit normally): this launch did not crash.</summary>
    void EndGuard()
    {
        if (!guarded)
            return;
        guarded = false;
        XRStartSentinel.Finished();
    }

    static bool TryStartSubsystems(XRBackend candidate)
    {
        try
        {
            candidate.StartSubsystems();
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"{Tag} {candidate.Runtime}: starting subsystems threw: {e}");
            return false;
        }
    }

    static XRBackend Prepare(VRRuntime runtime)
    {
        foreach (var asm in XRBackends.RequiredAssemblies(runtime))
        {
            if (!XRAssemblyLoader.TryLoad(asm))
            {
                Plugin.Log.LogError($"{Tag} {runtime}: {asm}.dll missing, skipping this runtime");
                return null;
            }
        }
        try
        {
            return XRBackends.Create(runtime);
        }
        catch (Exception e)
        {
            // TypeLoadException / FileNotFoundException when a RuntimeDeps DLL is missing or mismatched.
            Plugin.Log.LogError(
                $"{Tag} {runtime}: backend unavailable: {e.GetBaseException().Message}"
            );
            return null;
        }
    }

    static bool TryInitialize(XRBackend candidate)
    {
        var options = new XRStartOptions
        {
            DepthSubmission = (int)BootstrapFeature.DepthSubmissionEntry.Value,
            UseRuntimePredictedTime = BootstrapFeature.PredictedTime.Value,
        };
        try
        {
            if (candidate.Initialize(options))
            {
                Plugin.Log.LogInfo($"{Tag} {candidate.Runtime} loader initialized");
                return true;
            }
            Plugin.Log.LogError(
                $"{Tag} {candidate.Runtime} loader failed to initialize (see the Unity log lines above)"
            );
        }
        catch (Exception e)
        {
            Plugin.Log.LogError(
                $"{Tag} {candidate.Runtime} loader threw during initialization: {e}"
            );
        }
        return false;
    }

    static void SafeShutdown(XRBackend candidate)
    {
        try
        {
            candidate.Shutdown();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning(
                $"{Tag} {candidate.Runtime} shutdown threw: {e.GetBaseException().Message}"
            );
        }
    }

    /// <summary>False when setting up the running session threw: it is shut down and the game stays flat, never
    /// half on (a running display the rest of the mod does not know about).</summary>
    bool Activate(XRBackend running)
    {
        try
        {
            ActivateSession(running);
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError(
                $"{Tag} {running.Runtime}: setting up the running session threw, shutting it down: {e}"
            );
            InputDevices.deviceConnected -= OnDeviceConnected;
            started = false;
            backend = null;
            XRRuntimeInfo.Display = null;
            XRRuntimeInfo.Input = null;
            XRRuntimeInfo.Description = "none";
            VRSession.Running = VRRuntime.Disabled;
            VRSession.SetActive(false);
            SafeShutdown(running);
            return false;
        }
    }

    void ActivateSession(XRBackend running)
    {
        backend = running;
        started = true;
        XRRuntimeInfo.Display = FindRunningDisplay();
        XRRuntimeInfo.Input = FindInput();
        ConfigureTrackingOrigin();
        ConfigureStereo();
        backend.OnRunning();

        VRSession.Running = backend.Runtime;
        XRRuntimeInfo.Description = backend.Describe();
        Plugin.Log.LogInfo(
            $"{Tag} VR active: {backend.Describe()}; eye texture {XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight}, "
                + $"stereo mode {XRSettings.stereoRenderingMode}, refresh {RefreshRate():0.#} Hz, floor tracking {XRRuntimeInfo.FloorTracking}"
        );

        if (BootstrapFeature.LogInputDevices.Value)
        {
            XRDeviceDump.DumpAll();
            InputDevices.deviceConnected += OnDeviceConnected;
        }
        VRSession.SetActive(true);
    }

    static void ConfigureTrackingOrigin()
    {
        var input = XRRuntimeInfo.Input;
        XRRuntimeInfo.FloorTracking = false;
        if (input == null)
        {
            Plugin.Log.LogWarning($"{Tag} No XR input subsystem; using device-relative tracking");
            return;
        }
        var supported = input.GetSupportedTrackingOriginModes();
        // TrackingOriginModeFlags is a bit set without [Flags] (Unity's XRInputSubsystem API).
#pragma warning disable S3265
        bool floorSupported = (supported & TrackingOriginModeFlags.Floor) != 0;
#pragma warning restore S3265
        if (floorSupported && input.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor))
            XRRuntimeInfo.FloorTracking = true;
        else
            Plugin.Log.LogWarning(
                $"{Tag} Floor tracking unavailable (supported: {supported}); eye height comes from the first recenter"
            );
        Plugin.Log.LogInfo($"{Tag} Tracking origin mode: {input.GetTrackingOriginMode()}");
    }

    static void ConfigureStereo()
    {
        // The game build has no XR occlusion-mesh or mirror shaders (stripped by URP, stack.md section 4):
        // turn the visibility mesh off and let rig.camera draw the desktop mirror itself. API:
        // https://docs.unity3d.com/Packages/com.unity.render-pipelines.core@17.0/api/UnityEngine.Rendering.XRSRPSettings.html
        // and https://docs.unity3d.com/6000.3/Documentation/ScriptReference/XR.XRSettings.html
        XRSRPSettings.useVisibilityMesh = false;
        XRSRPSettings.occlusionMeshScale = 0f;
        XRSRPSettings.mirrorViewMode = XRMirrorViewBlitMode.None;
        XRSettings.useOcclusionMesh = false;
    }

    static void WarnGraphicsApi()
    {
        var api = SystemInfo.graphicsDeviceType;
        var advice = ApiAdvice(api, out var gpu);
        Plugin.Log.LogInfo($"{Tag} Graphics API {api}{(UnderWine ? " (Proton)" : "")}; {gpu}");
        if (advice != null)
            Plugin.Log.LogWarning($"{Tag} {advice}");
    }

    /// <summary>The <see cref="GraphicsAdvice"/> for this GPU and graphics API: NVIDIA and Proton's Wayland driver
    /// change what works under Proton. <paramref name="gpu"/> is what the verdict rests on, for the log.</summary>
    internal static string ApiAdvice(GraphicsDeviceType api, out string gpu)
    {
        int vendor = SystemInfo.graphicsDeviceVendorID;
        string device = SystemInfo.graphicsDeviceName;
        bool wine = UnderWine;
        bool? nvidia = GraphicsAdvice.Nvidia(vendor, device, api.ToString(), wine, out var how);
        bool wayland =
            Environment.GetEnvironmentVariable("PROTON_ENABLE_WAYLAND") is { Length: > 0 } w
            && w != "0";
        string verdict = nvidia switch
        {
            true => "yes",
            false => "no",
            null => "unknown",
        };
        gpu =
            $"GPU vendor 0x{vendor:X4} \"{device}\"; NVIDIA {verdict} ({how}); "
            + $"Proton Wayland driver {(wayland ? "on" : "off")}";
        return GraphicsAdvice.For(api.ToString(), wine, nvidia, wayland);
    }

    /// <summary>Wine sets these in every process it runs, Proton included.</summary>
    internal static bool UnderWine =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WINEHOMEDIR"))
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WINEPREFIX"));

    void Update()
    {
        if (!started)
            return;

        var display = XRRuntimeInfo.Display;
        bool running = display != null && display.running;
        if (!running && VRSession.Active)
        {
            Plugin.Log.LogWarning(
                $"{Tag} XR display stopped ({(XRSessionState.ExitRequested ? "runtime exit" : "unknown reason")}); continuing flat"
            );
            SetFocus(true);
            VRSession.SetActive(false);
        }
        else if (!running)
        {
            // The OpenXR restarter may bring the session back (runtime restarted); pick it up again.
            var again = FindRunningDisplay();
            if (again != null)
            {
                XRRuntimeInfo.Display = again;
                XRRuntimeInfo.Input = FindInput();
                ConfigureTrackingOrigin();
                ConfigureStereo();
                Plugin.Log.LogInfo($"{Tag} XR display running again; VR back on");
                VRSession.SetActive(true);
            }
        }

        if (VRSession.Active)
            SetFocus(ComputeFocus());
    }

    static bool ComputeFocus()
    {
        if (VRSession.Running == VRRuntime.OpenVR)
            return !XRSessionState.OpenVRFocusLost;
        if (VRSession.Running != VRRuntime.OpenXR)
            return true;
        // VISIBLE (4) means the dashboard or another app has input.
        return XRFocus.Focused(XRSessionState.OpenXRState);
    }

    /// <summary>Why focus is what it is, for the log and the self-test: the OpenXR session state, or OpenVR's flag.</summary>
    internal static string FocusSource =>
        VRSession.Running == VRRuntime.OpenXR ? "OpenXR " + XRFocus.Name(XRSessionState.OpenXRState)
        : VRSession.Running == VRRuntime.OpenVR
            ? (XRSessionState.OpenVRFocusLost ? "OpenVR: dashboard or another app" : "OpenVR")
        : "no runtime";

    void SetFocus(bool focus)
    {
        if (focus == lastFocus)
            return;
        lastFocus = focus;
        VRSession.HasFocus = focus;
        if (focus)
            VRFade.Clear(this);
        else
            VRFade.Set(this, 0.6f);
        Plugin.Log.LogInfo(
            $"{Tag} Runtime focus {(focus ? "regained" : "lost: input paused")} ({FocusSource})"
        );
    }

    static void OnDeviceConnected(InputDevice device) => XRDeviceDump.Dump(device);

    void OnDestroy()
    {
        EndGuard();
        InputDevices.deviceConnected -= OnDeviceConnected;
        if (backend != null)
            SafeShutdown(backend);
    }

    XRDisplaySubsystem FindRunningDisplay()
    {
        SubsystemManager.GetSubsystems(displays);
        foreach (var d in displays)
            if (d.running)
                return d;
        return null;
    }

    XRInputSubsystem FindInput()
    {
        SubsystemManager.GetSubsystems(inputs);
        foreach (var i in inputs)
            if (i.running)
                return i;
        return inputs.Count > 0 ? inputs[0] : null;
    }

    static float RefreshRate()
    {
        var d = XRRuntimeInfo.Display;
        return d != null && d.TryGetDisplayRefreshRate(out var hz) ? hz : 0f;
    }

    static bool HasDescriptor(string id)
    {
        var list = new List<XRDisplaySubsystemDescriptor>();
        SubsystemManager.GetSubsystemDescriptors(list);
        foreach (var d in list)
            if (d.id == id)
                return true;
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void LogDescriptors()
    {
        var list = new List<XRDisplaySubsystemDescriptor>();
        SubsystemManager.GetSubsystemDescriptors(list);
        var ids = new List<string>();
        foreach (var d in list)
            ids.Add(d.id);
        Plugin.Log.LogInfo(
            $"{Tag} Requested runtime {VRSession.Requested}; registered XR display providers: [{string.Join(", ", ids)}]"
        );
        if (list.Count == 0)
            Plugin.Log.LogError(
                $"{Tag} No XR display provider is registered: the preloader did not install a UnitySubsystemsManifest (check the PeakXR.Preloader lines above)"
            );
    }
}
