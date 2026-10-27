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

    /// <summary>Adaptive scale changes applied this session (self-test vr.231 reports it).</summary>
    internal static int AdaptiveSteps { get; private set; }

    /// <summary>The adaptive multiplier applied now and the one the controller wants next, for bug reports.</summary>
    internal static string AdaptiveState =>
        instance == null
            ? "not running"
            : System.FormattableString.Invariant(
                $"applied {instance.appliedAdaptive:0.00}, wanted {instance.adaptive.Scale:0.00}, steps {AdaptiveSteps}"
            );

    /// <summary>Seconds per adaptive quality window: one p95 per window feeds <see cref="AdaptiveScale"/>.</summary>
    private const float AdaptWindowSeconds = 5f;

    /// <summary>Without GPU time only missed frames are visible: step down when the p95 interval is a quarter past
    /// the budget (frames shown twice), never up.</summary>
    private const float IntervalOnlyTarget = 1.25f;
    private readonly HashSet<VolumeComponent> disabledComponents = new();
    private readonly List<LensFlareComponentSRP> disabledFlares = new();
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

    private EyeMatrixFeature eyeMatrices;
    private ScriptableRendererData eyeMatricesOn;

    /// <summary>When the controller first wanted the scale that has not landed yet (-1: none waiting).</summary>
    private float wantedSince = -1f;

    /// <summary>The blink this driver started to land a step; cleared once it lands.</summary>
    private static readonly object BlinkOwner = new();
    private bool blinking;

    /// <summary>Pacing seen in the last adaptive window; logged when it changes.</summary>
    private PacingVerdict pacing = PacingVerdict.OnRate;
    private int pacingWindows;
    private bool pacingNoticeShown;

    /// <summary>Hot windows in a row before the player is told the headset rate is out of the GPU's reach.</summary>
    private const int PacingNoticeWindows = 3;

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
        RenderFixesFeature.LensFlare.SettingChanged += OnLensFlareChanged;
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
        RenderFixesFeature.LensFlare.SettingChanged -= OnLensFlareChanged;
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
        wantedSince = -1f;
        pacing = PacingVerdict.OnRate;
        pacingWindows = 0;
        FrameClock.ResetAll();
        ApplyQuality();
        AddEyeMatrices();
        ApplyScene();

        scaler.SetScale(1f);
        XRSettings.renderViewportScale = 1f;
        Plugin.Log.LogInfo(
            $"{Tag} Applied: renderScale {RenderFixesFeature.RenderScale.Value:0.00}, MSAA {(urp != null ? urp.msaaSampleCount : 0)}x "
                + $"(configured {RenderFixesFeature.Msaa.Value}x{(urp != null && IsDeferred(urp) ? ", Deferred forces 1" : "")}), "
                + $"upscaling Linear, AA {RenderFixesFeature.Antialiasing.Value}, {ScalingSummary()}, "
                + "FPS cap and VSync off (the runtime paces frames)"
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
        RestoreLensFlares();
        RestoreFeatures();
        RemoveEyeMatrices();
        RestoreSettings();
        UnbindCamera();
        EndBlink();
        XRSettings.renderViewportScale = 1f;
        ReapplyGameSettings();
        Plugin.Log.LogInfo($"{Tag} Reverted for flat play");
    }

    /// <summary>Which knob adapts the resolution on this runtime: viewport scaling (OpenVR) or the eye texture scale
    /// (OpenXR), so the log never claims a dynamic resolution that is off.</summary>
    private static string ScalingSummary() =>
        VRSession.Running == VRRuntime.OpenXR
            ? $"adaptive quality {(RenderFixesFeature.AdaptiveQuality.Value ? "on" : "off")} (OpenXR; viewport dynamic resolution is OpenVR only)"
            : $"dynamic resolution {(RenderFixesFeature.DynamicResolution.Value ? "on" : "off")}";

    /// <summary>Adds <see cref="EyeMatrixFeature"/> to the game's renderer (URP_Renderer); URP rebuilds the renderer
    /// once on the next frame (ScriptableRendererData.SetDirty).</summary>
    private void AddEyeMatrices()
    {
        if (urp == null || eyeMatricesOn != null || urp.rendererDataList.Length == 0)
            return;
        var data = urp.rendererDataList[0];
        if (data == null)
            return;
        if (eyeMatrices == null)
        {
            eyeMatrices = ScriptableObject.CreateInstance<EyeMatrixFeature>();
            eyeMatrices.name = "PeakXR eye matrices";
            eyeMatrices.hideFlags = HideFlags.DontUnloadUnusedAsset;
        }
        data.rendererFeatures.Add(eyeMatrices);
        data.SetDirty();
        eyeMatricesOn = data;
    }

    private void RemoveEyeMatrices()
    {
        if (eyeMatricesOn == null)
            return;
        eyeMatricesOn.rendererFeatures.Remove(eyeMatrices);
        eyeMatricesOn.SetDirty();
        eyeMatricesOn = null;
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
        // The load screen hides the reallocation, so a step still waiting for its fade lands now. The scale is kept:
        // run 57 reset it to full at each load and a slow GPU never stayed down.
        if (adaptive.Scale != appliedAdaptive && urp != null)
            ApplyAdaptive("scene load");
        // Loading frames say nothing about the new scene.
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
    /// pass whose flares differ between the eyes). Unless <c>Render.LensFlare</c> is on, also the sun's data-driven
    /// <see cref="LensFlareComponentSRP"/> (DayNightManager.lensFlare, DayNightManager.cs:66, intensity at :425): in
    /// MultiPass URP places and occludes it with the mono camera's matrices for both eyes
    /// (PostProcessPassRenderGraph.RenderLensFlareDataDriven), so it sits apart in each eye or shows in one only.
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
        int flaresBefore = disabledFlares.Count;
        if (!RenderFixesFeature.LensFlare.Value)
            foreach (
                var flare in FindObjectsByType<LensFlareComponentSRP>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None
                )
            )
                if (flare.enabled)
                {
                    flare.enabled = false;
                    disabledFlares.Add(flare);
                }
        if (disabledComponents.Count != before || disabledFlares.Count != flaresBefore)
            Plugin.Log.LogInfo(
                $"{Tag} Disabled {disabledComponents.Count - before} post effect(s) and {disabledFlares.Count - flaresBefore} lens flare(s) unsafe in VR"
            );
    }

    /// <summary>The settings tab turns <c>Render.LensFlare</c> on or off live.</summary>
    private void OnLensFlareChanged(object sender, System.EventArgs e)
    {
        if (!applied)
            return;
        if (RenderFixesFeature.LensFlare.Value)
            RestoreLensFlares();
        else
            DisableComfortEffects();
    }

    private void RestoreLensFlares()
    {
        foreach (var flare in disabledFlares)
            if (flare != null)
                flare.enabled = true;
        disabledFlares.Clear();
        disabledComponents.RemoveWhere(c =>
        {
            if (c is not ScreenSpaceLensFlare)
                return false;
            c.active = true;
            return true;
        });
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
                && (
                    c
                        is MotionBlur
                            or LensDistortion
                            or ChromaticAberration
                            or DepthOfField
                            or FilmGrain
                            or Vignette
                    || (c is ScreenSpaceLensFlare && !RenderFixesFeature.LensFlare.Value)
                )
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

    /// <summary>Photosensitive mode is on in VR only because <see cref="ForceComfortSettings"/> turned it on; the
    /// player's own setting is off.</summary>
    internal static bool PhotosensitiveForced
    {
        get
        {
            if (instance == null)
                return false;
            foreach (var (setting, _) in instance.forcedSettings)
                if (setting is PhotosensitiveSetting)
                    return true;
            return false;
        }
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
        // Debug mode keeps the frame line coming even when the player turned it off.
        if (Diagnostics.DebugLog.Verbose && !(every > 0f && every <= 10f))
            every = 10f;
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
        if (FrameClock.Adapt.Seconds >= AdaptWindowSeconds)
        {
            var report = FrameClock.Summarize(FrameClock.Adapt);
            FrameClock.Adapt.Reset();
            JudgePacing(report);
            if (on)
                FeedAdaptive(report);
        }
        if (urp == null || adaptive.Scale == appliedAdaptive)
        {
            wantedSince = -1f;
            EndBlink();
            return;
        }
        if (wantedSince < 0f)
            wantedSince = Time.unscaledTime;
        if (FadeView.Alpha >= 0.99f)
        {
            ApplyAdaptive(blinking ? "blink" : "fade");
            EndBlink();
            return;
        }
        // A step down waits for a natural fade only so long: run 2026-10-24 sat at half rate for 70 s in the title.
        // A short blink hides the reallocation instead; steps up always wait, so a blink never repeats for comfort.
        float blinkAfter = RenderFixesFeature.AdaptiveQualityBlinkSeconds.Value;
        if (
            !blinking
            && blinkAfter > 0f
            && adaptive.Scale < appliedAdaptive
            && Time.unscaledTime - wantedSince >= blinkAfter
        )
        {
            blinking = true;
            VRFade.Set(BlinkOwner, 1f);
        }
    }

    private void EndBlink()
    {
        if (!blinking)
            return;
        blinking = false;
        VRFade.Clear(BlinkOwner);
    }

    private void FeedAdaptive(in FrameClock.Report report)
    {
        // A step still waiting for its fade would be measured at the old scale and stepped again.
        // Only the headset's own rate: the desktop monitor's (144 or 60 Hz) would step a fine frame down, or never.
        if (
            adaptive.Scale != appliedAdaptive
            || !report.Trusted
            || report.BudgetMs <= 0f
            || report.Interval.Empty
        )
            return;
        bool gpu = !report.Gpu.Empty;
        adaptive.Min = RenderFixesFeature.AdaptiveQualityMin.Value;
        adaptive.Target = gpu ? RenderFixesFeature.AdaptiveQualityTarget.Value : IntervalOnlyTarget;
        float load = (gpu ? report.Gpu.P95 : report.Interval.P95) / report.BudgetMs;
        if (adaptive.Feed(load, canRaise: gpu))
            Plugin.Log.LogInfo(
                System.FormattableString.Invariant(
                    $"{Tag} adaptive quality wants {adaptive.Scale:0.00} of RenderScale ({(gpu ? "GPU" : "frame")} p95 load {load:0.00}, target {adaptive.Target:0.00}); it lands at the next fade, or behind a blink after {RenderFixesFeature.AdaptiveQualityBlinkSeconds.Value:0} s"
                )
            );
    }

    /// <summary>
    /// Names what holds the frame rate down (<see cref="FramePacing"/>): the game's FPS cap and VSync are off in VR, so a
    /// frame interval locked to a multiple of the budget is the runtime showing a fraction of the refresh and
    /// reprojecting (SteamVR on a 120 Hz Quest: 60 Hz). Logged when the verdict changes; after
    /// <see cref="PacingNoticeWindows"/> GPU-bound windows with no scale left to drop, the player gets one wrist notice
    /// with the refresh rate the GPU keeps up with.
    /// </summary>
    private void JudgePacing(in FrameClock.Report report)
    {
        if (!report.Trusted || report.Interval.Empty)
            return;
        var verdict = FramePacing.Judge(report.Interval.P50, report.Gpu.P95, report.BudgetMs);
        pacingWindows = verdict == pacing ? pacingWindows + 1 : 1;
        if (verdict != pacing)
        {
            pacing = verdict;
            Plugin.Log.LogInfo($"{Tag} pacing: {PacingLine(report, verdict)}");
        }
        bool noScaleLeft =
            !RenderFixesFeature.AdaptiveQuality.Value
            || VRSession.Running != VRRuntime.OpenXR
            || appliedAdaptive <= RenderFixesFeature.AdaptiveQualityMin.Value + 0.001f;
        if (
            verdict != PacingVerdict.GpuBound
            || pacingNoticeShown
            || pacingWindows < PacingNoticeWindows
            || !noScaleLeft
        )
            return;
        var rig = VRRig.Instance;
        bool left = VRInput.OffHandLeft;
        var hand = rig != null ? rig.Hand(left) : null;
        if (hand == null)
            return;
        pacingNoticeShown = true;
        int shown = Mathf.RoundToInt(
            report.Hz / FramePacing.Divisor(report.Interval.P50, report.BudgetMs)
        );
        float fits = FramePacing.FittingRefresh(report.Gpu.P50);
        Affordance.Inform(
            left,
            hand.position,
            DenyReason.HappenedHere,
            DenyHint.Wrist(),
            fits > 0f && fits < report.Hz
                ? System.FormattableString.Invariant(
                    $"{shown} of {report.Hz:0} Hz: GPU too slow. Set the headset to {fits:0} Hz"
                )
                : System.FormattableString.Invariant($"{shown} of {report.Hz:0} Hz: GPU too slow"),
            forceText: true
        );
    }

    private static string PacingLine(in FrameClock.Report r, PacingVerdict verdict)
    {
        if (verdict == PacingVerdict.OnRate)
            return System.FormattableString.Invariant($"frames at the headset's {r.Hz:0} Hz");
        int divisor = FramePacing.Divisor(r.Interval.P50, r.BudgetMs);
        string head = System.FormattableString.Invariant(
            $"the runtime shows {r.Hz / divisor:0} of {r.Hz:0} Hz and reprojects the rest (frame p50 {r.Interval.P50:0.00} ms, budget {r.BudgetMs:0.00} ms); this is runtime throttling, not a cap: the game's FPS cap and VSync are off"
        );
        if (verdict == PacingVerdict.NotGpu)
            return head
                + (
                    r.Gpu.Empty
                        ? "; no GPU time reported, so the cause is unknown"
                        : System.FormattableString.Invariant(
                            $"; the GPU fits (p95 {r.Gpu.P95:0.00} ms), so the CPU (main/render p95 {(r.CpuMain.Empty ? "n/a" : $"{r.CpuMain.P95:0.00}/{r.CpuRender.P95:0.00} ms")}) or the runtime holds it"
                        )
                );
        float fits = FramePacing.FittingRefresh(r.Gpu.P50);
        string advice =
            fits > 0f && fits < r.Hz
                ? System.FormattableString.Invariant(
                    $"the GPU keeps up with {fits:0} Hz: set the headset's refresh rate to it, or lower the runtime's resolution or Render.RenderScale"
                )
                : "lower the runtime's resolution or Render.RenderScale";
        var api = SystemInfo.graphicsDeviceType;
        string apiAdvice = GraphicsAdvice.For(api.ToString(), XRBootstrapDriver.UnderWine);
        return head
            + System.FormattableString.Invariant(
                $"; the GPU needs p50/p95 {r.Gpu.P50:0.00}/{r.Gpu.P95:0.00} ms of it at render scale {CurrentScale:0.00}; {advice}"
            )
            + (apiAdvice != null ? "; " + apiAdvice : "");
    }

    /// <summary>Resizes the eye textures to the wanted adaptive scale; called only while nothing is visible.</summary>
    private void ApplyAdaptive(string when)
    {
        appliedAdaptive = adaptive.Scale;
        AdaptiveSteps++;
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
        // The sanity-checked rate (FrameClock): a throttled runtime's 36 Hz would be a 27.8 ms budget.
        float hz = FrameClock.Trusted ? FrameClock.RefreshHz : 0f;
        if (!(hz > 0f))
            return;
        // Providers report seconds or milliseconds; no real GPU frame takes under half a millisecond.
        float gpuMs = gpu < 0.5f ? gpu * 1000f : gpu;
        scaler.Min = RenderFixesFeature.DynamicResolutionMin.Value;
        float scale = scaler.Update(gpuMs, 1000f / hz, Time.unscaledDeltaTime);
        if (XRSettings.renderViewportScale != scale)
            XRSettings.renderViewportScale = scale;
    }
}
