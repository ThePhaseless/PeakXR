using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using PeakXR.Core;
using Unity.Mathematics;
using UnityEngine;
using Zorro.Core;
using Zorro.Settings;
using Zorro.Settings.DebugUI;

namespace PeakXR.UI.Settings;

/// <summary>A generated settings row backed by a BepInEx config entry.</summary>
internal interface IConfigRow
{
    /// <summary>The entry changed outside the row (another row, a feature, the file): update the UI.</summary>
    void Refresh();
}

/// <summary>
/// Writes config entries from the settings UI without saving the file on every slider tick; the
/// file is saved once the value has been still for a moment (see <see cref="Tick"/>).
/// </summary>
internal static class ConfigWriter
{
    private const float SaveDelay = 0.5f;
    private static ConfigFile dirty;
    private static float changedAt;

    /// <summary>True while a row is writing, so the change event does not echo back into the UI.</summary>
    public static bool Writing { get; private set; }

    public static void Set(ConfigEntryBase entry, object value)
    {
        var file = entry.ConfigFile;
        var save = file.SaveOnConfigSet;
        file.SaveOnConfigSet = false;
        Writing = true;
        try
        {
            entry.BoxedValue = value;
        }
        finally
        {
            Writing = false;
            file.SaveOnConfigSet = save;
        }
        dirty = file;
        changedAt = Time.unscaledTime;
    }

    public static void Tick(bool force)
    {
        if (dirty == null || (!force && Time.unscaledTime - changedAt < SaveDelay))
            return;
        dirty.Save();
        dirty = null;
    }
}

/// <summary>bool, enum or a list of acceptable values: the game's dropdown cell.</summary>
internal sealed class ChoiceRow
    : Setting,
        IEnumSetting,
        ICustomLocalizedEnumSetting,
        IExposedSetting,
        IConfigRow
{
    private readonly ConfigEntryBase entry;
    private readonly object[] values;
    private readonly List<string> labels;
    private readonly string nameKey;

    public ChoiceRow(ConfigEntryBase entry, string nameKey, object[] values, List<string> labels)
    {
        this.entry = entry;
        this.nameKey = nameKey;
        this.values = values;
        this.labels = labels;
    }

    public override void Load(ISettingsSaveLoad loader) { }

    public override void Save(ISettingsSaveLoad saver) { }

    public override void ApplyValue() { }

    public override SettingUI GetDebugUI(ISettingHandler settingHandler) => null;

    public override GameObject GetSettingUICell() =>
        SingletonAsset<InputCellMapper>.Instance.EnumSettingCell;

    public List<string> GetUnlocalizedChoices() => labels;

    public List<string> GetCustomLocalizedChoices() => labels;

    public void RegisterCustomLocalized(Action action) { }

    public void DeregisterCustomLocalized(Action action) { }

    public int GetValue()
    {
        var current = entry.BoxedValue;
        for (int i = 0; i < values.Length; i++)
            if (Equals(values[i], current))
                return i;
        return 0;
    }

    public void SetValue(int v, ISettingHandler settingHandler, bool fromUI)
    {
        if (v < 0 || v >= values.Length)
            return;
        ConfigWriter.Set(entry, values[v]);
        if (!fromUI)
            OnSettingChangedExternal();
        OnSettingChanged();
    }

    public string GetDisplayName() => nameKey;

    public string GetCategory() => VRTab.CategoryName;

    public void Refresh() => OnSettingChangedExternal();
}

/// <summary>Number with an acceptable range: the game's slider cell. Integers snap to whole values.</summary>
internal sealed class SliderRow : FloatSetting, IExposedSetting, IConfigRow
{
    private readonly ConfigEntryBase entry;
    private readonly string nameKey;
    private readonly bool integral;
    private readonly float min,
        max;

    public SliderRow(ConfigEntryBase entry, string nameKey, float min, float max)
    {
        this.entry = entry;
        this.nameKey = nameKey;
        this.min = min;
        this.max = max;
        integral = SettingsRows.IsIntegral(entry.SettingType);
        MinValue = min;
        MaxValue = max;
        Value = Read();
    }

    private float Read() => Convert.ToSingle(entry.BoxedValue, CultureInfo.InvariantCulture);

    public override void Load(ISettingsSaveLoad loader) => Value = Read();

    public override void Save(ISettingsSaveLoad saver) { }

    public override void ApplyValue()
    {
        double value = integral ? Math.Round(Value) : Value;
        ConfigWriter.Set(
            entry,
            Convert.ChangeType(value, entry.SettingType, CultureInfo.InvariantCulture)
        );
    }

    protected override float GetDefaultValue() =>
        Convert.ToSingle(entry.DefaultValue, CultureInfo.InvariantCulture);

    protected override float2 GetMinMaxValue() => new(min, max);

    public override float Clamp(float value)
    {
        value = Mathf.Clamp(value, min, max);
        return integral ? Mathf.Round(value) : value;
    }

    public override string Expose(float result) => SettingsRows.Format(result, integral, min, max);

    public string GetDisplayName() => nameKey;

    public string GetCategory() => VRTab.CategoryName;

    public void Refresh()
    {
        Value = Read();
        OnSettingChangedExternal();
    }
}

/// <summary>Anything else the config can parse from text: the game's text field cell.</summary>
internal sealed class TextRow : StringSetting, IExposedSetting, IConfigRow
{
    private readonly ConfigEntryBase entry;
    private readonly string nameKey;

    public TextRow(ConfigEntryBase entry, string nameKey)
    {
        this.entry = entry;
        this.nameKey = nameKey;
        Value = Read();
    }

    private string Read() => TomlTypeConverter.ConvertToString(entry.BoxedValue, entry.SettingType);

    public override void Load(ISettingsSaveLoad loader) => Value = Read();

    public override void Save(ISettingsSaveLoad saver) { }

    /// <summary>Applied per keystroke; text that does not parse yet is ignored until it does.</summary>
    public override void ApplyValue()
    {
        object parsed;
        try
        {
            parsed = TomlTypeConverter.ConvertToValue(Value, entry.SettingType);
        }
        catch (FormatException)
        {
            return;
        }
        catch (ArgumentException)
        {
            return;
        }
        ConfigWriter.Set(entry, parsed);
    }

    protected override string GetDefaultValue() =>
        TomlTypeConverter.ConvertToString(entry.DefaultValue, entry.SettingType);

    public string GetDisplayName() => nameKey;

    public string GetCategory() => VRTab.CategoryName;

    public void Refresh() => Value = Read();
}

/// <summary>Read-only row: a section header or a compatibility line.</summary>
internal sealed class InfoRow : Setting, IExposedSetting
{
    public readonly string NameKey;
    public readonly string Text;
    public readonly Color Color;
    public readonly bool IsHeader;

    public InfoRow(string nameKey, string text, Color color, bool isHeader)
    {
        NameKey = nameKey;
        Text = text;
        Color = color;
        IsHeader = isHeader;
    }

    public override void Load(ISettingsSaveLoad loader) { }

    public override void Save(ISettingsSaveLoad saver) { }

    public override void ApplyValue() { }

    public override SettingUI GetDebugUI(ISettingHandler settingHandler) => null;

    public override GameObject GetSettingUICell() => InfoCell.Template;

    public string GetDisplayName() => NameKey;

    public string GetCategory() => VRTab.CategoryName;
}
