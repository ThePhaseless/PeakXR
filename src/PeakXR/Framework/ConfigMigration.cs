using System.Collections.Generic;
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

    /// <summary>True the first time a named migration runs, false ever after (a hidden marker records it).</summary>
    public static bool Once(string key)
    {
        var marker = Plugin.Settings.Bind(
            Section,
            key,
            false,
            // "Hidden" is VRTab.HiddenTag: kept out of the in-game settings tab.
            new ConfigDescription("Internal: this migration has run.", null, "Hidden")
        );
        if (marker.Value)
            return false;
        marker.Value = true;
        return true;
    }

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

    /// <summary>
    /// A setting whose default and scale changed together (<see cref="SettingMigration.Rescaled"/>): the old default
    /// moves to the new one, any other saved value is multiplied by <paramref name="factor"/> and clamped, once.
    /// <paramref name="saved"/> says whether the file held a value before this version bound it
    /// (<see cref="Saved"/>, read before <c>Bind</c>); a fresh value is already on the new scale.
    /// </summary>
    public static void Rescale(
        ConfigEntry<float> entry,
        float oldDefault,
        float factor,
        float min,
        float max,
        bool saved
    )
    {
        var def = entry.Definition;
        var marker = Marker(def, oldDefault);
        float before = entry.Value;
        float after = SettingMigration.Rescaled(
            before,
            oldDefault,
            (float)entry.DefaultValue,
            factor,
            min,
            max,
            saved,
            marker.Value
        );
        marker.Value = true;
        if (after == before)
            return;
        entry.Value = after;
        Plugin.Log.LogInfo($"[config] {def.Section}.{def.Key}: {before} rescaled to {after}");
    }

    private static System.Reflection.MethodInfo orphans;

    /// <summary>
    /// Whether <paramref name="file"/> holds a saved value for the key that nothing has bound yet: BepInEx keeps
    /// such values in its protected <c>OrphanedEntries</c> until <c>Bind</c> claims them. Call before binding.
    /// </summary>
    public static bool Saved(ConfigFile file, string section, string key)
    {
        orphans ??= HarmonyLib.AccessTools.PropertyGetter(typeof(ConfigFile), "OrphanedEntries");
        return orphans?.Invoke(file, null) is Dictionary<ConfigDefinition, string> entries
            && entries.ContainsKey(new ConfigDefinition(section, key));
    }
}
