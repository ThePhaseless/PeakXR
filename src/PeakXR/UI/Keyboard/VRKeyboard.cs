using System;
using PeakXR.Core;
using PeakXR.UI.Common;
using PeakXR.UI.World;
using PeakXR.VR;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PeakXR.UI.Keyboard;

/// <summary>Marks the keyboard canvas so <see cref="CanvasRules"/> hands it to <see cref="KeyboardPlacement"/>.</summary>
internal sealed class VRKeyboardPanel : MonoBehaviour { }

/// <summary>Where the keyboard stands: set by <see cref="VRKeyboard"/> when it opens, then left in the world.</summary>
internal sealed class KeyboardPlacement : ICanvasPlacement
{
    public static readonly KeyboardPlacement Instance = new();

    public bool Interactive => true;

    public PlacementResult Place(
        Canvas canvas,
        out Vector3 position,
        out Quaternion rotation,
        out float widthMeters
    )
    {
        position = VRKeyboard.PanelPosition;
        rotation = VRKeyboard.PanelRotation;
        widthMeters = UIKeyboardFeature.Width.Value;
        return VRKeyboard.HasPose ? PlacementResult.Placed : PlacementResult.Hidden;
    }
}

/// <summary>
/// The in-headset keyboard. It watches the EventSystem selection: when a <see cref="TMP_InputField"/> gets
/// focus in VR, a world-space key panel opens in front of the player and types into that field through the
/// field's own key handling (<see cref="TMP_InputField.ProcessEvent"/>), so character limits, validation and
/// change events stay the game's. Keys are pressed with the menu laser (a uGUI canvas that ui.world converts
/// and raycasts) or by poking them with a controller tip. A platform text dialog can open it too
/// (<see cref="TryOpenDialog"/>); the typed text then goes back to the game's callback.
/// </summary>
[DefaultExecutionOrder(31200)]
internal sealed class VRKeyboard : MonoBehaviour
{
    private const float Unit = 70f;
    private const float Gap = 6f;
    private const float Pad = 24f;
    private const float HeaderHeight = 96f;
    private const float PokeTipForward = 0.04f;
    private const float PokeTickSeconds = 0.02f;
    private const float FlashSeconds = 0.12f;
    private const int SortingOrder = 30000;

    private static readonly Color PanelColor = new(0.06f, 0.07f, 0.09f, 0.96f);
    private static readonly Color KeyColor = new(0.2f, 0.22f, 0.27f, 1f);
    private static readonly Color SpecialColor = new(0.14f, 0.16f, 0.2f, 1f);
    private static readonly Color DoneColor = new(0.17f, 0.4f, 0.26f, 1f);
    private static readonly Color ShiftOnColor = new(0.3f, 0.45f, 0.75f, 1f);
    private static readonly Color PressedColor = new(0.55f, 0.7f, 0.95f, 1f);

    private static VRKeyboard instance;

    public static bool HasPose { get; private set; }
    public static Vector3 PanelPosition { get; private set; }
    public static Quaternion PanelRotation { get; private set; } = Quaternion.identity;

    private readonly PokeButton[] pokes = { new(), new() };
    private readonly TextEntry entry = new();
    private readonly Event keyEvent = new() { type = EventType.KeyDown };

    private Canvas canvas;
    private RectTransform canvasRect;
    private Text title;
    private Text preview;
    private KeyDef[] keys;
    private Rect[] keyBounds;
    private Image[] keyImages;
    private Text[] keyLabels;
    private Color[] keyColors;

    private GameObject lastSelected;
    private TMP_InputField candidate;
    private bool wasFocused;
    private bool open;
    private bool shift;
    private TMP_InputField field;
    private Action<string, bool> dialogDone;
    private string shown;
    private int flashKey = -1;
    private float flashUntil;

    public static void Create()
    {
        var go = new GameObject("PeakXR_KeyboardManager");
        DontDestroyOnLoad(go);
        go.AddComponent<VRKeyboard>();
    }

