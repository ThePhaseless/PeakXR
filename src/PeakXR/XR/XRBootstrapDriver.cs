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

            if (!TryInitialize(candidate))
            {
                SafeShutdown(candidate);
                continue;
            }

            try
            {
                candidate.StartSubsystems();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"{Tag} {runtime}: starting subsystems threw: {e}");
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

            Activate(candidate);
            yield break;
        }

        Plugin.Log.LogError($"{Tag} No XR runtime started; VR stays off and the game runs flat");
        VRSession.Running = VRRuntime.Disabled;
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

    void Activate(XRBackend running)
    {
        backend = running;
        started = true;
        XRRuntimeInfo.Display = FindRunningDisplay();
        XRRuntimeInfo.Input = FindInput();
        ConfigureTrackingOrigin();
        ConfigureStereo();
        backend.OnRunning();

        VRSession.Running = backend.Runtime;
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
        if (
            (supported & TrackingOriginModeFlags.Floor) != 0
            && input.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor)
        )
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
        Plugin.Log.LogInfo($"{Tag} Graphics API {api}");
        if (api != GraphicsDeviceType.Vulkan)
            Plugin.Log.LogWarning(
                $"{Tag} {api} is untested in VR; add -force-vulkan to the launch options"
            );
    }

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
