using System.Collections.Generic;
using PeakXR.Core;
using PeakXR.UI.Affordances;
using PeakXR.UI.Common;
using PeakXR.VR;
using PeakXR.XR;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using Zorro.Settings;

namespace PeakXR.Render;

/// <summary>
/// Applies and reverts <see cref="RenderFixesFeature"/>'s changes on <see cref="VRSession.Started"/> /
/// <see cref="VRSession.Stopped"/> and on every scene load, and runs dynamic resolution each frame.
/// Every change it makes to shared assets (URP asset, volume profiles, renderer features, game settings)
/// is recorded and restored, so flat play after a VR session looks like it did before.
/// </summary>
internal sealed class RenderDriver : MonoBehaviour
{
    private const string Tag = "[render.fixes]";

    private static RenderDriver instance;

    private readonly ViewportScaler scaler = new();
    private readonly AdaptiveScale adaptive = new();

    /// <summary>The adaptive multiplier on the URP asset's render scale right now; <see cref="adaptive"/> holds the
    /// wanted one until a fade or scene load lets it land.</summary>
    private float appliedAdaptive = 1f;

    /// <summary>Seconds per adaptive quality window: one p95 per window feeds <see cref="AdaptiveScale"/>.</summary>
    private const float AdaptWindowSeconds = 5f;

    /// <summary>Without GPU time only missed frames are visible: step down when the p95 interval is a quarter past
    /// the budget (frames shown twice), never up.</summary>
    private const float IntervalOnlyTarget = 1.25f;
    private readonly HashSet<VolumeComponent> disabledComponents = new();
    private readonly List<ScriptableRendererFeature> toggledFeatures = new();
    private readonly List<bool> toggledFeatureStates = new();
    private readonly List<(OffOnSetting Setting, OffOnMode Value)> forcedSettings = new();

    private bool applied;
    private UniversalRenderPipelineAsset urp;
    private float savedRenderScale;
    private UpscalingFilterSelection savedUpscaling;
    private int savedMsaa;
    private float savedShadowDistance;
    private int savedTargetFrameRate;
    private int savedVSync;

    private MainCamera boundMain;
    private UniversalAdditionalCameraData boundCameraData;
    private AntialiasingMode savedAntialiasing;
    private bool savedDynamicResolution;
    private const float QualityDelay = 0.4f;
    private float qualityDueAt = -1f;

    private void OnEnable()
    {
        instance = this;
        VRSession.Started += Apply;
        VRSession.Stopped += Revert;
        SceneManager.sceneLoaded += OnSceneLoaded;
        RenderFixesFeature.RenderScale.SettingChanged += OnQualityChanged;
        RenderFixesFeature.Msaa.SettingChanged += OnQualityChanged;
        RenderFixesFeature.EdgeDetection.SettingChanged += OnQualityChanged;
        RenderFixesFeature.ShadowDistance.SettingChanged += OnQualityChanged;
        RenderFixesFeature.ComfortSettings.SettingChanged += OnComfortSettingsChanged;
        if (VRSession.Active)
            Apply();
    }

    private void OnDisable()
    {
        VRSession.Started -= Apply;
        VRSession.Stopped -= Revert;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        RenderFixesFeature.RenderScale.SettingChanged -= OnQualityChanged;
        RenderFixesFeature.Msaa.SettingChanged -= OnQualityChanged;
        RenderFixesFeature.EdgeDetection.SettingChanged -= OnQualityChanged;
        RenderFixesFeature.ShadowDistance.SettingChanged -= OnQualityChanged;
        RenderFixesFeature.ComfortSettings.SettingChanged -= OnComfortSettingsChanged;
        Revert();
    }

    private void Apply()
    {
        if (applied)
            return;
        applied = true;

        savedTargetFrameRate = Application.targetFrameRate;
        savedVSync = QualitySettings.vSyncCount;
        Application.targetFrameRate = -1;
        QualitySettings.vSyncCount = 0;

        urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp != null)
        {
            savedRenderScale = urp.renderScale;
            savedUpscaling = urp.upscalingFilter;
            savedMsaa = urp.msaaSampleCount;
            savedShadowDistance = urp.shadowDistance;
        }
        else
        {
            Plugin.Log.LogWarning($"{Tag} No URP asset active; render scale and MSAA left alone");
        }
        adaptive.Reset();
        appliedAdaptive = 1f;
        FrameClock.ResetAll();
        ApplyQuality();
        ApplyScene();

