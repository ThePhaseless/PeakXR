using BepInEx.Configuration;
using PeakXR.Core;

namespace PeakXR.Framework;

/// <summary>Moves a saved setting off a superseded default, once (<see cref="SettingMigration"/>). Call right
/// after <c>Bind</c> in <c>Install()</c>, naming the old default at the call site. A hidden marker in the
/// <c>Migrations</c> section records the run, so a player who sets the old value back keeps it.</summary>
internal static class ConfigMigration
{
    private const string Section = "Migrations";

    /// <summary>A switch whose default flipped: as the float form, with off = 0 and on = 1.</summary>
    public static void FromOldDefault(ConfigEntry<bool> entry, bool oldDefault)
    {
        var def = entry.Definition;
        var marker = Marker(def, oldDefault ? 1f : 0f);
        var move = SettingMigration.ShouldMove(
            entry.Value ? 1f : 0f,
            oldDefault ? 1f : 0f,
            (bool)entry.DefaultValue ? 1f : 0f,
            marker.Value
        );
        marker.Value = true;
        if (!move)
            return;
        entry.Value = (bool)entry.DefaultValue;
        Plugin.Log.LogInfo(
            $"[config] {def.Section}.{def.Key}: old default {oldDefault} moved to {entry.Value}"
        );
    }

    private static ConfigEntry<bool> Marker(ConfigDefinition def, float oldDefault) =>
        Plugin.Settings.Bind(
            Section,
            SettingMigration.MarkerKey(def.Section, def.Key, oldDefault),
            false,
            // "Hidden" is VRTab.HiddenTag: kept out of the in-game settings tab.
            new ConfigDescription("Internal: this default migration has run.", null, "Hidden")
        );

    public static void FromOldDefault(ConfigEntry<float> entry, float oldDefault)
    {
        var def = entry.Definition;
        var marker = Marker(def, oldDefault);
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
