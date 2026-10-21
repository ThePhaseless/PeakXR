using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PeakXR.Framework;
using UnityEngine;

namespace PeakXR.UI.Settings;

/// <summary>
/// ui.settings: a "VR" tab in the game's own settings menu (title and pause), generated from every
/// PeakXR config entry, plus the feature compatibility report. Runs in flat mode too, so the report
/// and the settings are reachable without a headset.
/// </summary>
public sealed class UISettingsFeature : Feature
{
    public override string Id => "ui.settings";

    public override int Order => 60;

    public override bool VROnly => false;

    internal static AccessTools.FieldRef<SharedSettingsMenu, SettingsTABS> TabsRef;
    internal static AccessTools.FieldRef<SharedSettingsMenu, List<SettingsUICell>> CellsRef;

    protected internal override void Install()
    {
        TabsRef = GameMember.Field<SharedSettingsMenu, SettingsTABS>("m_tabs");
        CellsRef = GameMember.Field<SharedSettingsMenu, List<SettingsUICell>>("m_spawnedCells");

        Patch(typeof(SettingsMenuEnablePatch));
        Patch(typeof(ShowSettingsPatch));
        Patch(typeof(LocalizationReloadPatch));

        Plugin.Settings.SettingChanged += VRTab.OnSettingChanged;
        var saver = new GameObject("PeakXR_SettingsSaver");
        Object.DontDestroyOnLoad(saver);
        saver.AddComponent<ConfigSaver>();
    }
}

/// <summary>Saves the config file shortly after the last change made from the tab.</summary>
internal sealed class ConfigSaver : MonoBehaviour
{
    private void Update() => ConfigWriter.Tick(force: false);

    private void OnApplicationQuit() => ConfigWriter.Tick(force: true);
}

/// <summary>
/// Adds the VR tab button by cloning an existing tab before <c>TABS.Start</c> collects them; on later
/// enables the clone already exists. A/SharedSettingsMenu.cs:22 <c>private void OnEnable()</c>.
/// </summary>
[HarmonyPatch]
internal static class SettingsMenuEnablePatch
{
    private static MethodBase TargetMethod() =>
        GameMember.Method(typeof(SharedSettingsMenu), "OnEnable");

    private static void Prefix(SharedSettingsMenu __instance)
    {
        var tabs = UISettingsFeature.TabsRef(__instance);
        if (tabs == null)
            return;
        var buttons = tabs.GetComponentsInChildren<SettingsTABSButton>(true);
        if (buttons.Length == 0)
            return;
        foreach (var b in buttons)
            if (b.category == VRTab.Category)
                return;

        var source = buttons[buttons.Length - 1];
        var clone = Object.Instantiate(source, source.transform.parent);
        clone.name = "PeakXR_VRTab";
        clone.category = VRTab.Category;
        clone.Deselect();
        var label = clone.text != null ? clone.text.GetComponent<LocalizedText>() : null;
        if (label != null)
            label.SetIndex(Loc.Set(VRTab.TabKey, "VR"));
        else if (clone.text != null)
            clone.text.text = "VR";

        // TABS.Start (Zorro.UI TABS.cs:14) already ran on an earlier enable: register by hand.
        if (tabs.buttons.Count > 0)
            tabs.AddButton(clone);
    }
}

/// <summary>
/// Fills the VR tab. The game spawns nothing for the unknown category, then the generated rows are
/// spawned with the game's own cell prefab and added to its cell list so the next tab switch clears them.
/// A/SharedSettingsMenu.cs:39 <c>public void ShowSettings(SettingsCategory category)</c>.
/// </summary>
[HarmonyPatch]
internal static class ShowSettingsPatch
{
    private static MethodBase TargetMethod() =>
        GameMember.Method(
            typeof(SharedSettingsMenu),
            nameof(SharedSettingsMenu.ShowSettings),
            new[] { typeof(SettingsCategory) }
        );

    private static void Postfix(SharedSettingsMenu __instance, SettingsCategory category)
    {
        if (category != VRTab.Category)
            return;
        var cells = UISettingsFeature.CellsRef(__instance);
        var first = true;
        foreach (var setting in VRTab.BuildRows())
        {
            var cell = Object
                .Instantiate(__instance.m_settingsCellPrefab, __instance.m_settingsContentParent)
                .GetComponent<SettingsUICell>();
            cells.Add(cell);
            try
            {
                cell.Setup(setting);
            }
            catch (System.Exception e)
            {
                // One row the game can't draw (missing cell prefab in a future build) must not empty the tab.
                Plugin.Log.LogWarning(
                    $"[ui.settings] Row {setting.GetType().Name} failed: {e.GetBaseException().Message}"
                );
                continue;
            }
            // The game staggers fade-ins 50 ms apart; all at once, only the first cell plays its sound.
            var sfx = cell.fadeInSFX;
            if (!first)
                cell.fadeInSFX = null;
            cell.FadeIn();
            cell.fadeInSFX = sfx;
            first = false;
        }
    }
}

/// <summary>Re-injects PeakXR strings after a language change rebuilds the table. A/LocalizedText.cs:206.</summary>
[HarmonyPatch]
internal static class LocalizationReloadPatch
{
    private static MethodBase TargetMethod() =>
        GameMember.Method(typeof(LocalizedText), nameof(LocalizedText.LoadMainTable));

    private static void Postfix() => Loc.Reinject();
}
