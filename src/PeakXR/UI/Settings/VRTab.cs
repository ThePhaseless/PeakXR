using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using PeakXR.Core;
using PeakXR.Framework;
using UnityEngine;
using Zorro.Settings;

namespace PeakXR.UI.Settings;

/// <summary>
/// Content of the "VR" settings tab, generated from <see cref="Plugin.Settings"/> every time the tab is
/// shown, so a feature only binds config entries and never touches UI code. Rows are grouped by config
/// section and end with a read-only compatibility report of every feature.
/// </summary>
internal static class VRTab
{
    /// <summary>The settings menu compares categories by <c>ToString()</c>, so an unnamed enum value is a
    /// new tab without touching the enum (A/SharedSettingsMenu.cs:52).</summary>
    public const SettingsCategory Category = (SettingsCategory)100;

    public static readonly string CategoryName = Category.ToString();

    public const string TabKey = "PEAKXR_TAB";

    /// <summary>Description tag that keeps an entry out of the tab (internal state, calibration data).</summary>
    public const string HiddenTag = "Hidden";

    private static readonly Color Active = new(0.55f, 0.85f, 0.55f, 1f);
    private static readonly Color Failed = new(0.95f, 0.45f, 0.4f, 1f);
    private static readonly Color Muted = new(0.7f, 0.7f, 0.7f, 1f);

    private static readonly Dictionary<ConfigEntryBase, Setting> rows = new();

    /// <summary>Keeps open rows in sync when an entry changes elsewhere.</summary>
    public static void OnSettingChanged(object sender, SettingChangedEventArgs e)
    {
        if (
            !ConfigWriter.Writing
            && rows.TryGetValue(e.ChangedSetting, out var row)
            && row is IConfigRow configRow
        )
            configRow.Refresh();
    }

    public static List<Setting> BuildRows()
    {
        Loc.Set(TabKey, "VR");
        var sections = new List<string>();
        var bySection = new Dictionary<string, List<ConfigEntryBase>>(StringComparer.Ordinal);
        foreach (var pair in Plugin.Settings)
        {
            var entry = pair.Value;
            if (IsHidden(entry))
                continue;
            var section = entry.Definition.Section;
            if (!bySection.TryGetValue(section, out var list))
            {
                bySection[section] = list = new List<ConfigEntryBase>();
                sections.Add(section);
            }
            list.Add(entry);
        }

        var result = new List<Setting>();
        foreach (var section in sections)
        {
            result.Add(Header("PEAKXR_SECTION_" + section, section));
            foreach (var entry in bySection[section])
                if (RowFor(entry) is { } row)
                    result.Add(row);
        }

        result.Add(Header("PEAKXR_SECTION_COMPAT", "Compatibility"));
        foreach (
            var feature in FeatureManager
                .Features.OrderBy(f => f.Order)
                .ThenBy(f => f.Id, StringComparer.Ordinal)
        )
        {
            var text =
                feature.FailureReason == null
                    ? feature.State.ToString()
                    : $"{feature.State}: {feature.FailureReason}";
            var color = feature.State switch
            {
                FeatureState.Active => Active,
                FeatureState.Failed => Failed,
                _ => Muted,
            };
            result.Add(
                new InfoRow(Loc.Set("PEAKXR_FEATURE_" + feature.Id, feature.Id), text, color, false)
            );
        }
        return result;
    }

    private static InfoRow Header(string key, string title) =>
        new(Loc.Set(key, title.ToUpperInvariant()), null, Color.white, true);