        scaler.SetScale(1f);
        XRSettings.renderViewportScale = 1f;
        Plugin.Log.LogInfo(
            $"{Tag} Applied: renderScale {RenderFixesFeature.RenderScale.Value:0.00}, MSAA {RenderFixesFeature.Msaa.Value}x, "
                + $"upscaling Linear, AA {RenderFixesFeature.Antialiasing.Value}, dynamic resolution "
                + $"{RenderFixesFeature.DynamicResolution.Value}, FPS cap and VSync off"
        );
    }

    private void Revert()
    {
        if (!applied)
            return;
        applied = false;

        Application.targetFrameRate = savedTargetFrameRate;
        QualitySettings.vSyncCount = savedVSync;
        if (urp != null)
        {
            urp.renderScale = savedRenderScale;
            urp.upscalingFilter = savedUpscaling;
            urp.msaaSampleCount = savedMsaa;
            urp.shadowDistance = savedShadowDistance;
        }
        foreach (var c in disabledComponents)
            if (c != null)
                c.active = true;
        disabledComponents.Clear();
        RestoreFeatures();
        RestoreSettings();
        UnbindCamera();
        XRSettings.renderViewportScale = 1f;
        ReapplyGameSettings();
        Plugin.Log.LogInfo($"{Tag} Reverted for flat play");
    }

    /// <summary>The settings whose ApplyValue is skipped in VR may have changed in the game menu meanwhile: apply
    /// their current values now instead of the ones saved at Apply.</summary>
    private static void ReapplyGameSettings()
    {
        var handler = GameHandler.Instance != null ? GameHandler.Instance.SettingsHandler : null;
        if (handler == null)
            return;
        handler.GetSetting<RenderScaleSetting>()?.ApplyValue();
        handler.GetSetting<FPSCapSetting>()?.ApplyValue();
        handler.GetSetting<VSyncSetting>()?.ApplyValue();
        handler.GetSetting<ShadowDistanceSettings>()?.ApplyValue();
    }

    /// <summary>A Volume enabled after the scene loaded (item VFX such as the warp compass, WarpCompassVFX.cs:7-9).</summary>
    internal static void OnVolumeEnabled(Volume volume)
    {
        if (instance == null || !instance.applied)
            return;
        instance.DisableIn(volume.sharedProfile);
        if (volume.HasInstantiatedProfile())
            instance.DisableIn(volume.profile);
    }

    /// <summary>Applied otherwise only at XR start and scene load; the settings tab changes it live.</summary>
    private void OnComfortSettingsChanged(object sender, System.EventArgs e)
    {
        if (!applied)
            return;
        if (RenderFixesFeature.ComfortSettings.Value)
            ForceComfortSettings();
        else
            RestoreSettings();
    }

    /// <summary>Debounced: a slider drag in the settings tab fires every tick, and each render scale change
    /// reallocates the eye textures, so the value is applied once it has been still for a moment.</summary>
    private void OnQualityChanged(object sender, System.EventArgs e) =>
        qualityDueAt = Time.unscaledTime + QualityDelay;

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!applied)
            return;
        ApplyScene();
        // A new scene has its own load: start it at the full render scale, reallocated while it loads.
        adaptive.Reset();
        if (appliedAdaptive != 1f && urp != null)
            ApplyAdaptive("scene load");
        FrameClock.Adapt.Reset();
    }

    /// <summary>URP asset and renderer features: render scale once (it reallocates eye buffers), MSAA,
    /// linear upscaling (STP is temporal and needs TAA, which URP cannot combine with XR dynamic resolution),
    /// and a shadow distance cap (the shadow maps render for each eye pass). API:
    /// https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset.html</summary>
    private void ApplyQuality()
    {
        if (urp == null)
            return;
        urp.renderScale = RenderFixesFeature.RenderScale.Value * appliedAdaptive;
        urp.upscalingFilter = UpscalingFilterSelection.Linear;
        // Deferred renders without MSAA, but XR would still allocate multisampled eye textures and resolve them.
        urp.msaaSampleCount = IsDeferred(urp) ? 1 : RenderFixesFeature.Msaa.Value;
        urp.shadowDistance = Mathf.Min(
            savedShadowDistance,
            RenderFixesFeature.ShadowDistance.Value
        );

        RestoreFeatures();
        foreach (var data in urp.rendererDataList)
        {
            if (data == null)
                continue;
            foreach (var feature in data.rendererFeatures)
            {
                // SCPE EdgeDetection reconstructs normals from per-eye depth; it is correct in MultiPass and
                // only switched off on request (cost). Matched by name: sc.posteffects is not referenced.
                if (feature == null || !feature.GetType().Name.Contains("EdgeDetection"))
                    continue;
                toggledFeatures.Add(feature);
                toggledFeatureStates.Add(feature.isActive);
                feature.SetActive(feature.isActive && RenderFixesFeature.EdgeDetection.Value);
            }
        }
    }

    private static bool IsDeferred(UniversalRenderPipelineAsset asset)
    {
        var list = asset.rendererDataList;
        return list.Length > 0
            && list[0]
                is UniversalRendererData
                {
                    renderingMode: RenderingMode.Deferred or RenderingMode.DeferredPlus
                };
    }

    private void RestoreFeatures()
    {
        for (int i = 0; i < toggledFeatures.Count; i++)
            if (toggledFeatures[i] != null)
                toggledFeatures[i].SetActive(toggledFeatureStates[i]);
        toggledFeatures.Clear();
        toggledFeatureStates.Clear();
    }

    private void ApplyScene()
    {
        DisableComfortEffects();
        ForceComfortSettings();
    }

    /// <summary>
    /// Turns off view effects that make VR users sick or break stereo, in every Volume profile of the loaded
    /// scenes (found by component type, never by object name): MotionBlur (Fall/PassOut volumes),
    /// LensDistortion and ChromaticAberration (warp, sugar rush, soul statue), DepthOfField (blur volume),
    /// FilmGrain (cold), Vignette (base look; the comfort vignette replaces it), ScreenSpaceLensFlare (a per-eye
    /// pass whose flares differ between the eyes).
    /// </summary>
    private void DisableComfortEffects()
    {
        int before = disabledComponents.Count;
        foreach (
            var volume in FindObjectsByType<Volume>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
        {
            DisableIn(volume.sharedProfile);
            if (volume.HasInstantiatedProfile())
                DisableIn(volume.profile);
        }
        if (disabledComponents.Count != before)
            Plugin.Log.LogInfo(
                $"{Tag} Disabled {disabledComponents.Count - before} post effect(s) unsafe in VR"
            );
    }

    private void DisableIn(VolumeProfile profile)
    {
        if (profile == null)
            return;
        var list = profile.components;
        for (int i = 0; i < list.Count; i++)
        {
            var c = list[i];
            if (
                c != null
                && c.active
                && c
                    is MotionBlur
                        or LensDistortion
                        or ChromaticAberration
                        or DepthOfField
                        or FilmGrain
                        or Vignette
                        or ScreenSpaceLensFlare
            )
            {
                c.active = false;
                disabledComponents.Add(c);
            }
        }
    }

    /// <summary>
    /// HeadBobSetting ON swaps the local animator to the no-bob controller (CharacterAnimations.cs:152, read
    /// every Update at :160), so the head bone the view anchors to does not bob. PhotosensitiveSetting ON halves
    /// and caps shake (GamefeelHandler.cs:46) and softens ScreenVFX; GUIManager caches it at Start
    /// (GUIManager.cs:351), so its field is set too. Values are set in memory only, never saved.
    /// </summary>
    private void ForceComfortSettings()
    {
        if (!RenderFixesFeature.ComfortSettings.Value || GameHandler.Instance == null)
            return;
        var handler = GameHandler.Instance.SettingsHandler;
        if (handler == null)
            return;
        Force(handler.GetSetting<HeadBobSetting>());
        Force(handler.GetSetting<PhotosensitiveSetting>());
        var gui = GUIManager.instance;
        if (gui != null)
            gui.photosensitivity = true;
    }

    private void Force(OffOnSetting setting)
    {
        if (setting == null || setting.Value == OffOnMode.ON)
            return;
        forcedSettings.Add((setting, setting.Value));
        RenderPatches.OffOnValueSetter.Invoke(setting, [OffOnMode.ON]);
    }

    private void RestoreSettings()
    {
        foreach (var (setting, value) in forcedSettings)
            RenderPatches.OffOnValueSetter.Invoke(setting, [value]);
        forcedSettings.Clear();
        var gui = GUIManager.instance;
        if (gui != null && GameHandler.Instance != null)
            gui.photosensitivity =
                GameHandler.Instance.SettingsHandler.GetSetting<PhotosensitiveSetting>().Value
                == OffOnMode.ON;
    }

    private void Update()
    {
        if (qualityDueAt >= 0f && Time.unscaledTime >= qualityDueAt)
        {
            qualityDueAt = -1f;
            if (applied)
                ApplyQuality();
        }
        if (!applied || !VRSession.Active)
            return;
        BindCamera();
        UpdateDynamicResolution();
        FrameClock.Tick();
        LogFrameTiming();
        UpdateAdaptiveQuality();
    }

    private static void LogFrameTiming()
    {
        float every = RenderFixesFeature.FrameTimingLogSeconds.Value;
        if (!(every > 0f) || FrameClock.Log.Seconds < every)
            return;
        var report = FrameClock.Summarize(FrameClock.Log);
        FrameClock.Log.Reset();
        Plugin.Log.LogInfo($"{Tag} frames: {report.Line(CurrentScale)}");
    }

    /// <summary>Eye resolution now: the URP render scale times the OpenVR viewport scale.</summary>
    internal static float CurrentScale =>
        (instance != null && instance.urp != null ? instance.urp.renderScale : 1f)
        * XRSettings.renderViewportScale;

    /// <summary>
    /// OpenXR: <c>XRSettings.renderViewportScale</c> showed the game in a corner (<see cref="UpdateDynamicResolution"/>),
    /// so the URP asset's render scale, which sizes the eye textures, is the knob. Unity: "Changing the render scale
    /// reallocates the eye texture, which is an expensive operation", and <c>XRSettings.eyeTextureResolutionScale</c>
    /// is not supported in URP (https://docs.unity.com/en-us/engine/6000.3/manual/xr/graphics/resolution-scaling,
    /// API https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.3/api/UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset.html).
    /// So the controller moves at most once per 5 s window, and a step lands only behind a full fade to black or at
    /// a scene load, where the reallocation hitch is never seen.
    /// </summary>
    private void UpdateAdaptiveQuality()
    {
        bool on =
            RenderFixesFeature.AdaptiveQuality.Value
            && VRSession.Running == VRRuntime.OpenXR
            && urp != null;
        if (!on)
            adaptive.Reset();
        else if (FrameClock.Adapt.Seconds >= AdaptWindowSeconds)
            FeedAdaptive();
        if (urp != null && adaptive.Scale != appliedAdaptive && FadeView.Alpha >= 0.99f)
            ApplyAdaptive("fade");
    }

    private void FeedAdaptive()
    {
        var report = FrameClock.Summarize(FrameClock.Adapt);
        FrameClock.Adapt.Reset();
        // A step still waiting for its fade would be measured at the old scale and stepped again.
        if (adaptive.Scale != appliedAdaptive || report.BudgetMs <= 0f || report.Interval.Empty)
            return;
        bool gpu = !report.Gpu.Empty;
        adaptive.Min = RenderFixesFeature.AdaptiveQualityMin.Value;
        adaptive.Target = gpu ? RenderFixesFeature.AdaptiveQualityTarget.Value : IntervalOnlyTarget;
        float load = (gpu ? report.Gpu.P95 : report.Interval.P95) / report.BudgetMs;
        if (adaptive.Feed(load, canRaise: gpu))
            Plugin.Log.LogInfo(
                System.FormattableString.Invariant(
                    $"{Tag} adaptive quality wants {adaptive.Scale:0.00} of RenderScale ({(gpu ? "GPU" : "frame")} p95 load {load:0.00}, target {adaptive.Target:0.00}); it lands at the next fade"
                )
            );
    }

    /// <summary>Resizes the eye textures to the wanted adaptive scale; called only while nothing is visible.</summary>
    private void ApplyAdaptive(string when)
    {
        appliedAdaptive = adaptive.Scale;
        urp.renderScale = RenderFixesFeature.RenderScale.Value * appliedAdaptive;
        FrameClock.Adapt.Reset();
        Plugin.Log.LogInfo(
            System.FormattableString.Invariant(
                $"{Tag} adaptive quality: render scale {urp.renderScale:0.00} ({appliedAdaptive:0.00} of RenderScale), applied at {when}"
            )
        );
        var rig = VRRig.Instance;
        bool left = VRInput.OffHandLeft;
        var hand = rig != null ? rig.Hand(left) : null;
        if (hand != null)
            Affordance.Inform(
                left,
                hand.position,
                DenyReason.HappenedHere,
                DenyHint.Wrist(),
                System.FormattableString.Invariant($"Render scale {appliedAdaptive:0.00}")
            );
    }

    /// <summary>Headset camera: no TAA (temporal smearing, and URP cannot run it with XR dynamic resolution),
    /// post AA from the setting, URP dynamic resolution on so renderViewportScale also scales post-processing.</summary>
    private void BindCamera()
    {
        var main = MainCamera.instance;
        if (main == boundMain && (main == null || boundCameraData != null))
        {
            if (boundCameraData != null)
                boundCameraData.antialiasing = ToMode(RenderFixesFeature.Antialiasing.Value);
            return;
        }
        UnbindCamera();
        boundMain = main;
        if (main == null || !main.TryGetComponent(out Camera cam))
            return;
        boundCameraData = cam.GetUniversalAdditionalCameraData();
        savedAntialiasing = boundCameraData.antialiasing;
        savedDynamicResolution = cam.allowDynamicResolution;
        boundCameraData.antialiasing = ToMode(RenderFixesFeature.Antialiasing.Value);
        cam.allowDynamicResolution = true;
    }

    private void UnbindCamera()
    {
        if (boundCameraData != null)
        {
            boundCameraData.antialiasing = savedAntialiasing;
            if (boundCameraData.TryGetComponent(out Camera cam))
                cam.allowDynamicResolution = savedDynamicResolution;
        }
        boundMain = null;
        boundCameraData = null;
    }

    private static AntialiasingMode ToMode(VRAntialiasing aa) =>
        aa switch
        {
            VRAntialiasing.FXAA => AntialiasingMode.FastApproximateAntialiasing,
            VRAntialiasing.SMAA => AntialiasingMode.SubpixelMorphologicalAntiAliasing,
            _ => AntialiasingMode.None,
        };

    private void UpdateDynamicResolution()
    {
        // OpenXR (MultiPass) renders the scaled viewport but submits the full image rect, so the headset showed the
        // game in one corner of each eye over stale pixels (in-game VR run, 2026-10-21). Viewport scaling is OpenVR only.
        if (!RenderFixesFeature.DynamicResolution.Value || VRSession.Running == VRRuntime.OpenXR)
        {
            if (XRSettings.renderViewportScale != 1f)
                XRSettings.renderViewportScale = 1f;
            return;
        }
        var display = XRRuntimeInfo.Display;
        if (display == null || !display.TryGetAppGPUTimeLastFrame(out float gpu) || !(gpu > 0f))
            return;
        if (!display.TryGetDisplayRefreshRate(out float hz) || !(hz > 0f))
            return;
        // Providers report seconds or milliseconds; no real GPU frame takes under half a millisecond.
        float gpuMs = gpu < 0.5f ? gpu * 1000f : gpu;
        scaler.Min = RenderFixesFeature.DynamicResolutionMin.Value;
        float scale = scaler.Update(gpuMs, 1000f / hz, Time.unscaledDeltaTime);
        if (XRSettings.renderViewportScale != scale)
            XRSettings.renderViewportScale = scale;
    }
}
