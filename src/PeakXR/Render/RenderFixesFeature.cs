using BepInEx.Configuration;
using PeakXR.Framework;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Render;

/// <summary>Post-process anti-aliasing for the headset camera (TAA is off in VR, see <see cref="RenderFixesFeature"/>).</summary>
public enum VRAntialiasing
{
    None,
    FXAA,
    SMAA,
}

/// <summary>
/// Feature <c>render.fixes</c>: makes the game's URP setup headset-safe while XR runs, and puts everything
/// back when it stops. Comfort: motion blur, lens distortion, chromatic aberration, depth of field, film grain
/// and the fixed vignette are switched off in every Volume (found by component type), head bob is removed and
/// photosensitive mode (half shake, dimmer flashes) is on. Quality and pacing: linear upscaling instead of STP
/// (temporal, needs TAA), no TAA, MSAA and render scale from our settings, a dynamic
/// <c>XRSettings.renderViewportScale</c> on OpenVR and an adaptive render scale on OpenXR, frame-time logging, and the game's FPS cap and VSync are ignored (the runtime paces
/// frames). Screen-space passes are kept: MultiPass renders each eye as an ordinary camera pass with that
/// eye's matrices, so HBAO, edge detection, the fog pass and the full-screen passes see a normal mono view.
/// The <c>CameraQuad</c> status overlays, sized from the mono frustum, are placed per eye by ui.world.
/// </summary>
public sealed class RenderFixesFeature : Feature
{
    internal static ConfigEntry<float> RenderScale;
    internal static ConfigEntry<int> Msaa;
    internal static ConfigEntry<VRAntialiasing> Antialiasing;
    internal static ConfigEntry<bool> DynamicResolution;
    internal static ConfigEntry<float> DynamicResolutionMin;
    internal static ConfigEntry<bool> AdaptiveQuality;
    internal static ConfigEntry<float> AdaptiveQualityMin;
    internal static ConfigEntry<float> AdaptiveQualityTarget;
    internal static ConfigEntry<float> FrameTimingLogSeconds;
    internal static ConfigEntry<bool> AmbientOcclusion;
    internal static ConfigEntry<bool> EdgeDetection;
    internal static ConfigEntry<float> ShadowDistance;
    internal static ConfigEntry<bool> ComfortSettings;

    public override string Id => "render.fixes";

    public override int Order => -800;