    /// <summary>
    /// Shows the keyboard for a platform text dialog that has no input field behind it. The result goes to
    /// <paramref name="done"/> (text, submitted). False when the keyboard cannot be shown, so the game's own
    /// dialog should run.
    /// </summary>
    public static bool TryOpenDialog(string heading, int limit, Action<string, bool> done)
    {
        if (instance == null || !UIWorldRuntime.Ready || VRRig.Instance == null)
            return false;
        instance.CloseDialogOnly();
        instance.entry.Clear();
        instance.entry.Limit = limit;
        instance.dialogDone = done;
        instance.Open(null, heading);
        return true;
    }

    private void Awake()
    {
        instance = this;
        Build();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
        if (canvas != null)
            Destroy(canvas.gameObject);
    }

    private void Build()
    {
        var def = KeyboardLayout.Rows;
        float widest = 0f;
        foreach (var row in def)
        {
            float w = 0f;
            foreach (var k in row)
                w += k.Width;
            widest = Mathf.Max(widest, w);
        }
        var size = new Vector2(
            widest * Unit + Pad * 2f,
            def.Length * Unit + HeaderHeight + Pad * 2f
        );

        var go = new GameObject(
            "PeakXR_Keyboard",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(VRKeyboardPanel)
        );
        DontDestroyOnLoad(go);
        canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above the menu it types into: GraphicRaycaster results sort by canvas order before distance.
        canvas.sortingOrder = SortingOrder;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = size;
        canvasRect = (RectTransform)go.transform;

        var font = LoadFont();
        Panel(canvasRect, "Background", Vector2.zero, size, PanelColor);
        title = Label(
            canvasRect,
            "Title",
            font,
            26,
            TextAnchor.UpperLeft,
            new Color(1f, 1f, 1f, 0.6f)
        );
        Place(
            title.rectTransform,
            new Vector2(0f, size.y * 0.5f - Pad - 18f),
            new Vector2(size.x - Pad * 2f, 36f)
        );
        preview = Label(canvasRect, "Preview", font, 34, TextAnchor.MiddleLeft, Color.white);
        Place(
            preview.rectTransform,
            new Vector2(0f, size.y * 0.5f - Pad - 62f),
            new Vector2(size.x - Pad * 2f, 48f)
        );

        var count = 0;
        foreach (var row in def)
            count += row.Length;
        keys = new KeyDef[count];
        keyBounds = new Rect[count];
        keyImages = new Image[count];
        keyLabels = new Text[count];
        keyColors = new Color[count];

        int index = 0;
        float top = size.y * 0.5f - Pad - HeaderHeight;
        for (int r = 0; r < def.Length; r++)
        {
            float rowWidth = 0f;
            foreach (var k in def[r])
                rowWidth += k.Width;
            float x = -rowWidth * Unit * 0.5f;
            float y = top - Unit * (r + 0.5f);
            foreach (var key in def[r])
            {
                var w = key.Width * Unit;
                var center = new Vector2(x + w * 0.5f, y);
                var keySize = new Vector2(w - Gap, Unit - Gap);
                int i = index++;
                keys[i] = key;
                keyBounds[i] = new Rect(center - keySize * 0.5f, keySize);
                keyColors[i] =
                    key.Kind == KeyKind.Character || key.Kind == KeyKind.Space ? KeyColor
                    : key.Kind == KeyKind.Done ? DoneColor
                    : SpecialColor;

                var image = Panel(canvasRect, "Key", center, keySize, keyColors[i]);
                var button = image.gameObject.AddComponent<Button>();
                button.targetGraphic = image;
                // No navigation, so pressing a key never moves the EventSystem selection off the input field.
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                var colors = button.colors;
                colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
                colors.pressedColor = new Color(1.6f, 1.6f, 1.8f, 1f);
                button.colors = colors;
                button.onClick.AddListener(() => Press(i));

                var label = Label(
                    image.rectTransform,
                    "Label",
                    font,
                    30,
                    TextAnchor.MiddleCenter,
                    Color.white
                );
                label.raycastTarget = false;
                Place(label.rectTransform, Vector2.zero, keySize);
                keyImages[i] = image;
                keyLabels[i] = label;
                x += w;
            }
        }

        RefreshLabels();
        LaserPointer.KeepSelectionRoot = canvasRect;
        CanvasRules.AddCustom(typeof(VRKeyboardPanel).FullName, KeyboardPlacement.Instance);
        go.SetActive(false);
        WorldCanvases.Track(canvas);
    }

