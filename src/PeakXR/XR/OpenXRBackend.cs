#if PEAKXR_OPENXR
using System;
using System.Collections.Generic;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace PeakXR.XR;

/// <summary>
/// Unity OpenXR provider, configured entirely in code (docs/research/stack.md sections 3-6):
/// MultiPass (the game's shaders have no stereo variants), Legacy foveation API (the SRP foveation
/// variants are stripped too), depth submission and runtime predicted time set explicitly so an
/// OpenXR package update cannot change them, and every controller interaction profile the package
/// ships. No composition-layer features: wineopenxr mis-sizes any layer after the projection layer.
/// </summary>
sealed class OpenXRBackend : ManagedXRBackend
{
    /// <summary>Interaction profiles to enable and the OpenXR extension each one needs ("" = core).
    /// The package's editor attribute normally fills the extension string; in a player we must.
    /// Pico has no profile in this OpenXR package version, so it is not listed.</summary>
    static readonly (Type Type, string Name, string Extension)[] Profiles =
    [
        (typeof(ValveIndexControllerProfile), "Valve Index Controller Profile", ""),
        (typeof(OculusTouchControllerProfile), "Oculus Touch Controller Profile", ""),
        (
            typeof(MetaQuestTouchPlusControllerProfile),
            "Meta Quest Touch Plus Controller Profile",
            "XR_META_touch_controller_plus"
        ),
        (
            typeof(MetaQuestTouchProControllerProfile),
            "Meta Quest Touch Pro Controller Profile",
            "XR_FB_touch_controller_pro"
        ),
        (typeof(HTCViveControllerProfile), "HTC Vive Controller Profile", ""),
        (
            typeof(HPReverbG2ControllerProfile),
            "HP Reverb G2 Controller Profile",
            "XR_EXT_hp_mixed_reality_controller"
        ),
        (typeof(MicrosoftMotionControllerProfile), "Microsoft Motion Controller Profile", ""),
        (typeof(KHRSimpleControllerProfile), "Khronos Simple Controller Profile", ""),
    ];

    public override VRRuntime Runtime => VRRuntime.OpenXR;

    public override void Shutdown()
    {
        base.Shutdown();
        // OpenXR may have queued a delayed restart (restart or get-system loop) that later deinitializes whatever
        // XRGeneralSettings.Manager is then, the OpenVR fallback included. Its coroutines die with their object
        // (OpenXRRestarter.cs:87); the package recreates it on the next OpenXR start.
        var restarter = GameObject.Find("~oxrestarter");
        if (restarter != null)
            UnityEngine.Object.Destroy(restarter);
    }

    protected override XRLoader CreateLoader(XRStartOptions options)
    {
        // Awake of a new OpenXRSettings registers it as OpenXRSettings.Instance in a player build.
        var settings = ScriptableObject.CreateInstance<OpenXRSettings>();
        settings.renderMode = OpenXRSettings.RenderMode.MultiPass;
        settings.depthSubmissionMode = (OpenXRSettings.DepthSubmissionMode)
            Math.Max(0, Math.Min(2, options.DepthSubmission));
        settings.useOpenXRPredictedTime = options.UseRuntimePredictedTime;
        settings.foveatedRenderingApi = OpenXRSettings.BackendFovationApi.Legacy;
        settings.latencyOptimization = OpenXRSettings.LatencyOptimization.PrioritizeRendering;

        var features = new List<OpenXRFeature>();
        var priority = 0;
        foreach (var (type, name, extension) in Profiles)
            features.Add(
                MakeFeature(
                    type,
                    name,
                    "com.thephaseless.peakxr." + type.Name,
                    extension,
                    priority++
                )
            );
        features.Add(
            MakeFeature(
                typeof(PeakXRSessionFeature),
                "PeakXR Session Monitor",
                "com.thephaseless.peakxr.session",
                "",
                1000
            )
        );
        SetInternal(settings, "features", features.ToArray());

        // Leave the game running flat when the runtime closes (SteamVR quit, headset unplugged):
        // the bootstrap driver notices the display stopping and deactivates VR.
        OpenXRRuntime.wantsToQuit -= RefuseQuit;
        OpenXRRuntime.wantsToQuit += RefuseQuit;

        Plugin.Log.LogInfo(
            $"[xr.bootstrap] OpenXR settings: renderMode={settings.renderMode}, depthSubmission={settings.depthSubmissionMode}, "
                + $"predictedTime={settings.useOpenXRPredictedTime}, foveation={settings.foveatedRenderingApi}, "
                + $"profiles={Profiles.Length}, compositionLayers=none"
        );
        return ScriptableObject.CreateInstance<OpenXRLoader>();
    }

    static bool RefuseQuit()
    {
        Plugin.Log.LogWarning(
            "[xr.bootstrap] OpenXR runtime asked the game to quit; continuing without VR"
        );
        XRSessionState.ExitRequested = true;
        return false;
    }

    static OpenXRFeature MakeFeature(
        Type type,
        string name,
        string id,
        string extension,
        int priority
    )
    {
        var feature = (OpenXRFeature)ScriptableObject.CreateInstance(type);
        SetInternal(feature, "nameUi", name);
        SetInternal(feature, "featureIdInternal", id);
        SetInternal(feature, "openxrExtensionStrings", extension);
        SetInternal(feature, "company", "PeakXR");
        SetInternal(feature, "version", "1.0.0");
        SetInternal(feature, "priority", priority);
        feature.enabled = true;
        return feature;
    }

    public override void OnRunning()
    {
        // Missing extensions only disable their profile; list what the runtime actually gave us.
        try
        {
            Plugin.Log.LogInfo(
                $"[xr.bootstrap] OpenXR extensions enabled: {string.Join(" ", OpenXRRuntime.GetEnabledExtensions())}"
            );
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[xr.bootstrap] Could not list OpenXR extensions: {e.Message}");
        }
    }

    public override string Describe()
    {
        try
        {
            return $"OpenXR runtime '{OpenXRRuntime.name}' {OpenXRRuntime.version}, API {OpenXRRuntime.apiVersion}, plugin {OpenXRRuntime.pluginVersion}";
        }
        catch (Exception e)
        {
            return $"OpenXR (runtime info unavailable: {e.Message})";
        }
    }
}

/// <summary>
/// Hidden OpenXR feature whose only job is to report session state changes (focus loss when the
/// runtime dashboard opens, exit and loss events) to <see cref="XRSessionState"/>.
/// </summary>
sealed class PeakXRSessionFeature : OpenXRFeature
{
    // XrSessionState values from the OpenXR spec.
    const int Exiting = 8;
    const int LossPending = 7;

    protected override void OnSessionStateChange(int oldState, int newState)
    {
        XRSessionState.OpenXRState = newState;
        if (newState is Exiting or LossPending)
            XRSessionState.ExitRequested = true;
    }

    protected override void OnInstanceLossPending(ulong xrInstance) =>
        XRSessionState.ExitRequested = true;

    protected override void OnSessionBegin(ulong xrSession) => XRSessionState.ExitRequested = false;
}
#endif
