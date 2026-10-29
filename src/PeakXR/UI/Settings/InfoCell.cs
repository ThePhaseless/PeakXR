using PeakXR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Zorro.Settings;

namespace PeakXR.UI.Settings;

/// <summary>
/// Value cell for the VR tab's rows the game ships no cell for: its <c>InputCellMapper</c> asset assigns only the
/// enum and float cells (Zorro.Settings/InputCellMapper.cs:5; Button, String, Int and KeyCode are null in
/// resources.assets). Everything is taken from what the page already holds, so it looks like the game's own pages
/// and follows their restyles: section headers in the game's section header style (white, 32, no row strip, as the
/// settings page's Blocklist headers), read-only lines (the compatibility report, a text value) in the row prefab's
/// own "main menu only" note, and actions as clones of the settings page's Back button
/// (MainMenuSettingsPage.cs:9, PauseMenuSettingsMenuPage.cs:9), so they read as the game's buttons, not values.
/// </summary>
internal sealed class InfoCell : SettingInputUICell
{
    /// <summary>The game's settings text: row labels, headers, tabs.</summary>
    public static readonly Color Normal = Color.white;

    /// <summary>The game's warning line, #FF7575 (the row prefab's OnlyOnMainMenu note).</summary>
    public static readonly Color Warning = new(1f, 0.459f, 0.459f, 1f);

    /// <summary>Not in use: the game's disabled look, its colour blocks' half alpha.</summary>
    public static readonly Color Muted = new(1f, 1f, 1f, 0.5f);

    // The settings page's section headers (Blocklist "Blocked Players") are white upper case at 32 on no strip.
    private const float HeaderSize = 32f;

    // Forward actions wear the pause menu's green button (Invite friends: panel #244B1E, border #0D6F1D); back, stop
    // and previous keep the Back button's own red, as the game's Back and Leave game do.
    private static readonly Color GoPanel = new(0.141f, 0.294f, 0.118f, 1f);
    private static readonly Color GoBorder = new(0.051f, 0.435f, 0.114f, 1f);

    private static GameObject template;

    private RectTransform buttonRect;
    private float buttonWidth;

    /// <summary>Width the action button wants (caption plus padding, at least the game's Back button); 0 without one.
    /// The VR column reads it to widen a narrowed control area (<see cref="SettingsRows.ControlWidth"/>).</summary>
    public float ButtonWidth => buttonRect != null ? buttonWidth : 0f;

