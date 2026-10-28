using System;
using System.Collections.Generic;
using System.Globalization;
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

    /// <summary>A choice whose default changed, compared by its number (BepInEx saves the name, so a saved
    /// value keeps its meaning).</summary>
    public static void FromOldDefault<T>(ConfigEntry<T> entry, T oldDefault)
        where T : struct, Enum
    {
        var def = entry.Definition;
        float old = Convert.ToInt32(oldDefault, CultureInfo.InvariantCulture);
        var marker = Marker(def, old);
        var move = SettingMigration.ShouldMove(
            Convert.ToInt32(entry.Value, CultureInfo.InvariantCulture),
            old,
            Convert.ToInt32(entry.DefaultValue, CultureInfo.InvariantCulture),
            marker.Value
        );
        marker.Value = true;
        if (!move)
            return;
        entry.Value = (T)entry.DefaultValue;
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

    /// <param name="inEffect">The saved value is what the player has been playing with, so it counts as their
    /// choice: the migration is marked as run without moving it.</param>
    public static void FromOldDefault(
        ConfigEntry<float> entry,
        float oldDefault,
        bool inEffect = false
    )
    {
        var def = entry.Definition;
        var marker = Marker(def, oldDefault);
        var newDefault = (float)entry.DefaultValue;
        var move =
            !inEffect
            && SettingMigration.ShouldMove(entry.Value, oldDefault, newDefault, marker.Value);
        marker.Value = true;
        if (!move)
            return;
        entry.Value = newDefault;
        Plugin.Log.LogInfo(
            $"[config] {def.Section}.{def.Key}: old default {oldDefault} moved to {newDefault}"
        );
    }

    /// <summary>
    /// A setting whose default and scale changed together (<see cref="SettingMigration.RescaleSaved"/>): the old
    /// default moves to the new one, any other saved value is multiplied by <paramref name="factor"/> and clamped,
    /// once. <paramref name="savedText"/> is the text the file held before this version bound it
    /// (<see cref="SavedText"/>, read before <c>Bind</c>), null when it held none: a fresh value is already on the
    /// new scale. Text that does not parse is left at the default BepInEx fell back to, with one log line.
    /// </summary>
    public static void Rescale(
        ConfigEntry<float> entry,
        float oldDefault,
        float factor,
        float min,
        float max,
        string savedText
    )
    {
        var def = entry.Definition;
        var marker = Marker(def, oldDefault);
        bool ran = marker.Value;
        marker.Value = true;
        if (
            !SettingMigration.RescaleSaved(
                savedText,
                oldDefault,
                (float)entry.DefaultValue,
                factor,
                min,
                max,
                ran,
                out float after
            )
        )
        {
            if (!ran && savedText != null)
                Plugin.Log.LogWarning(
                    $"[config] {def.Section}.{def.Key}: saved value '{savedText}' does not parse; left at {entry.Value}"
                );
            return;
        }
        float before = entry.Value;
        if (after == before)
            return;
        entry.Value = after;
        Plugin.Log.LogInfo($"[config] {def.Section}.{def.Key}: {savedText} rescaled to {after}");
    }

    private static System.Reflection.MethodInfo orphans;

    /// <summary>
    /// The text <paramref name="file"/> holds for the key that nothing has bound yet, or null: BepInEx keeps such
    /// values in its protected <c>OrphanedEntries</c> until <c>Bind</c> claims them. Call before binding.
    /// </summary>
    public static string SavedText(ConfigFile file, string section, string key)
    {
        orphans ??= HarmonyLib.AccessTools.PropertyGetter(typeof(ConfigFile), "OrphanedEntries");
        return
            orphans?.Invoke(file, null) is Dictionary<ConfigDefinition, string> entries
            && entries.TryGetValue(new ConfigDefinition(section, key), out var text)
            ? text
            : null;
    }
}
