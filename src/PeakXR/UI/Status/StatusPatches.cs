using System.Reflection;
using HarmonyLib;
using PeakXR.Framework;
using PeakXR.UI.Common;
using PeakXR.UI.World;
using PeakXR.VR;

namespace PeakXR.UI.Status;

/// <summary>Pulses for stamina gains, on the wrist that carries the bar in wrist mode, else on both hands.</summary>
internal static class StatusHaptics
{
    public static void Pulse(float share, float seconds)
    {
        if (!VRSession.Active)
            return;
        var amplitude = UIStatusFeature.Haptics.Value * share;
        if (amplitude <= 0f)
            return;
        var cfg = UIWorldFeature.Settings;
        if (cfg != null && cfg.Hud.Value == HudMode.Wrist)
        {
            UIHaptics.Tick(cfg.WristHand.Value == WristSide.Left, amplitude, seconds);
            return;
        }
        UIHaptics.Tick(true, amplitude, seconds);
        UIHaptics.Tick(false, amplitude, seconds);
    }
}

/// <summary>Campfire morale boost (IN-28, ST-04): the bar animates MORALEBOOST, the hands feel it.
/// A/Character.cs:1595 <c>public void MoraleBoost(float staminaAdd, int scoutCount)</c> [PunRPC].</summary>
[HarmonyPatch]
internal static class MoraleBoostPatch
{
    private static MethodBase TargetMethod() =>
        GameMember.Method(typeof(Character), nameof(Character.MoraleBoost));

    private static void Postfix(Character __instance)
    {
        if (__instance.IsLocal)
            StatusHaptics.Pulse(1f, 0.35f);
    }
}

/// <summary>Bonus stamina from energy items and mushrooms (IC-48, ST-04): a short tick as the extra bar grows.
/// A/Character.cs:1630 <c>public void AddExtraStamina(float add)</c>.</summary>
[HarmonyPatch]
internal static class ExtraStaminaPatch
{
    private static MethodBase TargetMethod() =>
        GameMember.Method(typeof(Character), nameof(Character.AddExtraStamina));

    private static void Postfix(Character __instance, float add)
    {
        if (add > 0.001f && __instance.IsLocal)
            StatusHaptics.Pulse(0.5f, 0.08f);
    }
}