    private static Font LoadFont()
    {
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return font != null ? font : Font.CreateDynamicFontFromOSFont("Arial", 32);
    }

    private static Image Panel(
        RectTransform parent,
        string name,
        Vector2 center,
        Vector2 size,
        Color color
    )
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        Place(rect, center, size);
        var image = go.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static Text Label(
        RectTransform parent,
        string name,
        Font font,
        int fontSize,
        TextAnchor anchor,
        Color color
    )
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.alignment = anchor;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private static void Place(RectTransform rect, Vector2 center, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = center;
        rect.sizeDelta = size;
    }

    private void Update()
    {
        var rig = VRRig.Instance;
        if (!VRSession.Active || !UIWorldRuntime.Ready || rig == null || rig.Head == null)
        {
            if (open)
                Close(submitted: false);
            return;
        }

        if (dialogDone == null)
            WatchFocus();
        if (!open)
            return;

        if (title != null && flashKey >= 0 && Time.unscaledTime >= flashUntil)
        {
            keyImages[flashKey].color = KeyColorFor(flashKey);
            flashKey = -1;
        }
        RefreshPreview();
        if (UIKeyboardFeature.Poke.Value)
        {
            Poke(rig, true);
            Poke(rig, false);
        }
    }

    /// <summary>Opens when an input field gains focus, closes when it loses it. The selection is looked up
    /// only when it changes.</summary>
    private void WatchFocus()
    {
        var system = EventSystem.current;
        var selected = system != null ? system.currentSelectedGameObject : null;
        if (selected != lastSelected)
        {
            lastSelected = selected;
            candidate = selected != null ? selected.GetComponent<TMP_InputField>() : null;
        }

        var focused =
            candidate != null
            && candidate.isFocused
            && candidate.interactable
            && !candidate.readOnly
            && candidate.isActiveAndEnabled;
        if (!open)
        {
            if (focused && !wasFocused)
                Open(candidate, FieldTitle(candidate));
        }
        else if (field == null || !field.isActiveAndEnabled || !field.isFocused)
            Close(submitted: false);
        wasFocused = focused;
    }

    private static string FieldTitle(TMP_InputField input)
    {
        var placeholder =
            input.placeholder != null ? input.placeholder.GetComponent<TMP_Text>() : null;
        return placeholder != null ? placeholder.text : string.Empty;
    }

    private void Open(TMP_InputField input, string heading)
    {
        var rig = VRRig.Instance;
        var head = rig.Head;
        var yaw = PanelMath.YawOf(head.forward.ToNumerics(), head.up.ToNumerics());
        var offset = PanelMath
            .InFront(yaw, UIKeyboardFeature.Distance.Value, UIKeyboardFeature.Tilt.Value)
            .ToUnity();
        PanelPosition = head.position + offset;
        PanelRotation = Quaternion.LookRotation(offset, Vector3.up);
        HasPose = true;

        field = input;
        shift = false;
        shown = null;
        open = true;
        title.text = heading ?? string.Empty;
        RefreshLabels();
        pokes[0].Reset();
        pokes[1].Reset();
        canvas.gameObject.SetActive(true);
        RefreshPreview();
    }

    private void Close(bool submitted)
    {
        if (!open)
            return;
        open = false;
        canvas.gameObject.SetActive(false);
        var done = dialogDone;
        var text = entry.Text;
        dialogDone = null;
        field = null;
        done?.Invoke(text, submitted);
    }

