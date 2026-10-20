using System.Reflection;
using HarmonyLib;
using PeakXR.Framework;
using PeakXR.VR;

namespace PeakXR.Interact.Binoculars;

/// <summary>Harmony patches of <see cref="BinocularsFeature"/>. VR only; flat play runs vanilla code unchanged.</summary>
internal static class BinocularsPatches
{
    /// <summary>
    /// Action_ShowBinocularOverlay.RunAction (Action_ShowBinocularOverlay.cs:23-31): toggles
    /// <c>binocularsActive</c>, enables the item's overlay renderer feature (ItemRenderFeatureManager.cs:41) and
    /// sets <c>MainCamera.camOverride</c> (MainCamera.cs:27), which MainCameraMovement.OverrideCam
    /// (MainCameraMovement.cs:309) applies as the headset pose and FOV. In VR the postfix turns the overlay
    /// feature back off, clears only this binoculars' override, and starts or stops the lens view. The
    /// ToggleUseRPC sync and the toggle itself stay vanilla.
    /// </summary>
    [HarmonyPatch]
    internal static class Toggle
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(Action_ShowBinocularOverlay), "RunAction");

        private static void Postfix(Action_ShowBinocularOverlay __instance)
        {
            if (!VRSession.Active || __instance.isProp)
                return;
            if (__instance.binocularsActive)
                BinocularLens.Begin(__instance);
            else
                BinocularLens.End(__instance);
        }
    }
}
