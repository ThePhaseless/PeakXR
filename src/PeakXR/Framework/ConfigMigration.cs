using BepInEx.Configuration;
using PeakXR.Core;

namespace PeakXR.Framework;

/// <summary>Moves a saved setting off a superseded default, once (<see cref="SettingMigration"/>). Call right
/// after <c>Bind</c> in <c>Install()</c>, naming the old default at the call site. A hidden marker in the
/// <c>Migrations</c> section records the run, so a player who sets the old value back keeps it.</summary>
internal static class ConfigMigration
{
    private const string Section = "Migrations";

    public static void FromOldDefault(ConfigEntry<float> entry, float oldDefault)
    {
        var def = entry.Definition;
        var marker = Plugin.Settings.Bind(
            Section,
            SettingMigration.MarkerKey(def.Section, def.Key, oldDefault),
            false,
            // "Hidden" is VRTab.HiddenTag: kept out of the in-game settings tab.
            new ConfigDescription("Internal: this default migration has run.", null, "Hidden")
        );
        var newDefault = (float)entry.DefaultValue;
        var move = SettingMigration.ShouldMove(entry.Value, oldDefault, newDefault, marker.Value);
        marker.Value = true;
        if (!move)
            return;
        entry.Value = newDefault;
        Plugin.Log.LogInfo(
            $"[config] {def.Section}.{def.Key}: old default {oldDefault} moved to {newDefault}"
        );
    }
}
