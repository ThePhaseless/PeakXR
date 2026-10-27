using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.VR;
using UnityEngine;
using Zorro.Settings;

namespace PeakXR.UI.Settings;

/// <summary>
/// Content of the "VR" settings tab, generated from <see cref="Plugin.Settings"/> every time the tab is
/// shown, so a feature only binds config entries and never touches UI code. Rows are grouped by config
/// section and end with a read-only compatibility report: the active runtime, then every feature with
/// failed ones first.
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

    // The game's own text colours (InfoCell): white for working, its warning red for failed, half alpha for off.
    private static readonly Color Active = InfoCell.Normal;
    private static readonly Color Failed = InfoCell.Warning;
    private static readonly Color Muted = InfoCell.Muted;

    private static readonly Dictionary<ConfigEntryBase, Setting> rows = new();

    private sealed class Button
    {
        public string Key,
            Title,
            Text;
        public Action<Vector3, bool> Run;
        public Guide.GuideRow Row;
    }

    private static readonly Dictionary<ConfigEntryBase, Button> buttons = new();

    /// <summary>A button row right after <paramref name="entry"/>'s row (a reset beside its switch). The action gets
    /// the row's position and whether the left hand clicked, for its cue.</summary>
    public static void AddButtonAfter(
        ConfigEntryBase entry,
        string key,
        string title,
        string text,
        Action<Vector3, bool> run
    ) =>
        buttons[entry] = new Button
        {
            Key = key,
            Title = title,
            Text = text,
            Run = run,
        };

    /// <summary>Adds <paramref name="entry"/>'s row and the button that follows it, if any.</summary>
    private static void AddRows(List<Setting> list, ConfigEntryBase entry)
    {
        if (RowFor(entry) is not { } row)
            return;
        list.Add(row);
        if (!buttons.TryGetValue(entry, out var b))
            return;
        // Built on first show: the localisation table is only ready once the game runs.
        b.Row ??= new Guide.GuideRow(
            Guide.GuideAction.Run,
            GuideTopic.Count,
            Loc.Set(b.Key, b.Title),
            b.Text
        )
        {
            Run = b.Run,
        };
        list.Add(b.Row);
    }

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

    /// <summary>Every visible entry by display section (<see cref="SettingsGrid.DisplaySection"/>), in the order they
    /// were bound.</summary>
    private static List<KeyValuePair<string, List<ConfigEntryBase>>> Sections()
    {
        var sections = new List<KeyValuePair<string, List<ConfigEntryBase>>>();
        var bySection = new Dictionary<string, List<ConfigEntryBase>>(StringComparer.Ordinal);
        foreach (var pair in Plugin.Settings)
        {
            var entry = pair.Value;
            if (IsHidden(entry))
                continue;
            // Display grouping only (all vibration under Haptics, item gestures under Items); keys stay where saved.
            var section = SettingsGrid.DisplaySection(
                entry.Definition.Section,
                entry.Definition.Key
            );
            if (!bySection.TryGetValue(section, out var list))
            {
                bySection[section] = list = new List<ConfigEntryBase>();
                sections.Add(new KeyValuePair<string, List<ConfigEntryBase>>(section, list));
            }
            list.Add(entry);
        }
        return sections;
    }

    public static List<Setting> BuildRows()
    {
        if (!Refilling)
            Page = 0;
        Refilling = false;
        Loc.Set(TabKey, "VR");
        if (Guide.GuideRows.ComfortFirst && Guide.GuideRows.Shown)
            return Guide.GuideRows.Comfort(RowFor);

        var result = new List<Setting>();
        Guide.GuideRows.Append(result);
        foreach (var section in Sections())
        {
            result.Add(Header("PEAKXR_SECTION_" + section.Key, section.Key));
            foreach (var entry in section.Value)
                AddRows(result, entry);
        }

        result.Add(Header("PEAKXR_SECTION_COMPAT", "Compatibility"));
        AppendCompat(result);
        return OnePage(result);
    }

    /// <summary>Fills the VR tab again when it is on show, so starting or stopping VR with it open swaps the headset
    /// grid and the flat paged column (ui.world calls this once the menu is placed in, or put back from, the world).</summary>
    public static void RelayIfShown()
    {
        var menu = Guide.GuideRows.Menu;
        if (menu == null || !menu.isActiveAndEnabled)
            return;
        var tabs = UISettingsFeature.TabsRef(menu);
        if (tabs != null && tabs.selectedButton != null && tabs.selectedButton.category == Category)
            menu.ShowSettings(Category);
    }

    /// <summary>Rail key of the VR Guide page.</summary>
    public const string GuideGroup = "PEAKXR_GROUP_GUIDE";

    /// <summary>Rail key of the compatibility report page.</summary>
    public const string CompatGroup = "PEAKXR_GROUP_COMPAT";

    /// <summary>The rail entry on show in VR; kept for the session, so the menu reopens where you left it.</summary>
    public static string Group;

    /// <summary>
    /// The VR layout's rail entries: the VR Guide, one per config group (the section up to its first dot, so
    /// <c>UI.HUD</c> joins <c>UI</c>, and a feature's new section joins or adds an entry by itself), then the
    /// compatibility report. Inside a group with several sections each section starts with its own header.
    /// </summary>
    public static List<VRGroup> BuildGroups()
    {
        if (!Refilling)
            Page = 0;
        Refilling = false;
        Loc.Set(TabKey, "VR");
        var groups = new List<VRGroup>();
        if (Guide.GuideRows.Shown)
        {
            var guide = new VRGroup(GuideGroup, "VR Guide");
            Guide.GuideRows.AppendAll(guide.Rows);
            groups.Add(guide);
        }

        var byGroup = new Dictionary<string, VRGroup>(StringComparer.Ordinal);
        var sectionCount = new Dictionary<VRGroup, int>();
        var sections = Sections();
        foreach (var section in sections)
        {
            var name = SettingsGrid.GroupOf(section.Key);
            if (!byGroup.TryGetValue(name, out var group))
            {
                byGroup[name] = group = new VRGroup(
                    "PEAKXR_GROUP_" + name,
                    SettingsRows.Prettify(name)
                );
                groups.Add(group);
                sectionCount[group] = 0;
            }
            sectionCount[group]++;
        }
        foreach (var section in sections)
        {
            var group = byGroup[SettingsGrid.GroupOf(section.Key)];
            if (sectionCount[group] > 1)
            {
                var sub = SettingsGrid.SubsectionOf(section.Key);
                group.Rows.Add(
                    Header(
                        "PEAKXR_SUBSECTION_" + section.Key,
                        sub.Length == 0 ? group.Title : SettingsRows.Prettify(sub.Replace('.', ' '))
                    )
                );
            }
            foreach (var entry in section.Value)
                AddRows(group.Rows, entry);
        }
        groups.RemoveAll(g => !HasSetting(g.Rows));

        var compat = new VRGroup(CompatGroup, "Compatibility");
        int failed = AppendCompat(compat.Rows);
        if (failed > 0)
            compat.Title = $"Compat: {failed} failed";
        groups.Add(compat);
        return groups;
    }

    private static bool HasSetting(List<Setting> rows)
    {
        foreach (var row in rows)
            if (row is not InfoRow { IsHeader: true })
                return true;
        return false;
    }

    /// <summary>The runtime, then every feature with failed ones first; returns how many failed.</summary>
    private static int AppendCompat(List<Setting> result)
    {
        result.Add(
            new InfoRow(
                Loc.Set("PEAKXR_RUNTIME", "Runtime"),
                RuntimeText(),
                VRSession.Active ? Active : Muted,
                false
            )
        );
        int failed = 0;
        // Failed features first, so a broken install is visible without scrolling.
        foreach (
            var feature in FeatureManager
                .Features.OrderBy(f => f.State == FeatureState.Failed ? 0 : 1)
                .ThenBy(f => f.Order)
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
            if (feature.State == FeatureState.Failed)
                failed++;
            result.Add(
                new InfoRow(Loc.Set("PEAKXR_FEATURE_" + feature.Id, feature.Id), text, color, false)
            );
        }
        return failed;
    }

    /// <summary>Rows per page of the single column (flat play, or <see cref="VRTabGrid"/> could not lay the tab out):
    /// the settings window has no scroll view (SettingsPageShared/Content/Parent is a bare VerticalLayoutGroup), and on a world panel the rows past the screen's bottom edge hang below
    /// it into the scene instead of being cut off. Measured from the window when it fills.</summary>
    public static int PageRows = 7;

    /// <summary>The VR tab page on show; kept while the tab is refilled (<see cref="Refilling"/>), clamped when the
    /// list shrinks, and back to the first page whenever the tab is opened.</summary>
    public static int Page;

    /// <summary>Set by a refill of the open tab (page buttons, demo switch), so the page is kept.</summary>
    public static bool Refilling;

    /// <summary>The current page, its section header repeated when it starts mid-section, then the page buttons.</summary>
    private static List<Setting> OnePage(List<Setting> all)
    {
        int pages = SettingsRows.PageCount(all.Count, PageRows);
        if (pages == 1)
            return all;
        Page = SettingsRows.ClampPage(Page, all.Count, PageRows);
        SettingsRows.PageSlice(all.Count, PageRows, Page, out int start, out int count);
        var page = new List<Setting>(count + 3);
        if (all[start] is not InfoRow { IsHeader: true })
            for (int i = start - 1; i >= 0; i--)
                if (all[i] is InfoRow { IsHeader: true } header)
                {
                    page.Add(header);
                    break;
                }
        page.AddRange(all.GetRange(start, count));
        // The demo switch stays in reach on every page while the demos are listed, so they can be folded anywhere.
        if (Guide.GuideRows.Expanded)
            for (int i = 0; i < all.Count; i++)
                if (
                    all[i] is Guide.GuideRow { Action: Guide.GuideAction.Expand } expand
                    && (i < start || i >= start + count)
                )
                    page.Add(expand);
        // The page count rides on the last button row, so every page says where it is.
        bool last = Page == pages - 1;
        var where = Loc.Set("PEAKXR_PAGE_OF", $"Page {Page + 1} of {pages}");
        if (Page > 0)
            page.Add(
                new Guide.GuideRow(
                    Guide.GuideAction.PagePrevious,
                    GuideTopic.Count,
                    last ? where : Loc.Set("PEAKXR_PAGE_PREVIOUS", "Previous page"),
                    "Back"
                )
            );
        if (!last)
            page.Add(
                new Guide.GuideRow(Guide.GuideAction.PageNext, GuideTopic.Count, where, "Next")
            );
        return page;
    }

    /// <summary>Requested runtime, whether it is rendering, and the XR device name when there is one.</summary>
    private static string RuntimeText()
    {
        var requested = VRSession.Requested;
        if (requested == VRRuntime.Disabled)
            return "Flat (VR disabled)";
        var device = UnityEngine.XR.XRSettings.loadedDeviceName;
        var state = VRSession.Active ? "rendering" : "not rendering";
        return string.IsNullOrEmpty(device)
            ? $"{requested}, {state}"
            : $"{requested} ({device}), {state}";
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

    internal static Setting RowFor(ConfigEntryBase entry)
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

/// <summary>One rail entry of the VR layout and its rows (section headers included).</summary>
internal sealed class VRGroup
{
    public readonly string Key;
    public string Title;
    public readonly List<Setting> Rows = new();

    public VRGroup(string key, string title)
    {
        Key = key;
        Title = title;
    }
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
