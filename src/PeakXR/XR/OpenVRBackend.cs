#if PEAKXR_OPENVR
using System;
using System.Reflection;
using PeakXR.VR;
using Unity.XR.OpenVR;
using UnityEngine;
using UnityEngine.XR.Management;
using Valve.VR;

namespace PeakXR.XR;

/// <summary>
/// Valve OpenVR provider (unity-xr-plugin), the fallback when OpenXR does not start. In a player the
/// native plugin normally reads its settings from StreamingAssets/SteamVR/OpenVRSettings.asset, which
/// the game does not have; we pass MultiPass and "no mirror" through the plugin's own
/// SetUserDefinedSettings entry point instead (the managed side only calls it in the editor), see
/// https://github.com/ValveSoftware/unity-xr-plugin/blob/master/com.valve.openvr/Runtime/OpenVRLoader.cs
/// </summary>
sealed class OpenVRBackend : ManagedXRBackend
{
    public override VRRuntime Runtime => VRRuntime.OpenVR;

    protected override XRLoader CreateLoader(XRStartOptions options)
    {
        // Awake of a new OpenVRSettings registers it as OpenVRSettings.s_Settings in a player build.
        var settings = ScriptableObject.CreateInstance<OpenVRSettings>();
        settings.StereoRenderingMode = OpenVRSettings.StereoRenderingModes.MultiPass;
        settings.InitializationType = OpenVRSettings.InitializationTypes.Scene;
        settings.MirrorView = OpenVRSettings.MirrorViewModes.None;
        PushUserSettings(settings);
        Plugin.Log.LogInfo(
            "[xr.bootstrap] OpenVR settings: stereo=MultiPass, init=Scene, nativeMirror=None"
        );
        return ScriptableObject.CreateInstance<OpenVRLoader>();
    }

    /// <summary>Dashboard, input-focus and headset-idle events drive <see cref="XRSessionState"/>, each one logged so a
    /// tester's log shows which ran (OpenVR has no session states to log). The plugin's own VREvent_Quit handler stays:
    /// SteamVR expects the app to exit when it quits.</summary>
    public override void OnRunning()
    {
        try
        {
            Listen(
                EVREventType.VREvent_DashboardActivated,
                _ => XRSessionState.OpenVRFocusLost = true
            );
            Listen(
                EVREventType.VREvent_DashboardDeactivated,
                _ => XRSessionState.OpenVRFocusLost = false
            );
            Listen(
                EVREventType.VREvent_InputFocusCaptured,
                _ => XRSessionState.OpenVRFocusLost = true
            );
            Listen(
                EVREventType.VREvent_InputFocusReleased,
                _ => XRSessionState.OpenVRFocusLost = false
            );
            Listen(EVREventType.VREvent_Quit, _ => XRSessionState.ExitRequested = true);
            // Proximity sensor: interaction ends when the headset comes off (only the HMD's counts) and starts again
            // when it is worn; standby follows after SteamVR's idle timeout.
            Listen(
                EVREventType.VREvent_TrackedDeviceUserInteractionEnded,
                e => XRSessionState.OpenVRIdle |= e.trackedDeviceIndex == Hmd
            );
            Listen(
                EVREventType.VREvent_TrackedDeviceUserInteractionStarted,
                e => XRSessionState.OpenVRIdle &= e.trackedDeviceIndex != Hmd
            );
            Listen(EVREventType.VREvent_EnterStandbyMode, _ => XRSessionState.OpenVRIdle = true);
            Listen(EVREventType.VREvent_LeaveStandbyMode, _ => XRSessionState.OpenVRIdle = false);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning(
                $"[xr.bootstrap] OpenVR focus events unavailable: {e.GetBaseException().Message}"
            );
        }
    }

    const uint Hmd = Valve.VR.OpenVR.k_unTrackedDeviceIndex_Hmd;

    static void Listen(EVREventType type, Action<VREvent_t> apply) =>
        OpenVREvents.AddListener(
            type,
            e =>
            {
                apply(e);
                Plugin.Log.LogInfo(
                    $"[xr.bootstrap] OpenVR {type} (device {e.trackedDeviceIndex}): focus lost {XRSessionState.OpenVRFocusLost}, "
                        + $"headset idle {XRSessionState.OpenVRIdle}, exit requested {XRSessionState.ExitRequested}"
                );
            }
        );

    /// <summary>Valve's display provider sets <c>framePresentCount</c> from the compositor's
    /// <c>GetFrameTiming</c> (<c>m_nNumFramePresents</c>, unity-xr-plugin Providers/Display/Display.cpp), so 1 or more
    /// means SteamVR presented one of our frames.</summary>
    public override bool FramesShown(out string signal)
    {
        var display = XRRuntimeInfo.Display;
        if (display == null || !display.TryGetFramePresentCount(out int presents))
        {
            signal = "OpenVR frame present count not reported";
            return false;
        }
        signal = $"OpenVR compositor frame present count {presents}";
        return presents > 0;
    }

    static void PushUserSettings(OpenVRSettings settings)
    {
        try
        {
            var structType =
                typeof(OpenVRLoader).GetNestedType("UserDefinedSettings", BindingFlags.NonPublic)
                ?? throw new MissingMemberException(nameof(OpenVRLoader), "UserDefinedSettings");
            var method =
                typeof(OpenVRLoader).GetMethod(
                    "SetUserDefinedSettings",
                    BindingFlags.NonPublic | BindingFlags.Static
                )
                ?? throw new MissingMethodException(nameof(OpenVRLoader), "SetUserDefinedSettings");
            object boxed = Activator.CreateInstance(structType);
            Set(structType, boxed, "stereoRenderingMode", settings.GetStereoRenderingMode());
            Set(structType, boxed, "initializationType", settings.GetInitializationType());
            Set(structType, boxed, "mirrorViewMode", (ushort)settings.GetMirrorViewMode());
            Set(structType, boxed, "editorAppKey", null);
            Set(structType, boxed, "actionManifestPath", null);
            Set(structType, boxed, "applicationName", Application.productName);
            method.Invoke(null, [boxed]);
        }
        catch (Exception e)
        {
            // Not fatal: the plugin falls back to its built-in defaults; a single-eye image would show it.
            Plugin.Log.LogWarning(
                $"[xr.bootstrap] Could not pass OpenVR settings to the native plugin: {e.GetBaseException().Message}"
            );
        }
    }

    static void Set(Type t, object boxed, string field, object value) =>
        (
            t.GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(t.FullName, field)
        ).SetValue(boxed, value);
}
#endif
