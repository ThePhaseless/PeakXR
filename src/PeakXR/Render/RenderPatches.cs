using System;
using System.Reflection;
using HarmonyLib;
using PeakXR.Framework;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.Rendering;

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

    /// <summary>Peak.LimitFrameRate.Start (Peak/LimitFrameRate.cs:9) caps the game at 144 on scene start.</summary>
    [HarmonyPatch]
    internal static class LimitFrameRateStart
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(Peak.LimitFrameRate), "Start");

        private static bool Prefix() => !VRSession.Active;
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
