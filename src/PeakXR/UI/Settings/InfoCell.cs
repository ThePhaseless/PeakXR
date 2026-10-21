using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zorro.Settings;

namespace PeakXR.UI.Settings;

/// <summary>
/// Value cell for read-only rows. Styled from the row's own label (font, size), so it matches the
/// game's settings look without shipping assets. Headers restyle the label instead of adding text.
/// </summary>
internal sealed class InfoCell : SettingInputUICell
{
    private static readonly Color HeaderColor = new(0.95f, 0.78f, 0.36f, 1f);
    private static GameObject template;

    /// <summary>Inactive prefab the game's SettingsUICell instantiates for <see cref="InfoRow"/>.</summary>
    public static GameObject Template
    {
        get
        {
            if (template == null)
            {
                template = new GameObject("PeakXR_InfoCell", typeof(RectTransform));
                template.SetActive(false);
                template.AddComponent<InfoCell>();
                var layout = template.AddComponent<LayoutElement>();
                layout.flexibleWidth = 1f;
                layout.minHeight = 30f;
                DontDestroyOnLoad(template);
            }
            return template;
        }
    }

    public override void Setup(Setting setting, ISettingHandler settingHandler)
    {
        gameObject.SetActive(true);
        string value;
        var color = Color.white;
        var header = false;
        if (setting is InfoRow row)
        {
            value = row.Text;
            color = row.Color;
            header = row.IsHeader;
        }
        else if (setting is TextRow textRow)
            value = textRow.Value;
        else if (setting is Guide.GuideRow guideRow)
            value = guideRow.GetButtonText();
        else
            return;

        var cell = GetComponentInParent<SettingsUICell>(true);
        var label = cell != null ? cell.m_text : null;
        if (header && label != null)
        {
            label.fontStyle |= FontStyles.Bold;
            label.color = HeaderColor;
        }
        if (string.IsNullOrEmpty(value))
            return;

        var go = new GameObject("Value", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;

        var text = go.AddComponent<TextMeshProUGUI>();
        if (label != null)
        {
            text.font = label.font;
            text.fontSize = label.fontSize * 0.8f;
        }
        text.text = value;
        text.color = color;
        text.alignment = TextAlignmentOptions.MidlineRight;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
    }
}