    /// <summary>A second dialog replaces the first, which the game then hears as aborted.</summary>
    private void CloseDialogOnly()
    {
        if (open && dialogDone != null)
            Close(submitted: false);
    }

    private void Press(int index)
    {
        if (!open)
            return;
        var key = keys[index];
        switch (key.Kind)
        {
            case KeyKind.Character:
                Insert(key.Output(shift));
                if (shift)
                {
                    shift = false;
                    RefreshLabels();
                }
                break;
            case KeyKind.Space:
                Insert(' ');
                break;
            case KeyKind.Backspace:
                Erase();
                break;
            case KeyKind.Shift:
                shift = !shift;
                RefreshLabels();
                break;
            case KeyKind.Done:
                Finish();
                break;
            case KeyKind.Cancel:
                Dismiss();
                break;
        }
        Flash(index);
    }

    private void Insert(char c)
    {
        if (field == null)
        {
            entry.Insert(c);
            return;
        }
        keyEvent.keyCode = KeyCode.None;
        keyEvent.character = c;
        field.ProcessEvent(keyEvent);
        field.ForceLabelUpdate();
    }

    private void Erase()
    {
        if (field == null)
        {
            entry.Backspace();
            return;
        }
        keyEvent.keyCode = KeyCode.Backspace;
        keyEvent.character = '\0';
        field.ProcessEvent(keyEvent);
        field.ForceLabelUpdate();
    }

    /// <summary>Done: what Enter does in the field (submit, then leave it), or the dialog's answer.</summary>
    private void Finish()
    {
        var input = field;
        if (input != null)
        {
            input.onSubmit?.Invoke(input.text);
            input.DeactivateInputField();
        }
        Close(submitted: true);
    }

    /// <summary>Close: leave the field, keeping what was typed, or abort the dialog.</summary>
    private void Dismiss()
    {
        field?.DeactivateInputField();
        Close(submitted: false);
    }

    private void Poke(VRRig rig, bool left)
    {
        var aim = rig.Aim(left);
        var poke = pokes[left ? 0 : 1];
        if (aim == null || !VRInput.Hand(left).Tracked)
        {
            poke.Reset();
            return;
        }
        var local = canvasRect.InverseTransformPoint(aim.position + aim.forward * PokeTipForward);
        var scale = canvasRect.lossyScale.x;
        int hit = -1;
        for (int i = 0; i < keyBounds.Length; i++)
            if (keyBounds[i].Contains(new Vector2(local.x, local.y)))
            {
                hit = i;
                break;
            }
        // The panel faces away from the player, so "in front" is toward -Z.
        if (poke.Update(-local.z * scale, hit >= 0) && hit >= 0)
        {
            Press(hit);
            UIHaptics.Tick(left, UIKeyboardFeature.Haptics.Value, PokeTickSeconds);
        }
    }

    private void Flash(int index)
    {
        if (flashKey >= 0 && flashKey != index)
            keyImages[flashKey].color = KeyColorFor(flashKey);
        flashKey = index;
        flashUntil = Time.unscaledTime + FlashSeconds;
        keyImages[index].color = PressedColor;
    }

    private Color KeyColorFor(int index) =>
        keys[index].Kind == KeyKind.Shift && shift ? ShiftOnColor : keyColors[index];

    private void RefreshLabels()
    {
        for (int i = 0; i < keys.Length; i++)
        {
            var label = keys[i].Label(shift);
            if (keyLabels[i].text != label)
                keyLabels[i].text = label;
            if (i != flashKey)
                keyImages[i].color = KeyColorFor(i);
        }
    }

    private void RefreshPreview()
    {
        var text = field != null ? field.text : entry.Text;
        if (ReferenceEquals(text, shown))
            return;
        shown = text;
        preview.text =
            field != null && field.inputType == TMP_InputField.InputType.Password
                ? new string('*', text.Length)
                : text;
    }
}