    /// <summary>Inactive prefab the game's SettingsUICell instantiates for these rows.</summary>
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
        // Fills the control area like the game's own value cells (a new RectTransform is a 100 px centred square).
        var rect = (RectTransform)transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var cell = GetComponentInParent<SettingsUICell>(true);
        switch (setting)
        {
            case InfoRow { IsHeader: true }:
                Header(cell);
                break;
            case InfoRow row:
                Line(cell, row.Text, row.Color, upper: true);
                break;
            case TextRow textRow:
                Line(cell, textRow.Value, Normal, upper: false);
                break;
            case Guide.GuideRow guideRow:
                if (!Action(guideRow))
                    Line(cell, guideRow.GetButtonText(), Normal, upper: true);
                break;
        }
    }

    /// <summary>A section header: the row's label at the game's header size, without the row strip.</summary>
    private static void Header(SettingsUICell cell)
    {
        if (cell == null)
            return;
        var label = cell.m_text;
        if (label != null)
        {
            label.enableAutoSizing = false;
            label.fontSize = HeaderSize;
        }
        foreach (Transform child in cell.transform)
            if (
                child != cell.m_settingsContentParent
                && child.TryGetComponent<Image>(out var strip)
            )
                strip.enabled = false;
    }

    /// <summary>Read-only text in the row's own note style (the game's font, size and case), else the label's font.</summary>
    private void Line(SettingsUICell cell, string value, Color color, bool upper)
    {
        if (string.IsNullOrEmpty(value))
            return;
        var note =
            cell != null && cell.onlyOnMainMenu != null
                ? cell.onlyOnMainMenu.GetComponentInChildren<TextMeshProUGUI>(true)
                : null;
        TextMeshProUGUI text;
        if (note != null)
        {
            text = Instantiate(note, transform, false);
            text.gameObject.name = "Value";
            Unlocalize(text);
            text.gameObject.SetActive(true);
        }
        else
        {
            var go = new GameObject("Value", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            text = go.AddComponent<TextMeshProUGUI>();
            var label = cell != null ? cell.m_text : null;
            if (label != null)
            {
                text.font = label.font;
                text.fontSize = label.fontSize * 0.8f;
            }
            text.alignment = TextAlignmentOptions.MidlineRight;
        }
        var rt = text.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        FixSize(text);
        if (upper)
            text.fontStyle |= FontStyles.UpperCase;
        else
            text.fontStyle &= ~FontStyles.UpperCase;
        text.margin = Vector4.zero;
        text.text = value;
        text.color = color;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
    }

    /// <summary>
    /// The settings page's Back button, cloned into the control area with the row's action: the game's button
    /// sprite, borders, font, hover animation and sounds. False when the page has none (the caller shows text).
    /// </summary>
    private bool Action(Guide.GuideRow row)
    {
        var source = BackButton();
        if (source == null)
            return false;
        var button = Instantiate(source, transform, false);
        button.gameObject.name = "PeakXR_Action";
        // The page's own listeners (going back) must not come along; sounds wired to the button's children stay.
        for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
        {
            var target = button.onClick.GetPersistentTarget(i);
            var at =
                target is Component c ? c.transform
                : target is GameObject g ? g.transform
                : null;
            if (at != null && !at.IsChildOf(button.transform))
                button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
        }
        button.onClick.AddListener(() => Guide.GuideRows.Click(row));
        button.interactable = true;
        button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
        if (!row.IsBack)
            Recolour(button);

        var caption = row.GetButtonText().ToUpperInvariant();
        var text = button.GetComponentInChildren<TextMeshProUGUI>(true);
        float textWidth = 0f,
            inset = 0f;
        if (text != null)
        {
            Unlocalize(text);
            FixSize(text);
            text.text = caption;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            textWidth = text.GetPreferredValues(caption).x;
            inset = Mathf.Max(0f, -text.rectTransform.sizeDelta.x);
        }

        var rt = (RectTransform)button.transform;
        float height = rt.rect.height;
        buttonRect = rt;
        buttonWidth = Mathf.Max(textWidth + inset, rt.rect.width);
        FitButton();
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.sizeDelta = new Vector2(0f, height);
        rt.anchoredPosition = Vector2.zero;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        button.gameObject.SetActive(true);
        return true;
    }

    /// <summary>Sizes the action button for the control area as it is now. Again after the VR column narrows the row:
    /// an anchor measured against the game's wide area shrank with it and cut the caption ("ENTE~").</summary>
    public void FitButton()
    {
        if (buttonRect == null)
            return;
        float area = ((RectTransform)transform).rect.width;
        buttonRect.anchorMin = new Vector2(
            SettingsRows.ButtonLeftAnchor(buttonWidth, 0f, 0f, area),
            0.5f
        );
    }

    /// <summary>The Back button of the settings page this row is on (title or pause).</summary>
    private Button BackButton()
    {
        var title = GetComponentInParent<MainMenuSettingsPage>(true);
        if (title != null && title.backButton != null)
            return title.backButton;
        var pause = GetComponentInParent<PauseMenuSettingsMenuPage>(true);
        return pause != null ? pause.backButton : null;
    }

    /// <summary>Panel (the button's target graphic) and dotted borders (its tiled images) in the green button's colours.</summary>
    private static void Recolour(Button button)
    {
        if (button.targetGraphic != null)
            button.targetGraphic.color = Tint(button.targetGraphic.color, GoPanel);
        foreach (var image in button.GetComponentsInChildren<Image>(true))
            if (image.type == Image.Type.Tiled)
                image.color = Tint(image.color, GoBorder);
    }

    private static Color Tint(Color old, Color rgb) => new(rgb.r, rgb.g, rgb.b, old.a);

    /// <summary>The game's localisation would put its own string back on a cloned text.</summary>
    private static void Unlocalize(TMP_Text text)
    {
        foreach (var localized in text.GetComponents<LocalizedText>())
            DestroyImmediate(localized);
    }

    /// <summary>At the size the prefab was authored for: the VR column is scaled for its smallest text, so it must
    /// not shrink after that.</summary>
    private static void FixSize(TMP_Text text)
    {
        if (!text.enableAutoSizing)
            return;
        text.fontSize = Mathf.Max(text.fontSize, text.fontSizeMax);
        text.enableAutoSizing = false;
    }
}
