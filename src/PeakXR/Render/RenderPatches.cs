using System;
using System.Reflection;
using HarmonyLib;
using PeakXR.Framework;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PeakXR.Render;

/// <summary>Harmony patches of <see cref="RenderFixesFeature"/>; each one only acts while XR renders.</summary>
internal static class RenderPatches
{
    /// <summary>EnumSetting&lt;OffOnMode&gt;.Value setter (Zorro.Settings EnumSetting.cs:12, protected).</summary>
    internal static MethodInfo OffOnValueSetter;

    public static void Resolve()
    {
        OffOnValueSetter =
            AccessTools.PropertySetter(
                typeof(Zorro.Settings.EnumSetting<Zorro.Settings.OffOnMode>),
                "Value"
            ) ?? throw new MissingMethodException("EnumSetting<OffOnMode>", "set_Value");
    }

    /// <summary>
    /// RenderScaleSetting.ApplyValue (RenderScaleSetting.cs:18) writes the shared URP asset's renderScale and
    /// switches upscaling to STP below Native. In VR the render.fixes values own the asset instead.
    /// </summary>
    [HarmonyPatch]
    internal static class RenderScaleApply
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(RenderScaleSetting), nameof(RenderScaleSetting.ApplyValue));

        private static bool Prefix() => !VRSession.Active;
    }

    /// <summary>FPSCapSetting.ApplyValue (FPSCapSetting.cs:7) sets Application.targetFrameRate: a cap below
    /// the headset's refresh rate would judder. The runtime paces frames in VR.</summary>
    [HarmonyPatch]
    internal static class FpsCapApply
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(FPSCapSetting), nameof(FPSCapSetting.ApplyValue));

        private static bool Prefix() => !VRSession.Active;
    }

    /// <summary>VSyncSetting.ApplyValue (VSyncSetting.cs:14) sets QualitySettings.vSyncCount: the desktop
    /// monitor's vsync must not throttle the headset.</summary>
    [HarmonyPatch]
    internal static class VSyncApply
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(VSyncSetting), nameof(VSyncSetting.ApplyValue));

        private static bool Prefix() => !VRSession.Active;
    }

    /// <summary>ShadowDistanceSettings.ApplyValue (ShadowDistanceSettings.cs:17) writes the shared URP asset's
    /// shadow distance and cascades; in VR render.fixes caps the distance instead. RenderDriver re-applies the
    /// game value when VR stops.</summary>
    [HarmonyPatch]
    internal static class ShadowDistanceApply
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(
                typeof(ShadowDistanceSettings),
                nameof(ShadowDistanceSettings.ApplyValue)
            );

        private static bool Prefix() => !VRSession.Active;
    }

    /// <summary>Volume.OnEnable (Unity.RenderPipelines.Core.Runtime, private): volumes spawned or switched on after
    /// the scene loaded, such as the warp compass VFX (WarpCompassVFX.cs:7-9, toggled at :44-45), get the same
    /// comfort pass as the scene's volumes.</summary>
    [HarmonyPatch]
    internal static class VolumeEnable
    {
        private static MethodBase TargetMethod() => GameMember.Method(typeof(Volume), "OnEnable");

        private static void Postfix(Volume __instance)
        {
            if (VRSession.Active)
                RenderDriver.OnVolumeEnabled(__instance);
        }
    }

    /// <summary>Peak.LimitFrameRate.Start (Peak/LimitFrameRate.cs:9) caps the game at 144 on scene start.</summary>
    [HarmonyPatch]
    internal static class LimitFrameRateStart
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(Peak.LimitFrameRate), "Start");

        private static bool Prefix() => !VRSession.Active;
    }

    /// <summary>
    /// DisableIfPhotosensitive.Start (DisableIfPhotosensitive.cs:10) switches an effect off at spawn when photosensitive
    /// mode is on. The flare's flame is one (VFX_FlareSmoke's spark child, spawned by Flare.EnableFlareVisuals,
    /// Flare.cs:60, with no stand-in), so the forced comfort setting left a lit flare with only smoke and sound. When
    /// photosensitive mode is on only because render.fixes forced it (for shake and flashes), the effect stays.
    /// </summary>
    [HarmonyPatch]
    internal static class DisableIfPhotosensitiveStart
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(DisableIfPhotosensitive), "Start");

        private static bool Prefix() => !(VRSession.Active && RenderDriver.PhotosensitiveForced);
    }

    /// <summary>
    /// PostProcessUtils.ConfigureDithering (URP PostProcessUtils.cs:28) steps its blue-noise texture index on every call
    /// (:35), and MultiPass records the post passes once per eye (PostProcessPassRenderGraph.cs:1828 final pass, :2234
    /// uber pass, both keeping the index in the pass object), so each eye dithered with a different noise texture. In VR
    /// the index follows the frame instead, so both eyes pick the same texture; the offsets already follow the frame
    /// (Random.InitState(Time.frameCount), :40). The postfix records each frame's picks for self-test vr.278.
    /// </summary>
    [HarmonyPatch]
    internal static class DitherSameInBothEyes
    {
        /// <summary>The frame last seen, its first texture index, calls in it and calls that picked another index.</summary>
        internal static int Frame = -1;
        internal static int First;
        internal static int Calls;
        internal static int Mismatches;

        /// <summary>Frames with two or more picks (both eyes), and those whose picks all matched.</summary>
        internal static int StereoFrames;
        internal static int MatchedFrames;

        private static MethodBase TargetMethod() =>
            GameMember.Method(
                typeof(PostProcessUtils),
                nameof(PostProcessUtils.ConfigureDithering),
                [typeof(PostProcessData), typeof(int), typeof(int), typeof(int), typeof(Material)]
            );

        private static void Prefix(PostProcessData data, ref int index)
        {
            if (!VRSession.Active || data == null || data.textures == null)
                return;
            var noise = data.textures.blueNoise16LTex;
            // ConfigureDithering adds one before it picks: this lands on frameCount % length.
            if (noise != null && noise.Length > 0)
                index = Time.frameCount % noise.Length - 1;
        }

        private static void Postfix(int __result)
        {
            if (!VRSession.Active)
                return;
            if (Time.frameCount != Frame)
            {
                if (Calls >= 2)
                {
                    StereoFrames++;
                    if (Mismatches == 0)
                        MatchedFrames++;
                }
                Frame = Time.frameCount;
                First = __result;
                Calls = 0;
                Mismatches = 0;
            }
            Calls++;
            if (__result != First)
                Mismatches++;
        }
    }

    /// <summary>
    /// PostHandler.LateUpdate (PostHandler.cs:17) re-enables the HBAO volume component from AOSetting every
    /// frame. Postfix: in VR it stays off unless Render.AmbientOcclusion is on. HBAO is found by type name
    /// because its assembly (HBAO.Universal.Runtime) is not referenced.
    /// </summary>
    [HarmonyPatch]
    internal static class PostHandlerLateUpdate
    {
        private static VolumeProfile cachedProfile;
        private static VolumeComponent cachedHbao;

        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(PostHandler), "LateUpdate");

        private static void Postfix(PostHandler __instance)
        {
            if (
                !VRSession.Active
                || RenderFixesFeature.AmbientOcclusion.Value
                || __instance.volume == null
            )
                return;
            var profile = __instance.volume.sharedProfile;
            if (profile == null)
                return;
            if (profile != cachedProfile)
            {
                cachedProfile = profile;
                cachedHbao = null;
                var list = profile.components;
                for (int i = 0; i < list.Count; i++)
                    if (list[i] != null && list[i].GetType().Name == "HBAO")
                        cachedHbao = list[i];
            }
            if (cachedHbao != null)
                cachedHbao.active = false;
        }
    }
}