    protected internal override void Install()
    {
        const string section = "Render";
        RenderScale = Plugin.Settings.Bind(
            section,
            "RenderScale",
            1f,
            new ConfigDescription(
                "Eye buffer resolution multiplier in VR (on top of SteamVR's own resolution setting). Changing it "
                    + "reallocates the eye buffers, so it is applied once, not per frame.",
                new AcceptableValueRange<float>(0.5f, 2f)
            )
        );
        Msaa = Plugin.Settings.Bind(
            section,
            "MSAA",
            4,
            new ConfigDescription(
                "Hardware anti-aliasing samples on the eye buffers in VR. Has no effect while the game's renderer is "
                    + "in Deferred mode (URP cannot use MSAA there, so it is forced to 1); then Antialiasing below is "
                    + "what smooths edges. The log line '[render.fixes] Applied' shows the effective count.",
                new AcceptableValueList<int>(1, 2, 4, 8)
            )
        );
        Antialiasing = Plugin.Settings.Bind(
            section,
            "Antialiasing",
            VRAntialiasing.FXAA,
            new ConfigDescription(
                "Post-process anti-aliasing on the headset camera, replacing the game's TAA (temporal smearing and "
                    + "it blocks dynamic resolution in XR). Choose None if edges flicker or the image looks wrong."
            )
        );
        DynamicResolution = Plugin.Settings.Bind(
            section,
            "DynamicResolution",
            true,
            new ConfigDescription(
                "Lower the rendered part of the eye buffers when the GPU misses the headset's frame time, and raise "
                    + "it again slowly when there is headroom (XRSettings.renderViewportScale). OpenVR only: under OpenXR it stays off, "
                    + "because the runtime would show the unscaled frame; AdaptiveQuality does this job there."
            )
        );
        DynamicResolutionMin = Plugin.Settings.Bind(
            section,
            "DynamicResolutionMin",
            0.7f,
            new ConfigDescription(
                "Lowest viewport scale dynamic resolution may use.",
                new AcceptableValueRange<float>(0.5f, 1f)
            )
        );
        AdaptiveQuality = Plugin.Settings.Bind(
            section,
            "AdaptiveQuality",
            true,
            new ConfigDescription(
                "OpenXR: lower the render scale when the GPU keeps missing the headset's frame time, and raise it again "
                    + "after a long spell with room to spare. A change reallocates the eye buffers, so it waits for the "
                    + "next fade to black (a segment change, a death, a recenter) or a scene load; you get a notice when "
                    + "it lands. Each new scene starts at your full RenderScale."
            )
        );
        AdaptiveQualityMin = Plugin.Settings.Bind(
            section,
            "AdaptiveQualityMin",
            0.7f,
            new ConfigDescription(
                "Lowest fraction of RenderScale adaptive quality may drop to.",
                new AcceptableValueRange<float>(0.5f, 1f)
            )
        );
        AdaptiveQualityTarget = Plugin.Settings.Bind(
            section,
            "AdaptiveQualityTarget",
            0.85f,
            new ConfigDescription(
                "Share of the headset's frame time the GPU may use before adaptive quality steps down. Lower keeps "
                    + "more headroom for busy moments; higher keeps more detail.",
                new AcceptableValueRange<float>(0.6f, 0.95f)
            )
        );
        FrameTimingLogSeconds = Plugin.Settings.Bind(
            section,
            "FrameTimingLogSeconds",
            10f,
            new ConfigDescription(
                "In VR, write frame-time percentiles, GPU time and dropped frames to the log this often (seconds). "
                    + "0 turns the log line off.",
                new AcceptableValueRange<float>(0f, 120f)
            )
        );
        AmbientOcclusion = Plugin.Settings.Bind(
            section,
            "AmbientOcclusion",
            false,
            new ConfigDescription(
                "Keep the game's HBAO ambient occlusion in VR. Off by default: it is screen-space and runs once per "
                    + "eye, roughly doubling its cost, and its halo differs between the eyes at depth edges."
            )
        );
        EdgeDetection = Plugin.Settings.Bind(
            section,
            "EdgeDetection",
            false,
            new ConfigDescription(
                "Keep the game's outline (edge detection) pass in VR. Off by default: it runs full screen once per "
                    + "eye. It works per eye in MultiPass; turn it on for the game's outlined look."
            )
        );
        ShadowDistance = Plugin.Settings.Bind(
            section,
            "ShadowDistance",
            60f,
            new ConfigDescription(
                "Furthest distance (m) that casts shadows in VR, capping the game's Shadow Distance setting (150 m on "
                    + "Medium). Shadow maps render once per eye, so a shorter distance saves GPU time.",
                new AcceptableValueRange<float>(0f, 200f)
            )
        );
        ComfortSettings = Plugin.Settings.Bind(
            section,
            "ForceComfortSettings",
            true,
            new ConfigDescription(
                "While in VR, turn on the game's 'Reduce Camera Bobbing' and photosensitive mode (halved shake, "
                    + "softer screen flashes). Your saved game settings are restored when VR stops."
            )
        );

        RenderPatches.Resolve();
        Patch(typeof(RenderPatches.RenderScaleApply));
        Patch(typeof(RenderPatches.FpsCapApply));
        Patch(typeof(RenderPatches.VSyncApply));
        Patch(typeof(RenderPatches.ShadowDistanceApply));
        Patch(typeof(RenderPatches.VolumeEnable));
        Patch(typeof(RenderPatches.LimitFrameRateStart));
        Patch(typeof(RenderPatches.PostHandlerLateUpdate));

        var go = new GameObject("PeakXR.RenderFixes") { hideFlags = HideFlags.HideAndDontSave };
        Object.DontDestroyOnLoad(go);
        go.AddComponent<RenderDriver>();
    }
}
