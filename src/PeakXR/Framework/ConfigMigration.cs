using BepInEx.Configuration;
using PeakXR.Core;

namespace PeakXR.Framework;

/// <summary>Moves a saved setting off a superseded default (<see cref="SettingMigration"/>). Call right after
/// <c>Bind</c> in <c>Install()</c>, naming the old default at the call site.</summary>
internal static class ConfigMigration
{
    public static void FromOldDefault(ConfigEntry<float> entry, float oldDefault)
    {
        var newDefault = (float)entry.DefaultValue;
        if (!SettingMigration.ShouldMove(entry.Value, oldDefault, newDefault))
            return;
        entry.Value = newDefault;
        Plugin.Log.LogInfo(
            $"[config] {entry.Definition.Section}.{entry.Definition.Key}: old default {oldDefault} moved to {newDefault}"
        );
    }
}
