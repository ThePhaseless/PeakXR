using System.Collections.Generic;
using PeakXR.Core;
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

    private void OnQualityChanged(object sender, System.EventArgs e)
    {
        if (applied)
            ApplyQuality();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (applied)
            ApplyScene();
    }

    /// <summary>URP asset and renderer features: render scale once (it reallocates eye buffers), MSAA,
    /// linear upscaling (STP is temporal and needs TAA, which URP cannot combine with XR dynamic resolution),
    /// and a shadow distance cap (the shadow maps render for each eye pass).</summary>
    private void ApplyQuality()
    {
        if (urp == null)
            return;
        urp.renderScale = RenderFixesFeature.RenderScale.Value;
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
        if (!applied || !VRSession.Active)
            return;
        BindCamera();
        UpdateDynamicResolution();
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
