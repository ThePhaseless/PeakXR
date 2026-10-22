using System.Reflection;
using HarmonyLib;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.UI.Affordances;
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
            UIHaptics.Tick(cfg.WristLeft, amplitude, seconds);
            return;
        }
        UIHaptics.Tick(true, amplitude, seconds);
        UIHaptics.Tick(false, amplitude, seconds);
    }
}

/// <summary>Campfire morale boost (IN-28, ST-04): the bar animates MORALEBOOST, the hands feel it.
/// A/Character.cs:1592 <c>public void MoraleBoost(float staminaAdd, int scoutCount)</c> [PunRPC].</summary>
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
/// A/Character.cs:1627 <c>public void AddExtraStamina(float add)</c>.</summary>
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

/// <summary>
/// The MORALEBOOST word on the wrist bar (B3) is text to read mid-climb: in VR the bar's own boost animation plays
/// without the word, with the rising chord at the wrist; <see cref="MoraleBoostPatch"/> already pulses the hands.
/// A/StaminaBar.cs:223 <c>public void PlayMoraleBoost(int scoutCount)</c>.
/// </summary>
[HarmonyPatch]
internal static class MoraleBoostTextPatch
{
    /// <summary>Boosts shown without the word (self-test).</summary>
    internal static int Shown { get; private set; }

    private static MethodBase TargetMethod() =>
        GameMember.Method(typeof(StaminaBar), nameof(StaminaBar.PlayMoraleBoost));

    private static bool Prefix(StaminaBar __instance)
    {
        if (!Affordance.Enabled)
            return true;
        Shown++;
        if (__instance.moraleBoostText != null)
            __instance.moraleBoostText.enabled = false;
        if (__instance.moraleBoostAnimator != null)
            __instance.moraleBoostAnimator.Play("Boost", 0, 0f);
        var spot = DenyHint.Wrist();
        Affordance.Play(
            CueSound.WouldWork,
            spot.HasSpot ? spot.SpotNow : __instance.transform.position
        );
        return false;
    }
}
