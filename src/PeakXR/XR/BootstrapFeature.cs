using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using PeakXR.Framework;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.XR;

public enum DepthSubmission
{
    None,
    Depth16Bit,
    Depth24Bit,
}

/// <summary>
/// Feature <c>xr.bootstrap</c>: loads the managed XR assemblies from RuntimeDeps, creates the XR
/// Management settings and the OpenXR (or OpenVR) loader in code, starts the subsystems and sets
/// <see cref="VRSession.Active"/> once a display subsystem runs. OpenXR falls back to OpenVR when the
/// preloader registered it, and anything else falls back to flat play. While running it watches for
/// the runtime shutting down and for focus loss (dashboard open), which pauses the game's input and
/// dims the view.
/// </summary>
public sealed class BootstrapFeature : Feature
{
    internal static ConfigEntry<DepthSubmission> DepthSubmissionEntry;
    internal static ConfigEntry<bool> PredictedTime;
    internal static ConfigEntry<float> StartTimeout;
    internal static ConfigEntry<bool> FallbackToOpenVR;
    internal static ConfigEntry<bool> PauseOnFocusLoss;
    internal static ConfigEntry<bool> LogInputDevices;

    public override string Id => "xr.bootstrap";

    /// <summary>Very early: every other VR feature reacts to <see cref="VRSession.Started"/>.</summary>
    public override int Order => -1000;

    protected internal override void Install()
    {
        DepthSubmissionEntry = Plugin.Settings.Bind(
            "XR",
            "DepthSubmission",
            DepthSubmission.None,
            new ConfigDescription(
                "Depth buffer sent to the VR runtime with each frame (OpenXR). Runtimes can use it for "
                    + "reprojection. None is the safest under Proton; try Depth24Bit if reprojection wobbles."
            )
        );
        PredictedTime = Plugin.Settings.Bind(
            "XR",
            "RuntimePredictedTime",
            false,
            new ConfigDescription(
                "Let the OpenXR runtime predict the display time of each frame instead of Unity. "
                    + "Set explicitly so an OpenXR package update cannot change it (its default flipped in 1.17)."
            )
        );
        StartTimeout = Plugin.Settings.Bind(
            "XR",
            "StartTimeoutSeconds",
            20f,
            new ConfigDescription(
                "How long to wait for the headset to start rendering before trying the next runtime.",
                new AcceptableValueRange<float>(5f, 120f)
            )
        );
        FallbackToOpenVR = Plugin.Settings.Bind(
            "XR",
            "FallbackToOpenVR",
            true,
            new ConfigDescription(
                "When OpenXR does not start, try SteamVR's OpenVR plugin before falling back to flat play. "
                    + "Needs the preloader setting Runtime.OpenVRFallback (on by default)."
            )
        );
        PauseOnFocusLoss = Plugin.Settings.Bind(
            "XR",
            "PauseInputOnFocusLoss",
            true,
            new ConfigDescription(
                "Ignore controller and keyboard input for your character while the VR runtime has input "
                    + "focus (SteamVR dashboard open, headset off) or the headset's wear sensor reads it taken off."
            )
        );
        LogInputDevices = Plugin.Settings.Bind(
            "XR",
            "LogInputDevices",
            true,
            new ConfigDescription(
                "Write every connected XR device and its input usages to the log (helps when a button does not work)."
            )
        );

        // Missing XR Management means no backend can work: fail this feature, keep the game flat.
        if (!XRAssemblyLoader.TryLoad(XRAssemblyLoader.Management))
            throw new System.IO.FileNotFoundException(
                $"{XRAssemblyLoader.Management}.dll is missing from {XRAssemblyLoader.DepsDir}"
            );

        // Optional: without it focus loss only dims the view.
        try
        {
            FocusInputPatch.CanDoInput = GameMember.Method(
                typeof(Character),
                "CanDoInput",
                System.Type.EmptyTypes
            );
            Patch(typeof(FocusInputPatch));
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning(
                $"[xr.bootstrap] Focus-loss input pause unavailable: {e.GetBaseException().Message}"
            );
        }

        if (VRSession.CompatProbe)
            return;

        var go = new GameObject("PeakXR.XR") { hideFlags = HideFlags.HideAndDontSave };
        Object.DontDestroyOnLoad(go);
        go.AddComponent<XRBootstrapDriver>();
    }
}

/// <summary>
/// Blocks the local character's input while the runtime has focus (dashboard open) or the headset is off.
/// Target: <c>Character.CanDoInput()</c> (internal, bool), decomp Assembly-CSharp/Character.cs:1711;
/// it gates <c>character.input.Sample(...)</c> at CharacterMovement.cs:198.
/// </summary>
[HarmonyPatch]
static class FocusInputPatch
{
    internal static MethodInfo CanDoInput;

    static MethodBase TargetMethod() => CanDoInput;

    static void Postfix(Character __instance, ref bool __result)
    {
        if (
            __result
            && VRSession.Active
            && !VRSession.HasFocus
            && BootstrapFeature.PauseOnFocusLoss.Value
            && __instance.IsLocal
        )
            __result = false;
    }
}