    private static bool IsHidden(ConfigEntryBase entry)
    {
        var tags = entry.Description?.Tags;
        if (tags == null)
            return false;
        foreach (var tag in tags)
            if (tag is string s && string.Equals(s, HiddenTag, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static Setting RowFor(ConfigEntryBase entry)
    {
        if (rows.TryGetValue(entry, out var cached))
            return cached;

        var type = entry.SettingType;
        var acceptable = entry.Description?.AcceptableValues;
        var acceptableType = acceptable?.GetType();
        var generic = acceptableType is { IsGenericType: true }
            ? acceptableType.GetGenericTypeDefinition()
            : null;
        var hasRange = generic == typeof(AcceptableValueRange<>);
        var hasList = generic == typeof(AcceptableValueList<>);
        var nameKey = Loc.Set(
            $"PEAKXR_CFG_{entry.Definition.Section}_{entry.Definition.Key}",
            SettingsRows.Prettify(entry.Definition.Key)
        );

        Setting row = SettingsRows.Classify(
            type,
            hasRange,
            hasList,
            TomlTypeConverter.CanConvert(type)
        ) switch
        {
            RowKind.Toggle => new ChoiceRow(
                entry,
                nameKey,
                new object[] { false, true },
                new List<string> { OnOff("OFF", "Off"), OnOff("ON", "On") }
            ),
            RowKind.Choice when hasList => ListRow(
                entry,
                nameKey,
                acceptableType.GetProperty("AcceptableValues")?.GetValue(acceptable) as Array
            ),
            RowKind.Choice => EnumRow(entry, nameKey),
            RowKind.Slider => new SliderRow(
                entry,
                nameKey,
                Convert.ToSingle(
                    acceptableType.GetProperty("MinValue")?.GetValue(acceptable),
                    CultureInfo.InvariantCulture
                ),
                Convert.ToSingle(
                    acceptableType.GetProperty("MaxValue")?.GetValue(acceptable),
                    CultureInfo.InvariantCulture
                )
            ),
            RowKind.Text => new TextRow(entry, nameKey),
            _ => null,
        };
        if (row != null)
            rows[entry] = row;
        return row;
    }

    private static Setting EnumRow(ConfigEntryBase entry, string nameKey)
    {
        var values = Enum.GetValues(entry.SettingType);
        var boxed = new object[values.Length];
        var labels = new List<string>(values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            boxed[i] = values.GetValue(i);
            labels.Add(SettingsRows.Prettify(boxed[i].ToString()));
        }
        return new ChoiceRow(entry, nameKey, boxed, labels);
    }

    private static Setting ListRow(ConfigEntryBase entry, string nameKey, Array values)
    {
        if (values == null || values.Length == 0)
            return new TextRow(entry, nameKey);
        var boxed = new object[values.Length];
        var labels = new List<string>(values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            boxed[i] = values.GetValue(i);
            labels.Add(TomlTypeConverter.ConvertToString(boxed[i], entry.SettingType));
        }
        return new ChoiceRow(entry, nameKey, boxed, labels);
    }

    /// <summary>The game's own Off/On strings when it has them, so the tab matches the other tabs.</summary>
    private static string OnOff(string id, string fallback) =>
        LocalizedText.mainTable.ContainsKey(id) ? LocalizedText.GetText(id) : fallback;
}

/// <summary>
/// Injects display strings into the game's localisation table. Setting labels are LocalizedText keys
/// (A/SettingsUICell.cs:30); an unknown key would show as "LOC: key". The table is rebuilt on language
/// change (<c>LocalizedText.LoadMainTable</c>, A/LocalizedText.cs:206), so every key is re-injected then.
/// </summary>
internal static class Loc
{
    private static readonly Dictionary<string, string> keys = new(StringComparer.Ordinal);

    public static string Set(string id, string text)
    {
        id = id.ToUpperInvariant().Replace(' ', '_');
        keys[id] = text;
        Inject(id, text);
        return id;
    }

    public static void Reinject()
    {
        foreach (var pair in keys)
            Inject(pair.Key, pair.Value);
    }

    private static void Inject(string id, string text)
    {
        var count =
            Math.Max(
                LocalizedText.LANGUAGE_COUNT,
                Enum.GetValues(typeof(LocalizedText.Language)).Length
            ) + 1;
        LocalizedText.mainTable[id] = Enumerable.Repeat(text, count).ToList();
    }
}
