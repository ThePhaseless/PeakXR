#if PEAKXR_OPENVR
using System;
using System.Reflection;
using PeakXR.VR;
using Unity.XR.OpenVR;
using UnityEngine;
using UnityEngine.XR.Management;

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

    /// <summary>Dashboard and input-focus events drive <see cref="XRSessionState.OpenVRFocusLost"/>. The
    /// plugin's own VREvent_Quit handler stays: SteamVR expects the app to exit when it quits.</summary>
    public override void OnRunning()
    {
        try
        {
            OpenVREvents.AddListener(
                Valve.VR.EVREventType.VREvent_DashboardActivated,
                _ => XRSessionState.OpenVRFocusLost = true
            );
            OpenVREvents.AddListener(
                Valve.VR.EVREventType.VREvent_DashboardDeactivated,
                _ => XRSessionState.OpenVRFocusLost = false
            );
            OpenVREvents.AddListener(
                Valve.VR.EVREventType.VREvent_InputFocusCaptured,
                _ => XRSessionState.OpenVRFocusLost = true
            );
            OpenVREvents.AddListener(
                Valve.VR.EVREventType.VREvent_InputFocusReleased,
                _ => XRSessionState.OpenVRFocusLost = false
            );
            OpenVREvents.AddListener(
                Valve.VR.EVREventType.VREvent_Quit,
                _ => XRSessionState.ExitRequested = true
            );
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning(
                $"[xr.bootstrap] OpenVR focus events unavailable: {e.GetBaseException().Message}"
            );
        }
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
