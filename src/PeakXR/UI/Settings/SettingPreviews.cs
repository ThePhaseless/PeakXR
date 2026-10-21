using System.Collections.Generic;
using BepInEx.Configuration;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Inventory;
using PeakXR.Locomotion;
using PeakXR.UI.Common;
using PeakXR.UI.Status;
using PeakXR.UI.World;
using PeakXR.VR;
using PeakXR.XR;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PeakXR.UI.Settings;

/// <summary>
/// Preview registry for the VR settings tab: which row is on screen for which entry, and what each entry
/// previews (<see cref="SettingPreviewTable"/>, by section and key). Feature code is never touched; values are
/// read back from <see cref="Plugin.Settings"/>. VR only: in flat play the rows behave as before.
/// </summary>
internal static class SettingPreviews
{
    private static readonly Dictionary<ConfigEntryBase, PreviewKind> kinds = new();
    private static readonly Dictionary<ConfigEntryBase, SettingsUICell> cells = new();

    /// <summary>A row was spawned for <paramref name="entry"/>: remember it and listen for the laser.</summary>
    public static void Track(ConfigEntryBase entry, SettingsUICell cell)
    {
        cells[entry] = cell;
        cell.gameObject.AddComponent<RowHover>().Entry = entry;
    }

    /// <summary>The live row of an entry, or null once the tab was switched or closed.</summary>
    public static SettingsUICell CellOf(ConfigEntryBase entry) =>
        entry != null && cells.TryGetValue(entry, out var cell) && cell != null ? cell : null;

    public static PreviewKind KindOf(ConfigEntryBase entry)
    {
        if (!kinds.TryGetValue(entry, out var kind))
            kinds[entry] = kind = SettingPreviewTable.KindFor(
                entry.Definition.Section,
                entry.Definition.Key
            );
        return kind;
    }

    /// <summary>A row wrote a new value (<see cref="ConfigWriter.Set"/>).</summary>
    public static void Changed(ConfigEntryBase entry)
    {
        if (VRSession.Active && PreviewStage.Instance != null)
            PreviewStage.Instance.Begin(entry, change: true);
    }

    /// <summary>Looks an entry up once and keeps it, so per-frame reads do not allocate.</summary>
    public static T Value<T>(ref ConfigEntry<T> slot, string section, string key, T fallback)
    {
        if (slot == null && !Plugin.Settings.TryGetEntry(section, key, out slot))
            return fallback;
        return slot.Value;
    }
}

/// <summary>Laser hover on a generated row; the stage starts the preview once the laser rests there.</summary>
internal sealed class RowHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public ConfigEntryBase Entry;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (VRSession.Active && PreviewStage.Instance != null)
            PreviewStage.Instance.HoverEnter(Entry);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (PreviewStage.Instance != null)
            PreviewStage.Instance.HoverExit(Entry);
    }
}

/// <summary>
/// Shows what a settings row does for a moment after it is changed (or rested on with the laser): an info card
/// beside the row with its description, value and when it applies, plus a sample of the effect where it lives:
/// the vignette, the edge glow, a turn dial, a walk-direction arrow, the HUD or wrist outline, belt slot spheres,
/// a sample name tag, a controller pulse, a partial fade, or the menu re-placed at its new distance. Previews
/// never move the player, press game input or send anything over the network; everything is built on first use
/// and hidden afterwards.
/// </summary>
internal sealed class PreviewStage : MonoBehaviour
{
    private const float CardWidth = 560f;
    private const float CardHeight = 320f;
    private const float LineWidth = 0.004f;
    private const float SnapStepSeconds = 0.6f;
    private const float PreviewFade = 0.6f;
    private const float ReplaceDelay = 0.35f;
    private const float PulseInterval = 0.2f;
    private const float VignetteRamp = 0.15f;
    private const float HoverVignetteCap = 0.4f;
    private static readonly float CrouchEnter = new PhysicalCrouch().EnterFraction;
    private static readonly Color Accent = new(0.45f, 0.85f, 1f, 1f);
    private static readonly Color StatusSample = new(0.35f, 0.85f, 0.2f, 1f);
    private static readonly Color DyingSample = new(0.85f, 0.1f, 0.1f, 1f);

    public static PreviewStage Instance { get; private set; }

    private readonly PreviewClock clock = new();
    private readonly Vector3[] corners = new Vector3[4];
    private readonly object fadeOwner = new();
    private readonly VignetteMesh vignette = new();
    private readonly Transform[] slots = new Transform[BodyInventory.MaxSlots];

    private ConfigEntryBase entry;
    private ConfigEntryBase hoverEntry;
    private PreviewKind kind;
    private bool visual;
    private bool shown;

    private float fadeUntil = -1f;
    private float lastPulse = -1f;
    private float lastFade = -10f;
    private ConfigEntryBase replaceEntry;
    private Canvas replaceCanvas;
    private float replaceAt = -1f;

    private GameObject card;
    private CanvasGroup cardGroup;
    private RectTransform cardText;
    private TextMeshProUGUI text;
    private GameObject sampleTag;
    private TextMeshProUGUI tagText;
    private Material lineMaterial;
    private Material slotMaterial;
    private LineRenderer ring,
        startNeedle,
        needle,
        outline,
        arrow;
    private PeripheralRim rim;
    private VRRig rimRig;

    private static ConfigEntry<bool> vignetteOn;
    private static ConfigEntry<float> vignetteStrength,
        snapAngle,
        smoothSpeed,
        statusIntensity,
        nameTagScale,
        wristWidth,
        interactStrength;
    private static ConfigEntry<bool> physicalCrouch;
    private static ConfigEntry<string> turnMode,
        moveRelativeTo;
    private static ConfigEntry<StatusOverlayMode> statusMode;
    private static ConfigEntry<WristSide> wristHand;
    private static ConfigEntry<bool> dyingTint;

    private void Awake() => Instance = this;

    private void OnDisable() => HideAll();

    private void OnDestroy()
    {
        vignette.Destroy();
        rim?.Destroy();
        if (lineMaterial != null)
            Destroy(lineMaterial);
        if (slotMaterial != null)
            Destroy(slotMaterial);
        if (Instance == this)
            Instance = null;
    }

    public void HoverEnter(ConfigEntryBase target)
    {
        hoverEntry = target;
        clock.HoverEnter();
    }

    public void HoverExit(ConfigEntryBase target)
    {
        if (hoverEntry != target)
            return;
        hoverEntry = null;
        clock.HoverExit();
    }

    public void Begin(ConfigEntryBase target, bool change)
    {
        var cell = SettingPreviews.CellOf(target);
        if (cell == null)
        {
            if (!change)
                clock.Stop();
            return;
        }
        if (change)
            clock.Change();
        if (entry != target || kind != SettingPreviews.KindOf(target))
            HideVisuals();
        entry = target;
        kind = SettingPreviews.KindOf(target);
        visual = change || SettingPreviewTable.VisualOnHover(kind);
        FillCard(cell);
        if (change)
            OnChange(cell);
    }

    private void Update()
    {
        if (clock.Tick(Time.unscaledDeltaTime) && hoverEntry != null)
            Begin(hoverEntry, change: false);
        var now = Time.unscaledTime;
        // Never mid-drag: wait for both triggers to be up, and skip it if the menu closed meanwhile.
        if (replaceAt >= 0f && now >= replaceAt && !TriggerHeld())
        {
            replaceAt = -1f;
            var row = SettingPreviews.CellOf(replaceEntry);
            if (
                VRSession.Active
                && replaceCanvas != null
                && replaceCanvas.isActiveAndEnabled
                && row != null
                && row.gameObject.activeInHierarchy
            )
                WorldCanvases.Reopen(replaceCanvas);
        }
        if (fadeUntil >= 0f && now >= fadeUntil)
            EndFade();

        var rig = VRRig.Instance;
        var cell = SettingPreviews.CellOf(entry);
        if (
            !clock.Active
            || !VRSession.Active
            || rig == null
            || rig.Head == null
            || cell == null
            || !cell.gameObject.activeInHierarchy
        )
        {
            if (shown)
                HideAll();
            return;
        }
        shown = true;
        var alpha = clock.Alpha;
        PlaceCard((RectTransform)cell.transform, alpha);
        if (visual)
            DrawVisual(rig, alpha);
    }

    // ---- change side effects (once per change, never per frame) ----

    private void OnChange(SettingsUICell cell)
    {
        var now = Time.unscaledTime;
        switch (kind)
        {
            case PreviewKind.Haptic:
                if (now - lastPulse < PulseInterval)
                    break;
                lastPulse = now;
                var amplitude = entry.BoxedValue switch
                {
                    float f => Mathf.Clamp01(f),
                    bool b => b ? PairedStrength() : 0f,
                    _ => 0.5f,
                };
                UIHaptics.Tick(true, amplitude, 0.12f);
                UIHaptics.Tick(false, amplitude, 0.12f);
                break;
            case PreviewKind.Wrist:
                if (now - lastPulse < PulseInterval)
                    break;
                lastPulse = now;
                var left =
                    SettingPreviews.Value(ref wristHand, "UI.HUD", "WristHand", WristSide.Left)
                    == WristSide.Left;
                UIHaptics.Tick(left, 0.3f, 0.08f);
                break;
            case PreviewKind.Fade:
                // A partial dip, not a black flash, at most once per preview so a slider drag does not pump it.
                if (now - lastFade < PreviewClock.ChangeSeconds)
                    break;
                lastFade = now;
                VRFade.Set(fadeOwner, PreviewFade);
                fadeUntil = now + (entry.BoxedValue is float seconds ? seconds : 0.3f) + 0.25f;
                break;
            case PreviewKind.MenuPlacement:
                // Re-placed once the slider rests, so the panel does not jump under the laser while dragging.
                var canvas = cell.GetComponentInParent<Canvas>();
                replaceCanvas = canvas != null ? canvas.rootCanvas : null;
                replaceEntry = entry;
                replaceAt = now + ReplaceDelay;
                break;
        }
    }

    private static bool TriggerHeld() =>
        VRInput.Left.Trigger > 0.2f || VRInput.Right.Trigger > 0.2f;

    /// <summary>A bool haptics switch pulses at the strength slider it goes with, when there is one.</summary>
    private float PairedStrength()
    {
        if (
            Plugin.Settings.TryGetEntry<float>(
                entry.Definition.Section,
                "HapticStrength",
                out var own
            )
        )
            return Mathf.Clamp01(own.Value);
        return Mathf.Clamp01(
            SettingPreviews.Value(ref interactStrength, "Interaction", "HapticStrength", 0.5f)
        );
    }

    private void EndFade()
    {
        fadeUntil = -1f;
        VRFade.Clear(fadeOwner);
    }

    // ---- info card ----

    private void FillCard(SettingsUICell cell)
    {
        if (card == null)
            BuildCard();
        var label = cell.m_text;
        if (label != null && text.font != label.font)
        {
            text.font = label.font;
            tagText.font = label.font;
        }
        // No card behind the text: an outline keeps it readable against the world.
        TextHalo.Apply(text);
        var def = entry.Definition;
        var value = NoParse(TomlTypeConverter.ConvertToString(entry.BoxedValue, entry.SettingType));
        var note = SettingPreviewTable.ApplyNote(def.Section, def.Key);
        text.text =
            $"<b>{SettingsRows.Prettify(def.Key)}</b>   <color=#8fd8ff>{value}</color>\n"
            + $"<size=80%>{NoParse(entry.Description?.Description)}</size>"
            + (note != null ? $"\n<size=80%><color=#ffc880>{note}</color></size>" : "");
        // The turn dial takes the lower part of the card.
        cardText.offsetMin = new Vector2(24f, kind == PreviewKind.Turn ? CardHeight * 0.5f : 20f);
    }

    /// <summary>Config text is shown as typed: a '&lt;' in a value or description is not a TMP tag.</summary>
    private static string NoParse(string s) =>
        string.IsNullOrEmpty(s) ? s : "<noparse>" + s.Replace("</noparse>", "") + "</noparse>";

    private void BuildCard()
    {
        card = new GameObject("PeakXR_SettingPreviewCard", typeof(RectTransform));
        card.transform.SetParent(transform, false);
        var canvas = card.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        ((RectTransform)card.transform).sizeDelta = new Vector2(CardWidth, CardHeight);
        cardGroup = card.AddComponent<CanvasGroup>();
        cardGroup.interactable = false;
        cardGroup.blocksRaycasts = false;
        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(card.transform, false);
        cardText = (RectTransform)textGo.transform;
        cardText.anchorMin = Vector2.zero;
        cardText.anchorMax = Vector2.one;
        cardText.offsetMin = new Vector2(24f, 20f);
        cardText.offsetMax = new Vector2(-24f, -20f);
        text = textGo.AddComponent<TextMeshProUGUI>();
        text.fontSize = 28f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;

        sampleTag = new GameObject("PeakXR_SettingPreviewTag", typeof(RectTransform));
        sampleTag.transform.SetParent(transform, false);
        sampleTag.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        ((RectTransform)sampleTag.transform).sizeDelta = new Vector2(400f, 80f);
        tagText = sampleTag.AddComponent<TextMeshProUGUI>();
        tagText.text = "Scout";
        // shortcut: the game's tag font size lives in its prefab; 36 is a guess, tune in headset.
        tagText.fontSize = 36f;
        tagText.alignment = TextAlignmentOptions.Center;
        tagText.raycastTarget = false;

        lineMaterial = UIMaterials.Create(Accent, drawOnTop: false, renderQueue: 3100);
        slotMaterial = UIMaterials.Create(Accent, drawOnTop: false, renderQueue: 3050);
        ring = Line("Ring", 32, loop: true);
        startNeedle = Line("Start", 2, loop: false);
        needle = Line("Needle", 2, loop: false);
        outline = Line("Outline", 4, loop: true);
        arrow = Line("Arrow", 5, loop: false);
        var sphere = UIMaterials.SphereMesh();
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i] = UIMaterials
                .MeshObject($"Slot{i}", sphere, slotMaterial, transform)
                .transform;
            slots[i].gameObject.SetActive(false);
        }
        HideAll();
    }

    private LineRenderer Line(string name, int points, bool loop)
    {
        var go = new GameObject("PeakXR_Preview" + name);
        go.transform.SetParent(transform, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = points;
        line.loop = loop;
        line.startWidth = line.endWidth = LineWidth;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = lineMaterial;
        go.SetActive(false);
        return line;
    }

    /// <summary>Beside the row's right edge, in the menu's plane and pixel scale, so it reads like the menu.</summary>
    private void PlaceCard(RectTransform row, float alpha)
    {
        RowRightEdge(row, corners, out var edge, out var right);
        var mpp = row.lossyScale.x;
        var t = card.transform;
        t.SetPositionAndRotation(
            edge + right * (0.04f + CardWidth * 0.5f * mpp) - row.forward * 0.002f,
            row.rotation
        );
        t.localScale = new Vector3(mpp, mpp, mpp);
        cardGroup.alpha = alpha;
        if (!card.activeSelf)
            card.SetActive(true);
    }

    /// <summary>Middle of the row's right edge and the panel's rightward direction, in world space; shared with the
    /// VR Guide demos so both stand beside the row the same way. <paramref name="corners"/> is scratch space.</summary>
    internal static void RowRightEdge(
        RectTransform row,
        Vector3[] corners,
        out Vector3 edge,
        out Vector3 right
    )
    {
        row.GetWorldCorners(corners);
        right = (corners[3] - corners[0]).normalized;
        edge = (corners[2] + corners[3]) * 0.5f;
    }

    // ---- per-frame samples ----

    private void DrawVisual(VRRig rig, float alpha)
    {
        var c = Accent;
        c.a = 0.9f * alpha;
        lineMaterial.color = c;
        var head = rig.Head;
        switch (kind)
        {
            case PreviewKind.Vignette:
                var on = SettingPreviews.Value(ref vignetteOn, "Comfort", "Vignette", true);
                var strength = SettingPreviews.Value(
                    ref vignetteStrength,
                    "Comfort",
                    "VignetteStrength",
                    0.5f
                );
                // Eased in, and a hover never tunnels the view further than a medium vignette.
                if (clock.FromHover)
                    strength = Mathf.Min(strength, HoverVignetteCap);
                vignette.Show(head, on ? strength * alpha * clock.RampIn(VignetteRamp) : 0f);
                break;
            case PreviewKind.StatusGlow:
                DrawStatus(rig, alpha);
                break;
            case PreviewKind.Turn:
                DrawTurnDial();
                break;
            case PreviewKind.MoveDirection:
                DrawArrow(rig);
                break;
            case PreviewKind.Crouch:
                DrawCrouchLine(rig);
                break;
            case PreviewKind.Hud:
                DrawHudOutline();
                break;
            case PreviewKind.Wrist:
                DrawWristOutline(rig);
                break;
            case PreviewKind.NameTag:
                DrawTag(head, alpha);
                break;
            case PreviewKind.BeltSlots:
                DrawSlots(rig, alpha);
                break;
        }
    }

    private void DrawStatus(VRRig rig, float alpha)
    {
        if (rim == null || !rim.Alive || rimRig != rig)
        {
            rim?.Destroy();
            rim = new PeripheralRim(
                rig.Head,
                rig.HeadCamera != null ? rig.HeadCamera.nearClipPlane : 0.05f
            );
            rimRig = rig;
        }
        var mode = SettingPreviews.Value(
            ref statusMode,
            "UI",
            "StatusOverlays",
            StatusOverlayMode.Peripheral
        );
        var dying = entry.Definition.Key == "DyingTint";
        var show =
            mode == StatusOverlayMode.Peripheral
            && (!dying || SettingPreviews.Value(ref dyingTint, "UI.Status", "DyingTint", true));
        var color = dying ? DyingSample : StatusSample;
        color.a = show
            ? Mathf.Clamp01(
                SettingPreviews.Value(ref statusIntensity, "UI.Status", "PeripheralIntensity", 0.5f)
            ) * alpha
            : 0f;
        rim.Show(color);
    }

    /// <summary>Top-down dial on the card: the needle turns as your view would, the player never does.</summary>
    private void DrawTurnDial()
    {
        var t = card.transform;
        var mpp = t.localScale.x;
        var up = t.up;
        var side = t.right;
        var center = t.position - up * (CardHeight * 0.22f * mpp) - t.forward * 0.004f;
        var radius = CardHeight * 0.2f * mpp;
        for (int i = 0; i < 32; i++)
        {
            var a = i * Mathf.PI * 2f / 32f;
            ring.SetPosition(i, center + (up * Mathf.Cos(a) + side * Mathf.Sin(a)) * radius);
        }
        startNeedle.SetPosition(0, center);
        startNeedle.SetPosition(1, center + up * (radius * 0.6f));

        float degrees;
        if (SettingPreviews.Value(ref turnMode, "Comfort", "TurnMode", "Snap") == "Smooth")
            degrees =
                clock.Elapsed
                * SettingPreviews.Value(ref smoothSpeed, "Comfort", "SmoothTurnSpeed", 120f);
        else
            degrees =
                Mathf.Floor(clock.Elapsed / SnapStepSeconds + 1f)
                * SettingPreviews.Value(ref snapAngle, "Comfort", "SnapTurnAngle", 30f);
        var r = degrees * Mathf.Deg2Rad;
        needle.SetPosition(0, center);
        needle.SetPosition(1, center + (up * Mathf.Cos(r) + side * Mathf.Sin(r)) * radius);
        Show(ring);
        Show(startNeedle);
        Show(needle);
    }

    /// <summary>
    /// Ring around you at the head height where physical crouch starts (same rule as Core
    /// <see cref="PhysicalCrouch"/>): dip your head below it to crouch. Hidden when the setting is off or seated.
    /// </summary>
    private void DrawCrouchLine(VRRig rig)
    {
        if (
            !SettingPreviews.Value(ref physicalCrouch, "Locomotion", "PhysicalCrouch", true)
            || rig.SeatedMode
            || rig.Origin == null
        )
        {
            Hide(ring);
            return;
        }
        float eye = RigMath.TrackedEyeHeight(rig.HeightOffset);
        float standing = XRRuntimeInfo.FloorTracking
            ? eye
            : HeightCalibration.DefaultStandingEyeHeight;
        var center = VRInput.HeadPosition;
        center.y = eye - CrouchEnter * standing;
        for (int i = 0; i < 32; i++)
        {
            var a = i * Mathf.PI * 2f / 32f;
            ring.SetPosition(
                i,
                rig.Origin.TransformPoint(
                    center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.35f
                )
            );
        }
        Show(ring);
    }

    /// <summary>Waist-high arrow where pushing the stick forward would walk you.</summary>
    private void DrawArrow(VRRig rig)
    {
        var head = rig.Head;
        var dir = head.forward;
        if (
            SettingPreviews.Value(ref moveRelativeTo, "Locomotion", "MoveRelativeTo", "Head")
            == "Hand"
        )
        {
            // The move stick is on the left controller unless left-handed.
            var aim = rig.Aim(!VRInput.LeftHanded);
            if (aim != null)
                dir = aim.forward;
        }
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f)
            dir = Vector3.ProjectOnPlane(head.up, Vector3.up);
        dir.Normalize();
        var sideways = Vector3.Cross(Vector3.up, dir);
        var tail = head.position + dir * 0.5f + Vector3.down * 0.5f;
        var tip = tail + dir * 0.4f;
        arrow.SetPosition(0, tail);
        arrow.SetPosition(1, tip);
        arrow.SetPosition(2, tip - dir * 0.1f + sideways * 0.06f);
        arrow.SetPosition(3, tip);
        arrow.SetPosition(4, tip - dir * 0.1f - sideways * 0.06f);
        Show(arrow);
    }

    private void DrawHudOutline()
    {
        var gui = GUIManager.instance;
        var hud = gui != null ? gui.hudCanvas : null;
        if (hud == null || hud.renderMode != RenderMode.WorldSpace)
        {
            Hide(outline);
            return;
        }
        ((RectTransform)hud.transform).GetWorldCorners(corners);
        for (int i = 0; i < 4; i++)
            outline.SetPosition(i, corners[i]);
        Show(outline);
    }

    /// <summary>Where the wrist HUD sits (same pose as ui.world's wrist box).</summary>
    private void DrawWristOutline(VRRig rig)
    {
        var left =
            SettingPreviews.Value(ref wristHand, "UI.HUD", "WristHand", WristSide.Left)
            == WristSide.Left;
        var hand = rig.Hand(left);
        if (hand == null)
        {
            Hide(outline);
            return;
        }
        // The watch-face pose ui.world's wrist box rests in (before it turns toward the eye).
        var rotation = WristHud.Rest(hand.rotation.N(), left).U();
        var anchor = WristHud.Anchor(hand.position.N(), hand.rotation.N(), left).U();
        var w = SettingPreviews.Value(ref wristWidth, "UI.HUD", "WristWidth", 0.16f) * 0.5f;
        var h = w * 0.4f;
        outline.SetPosition(0, anchor + rotation * new Vector3(-w, -h, 0f));
        outline.SetPosition(1, anchor + rotation * new Vector3(-w, h, 0f));
        outline.SetPosition(2, anchor + rotation * new Vector3(w, h, 0f));
        outline.SetPosition(3, anchor + rotation * new Vector3(w, -h, 0f));
        Show(outline);
    }

    /// <summary>A tag above the card, sized like ui.world sizes player tags at that distance.</summary>
    private void DrawTag(Transform head, float alpha)
    {
        var t = card.transform;
        var position = t.position + t.up * (CardHeight * 0.5f * t.localScale.x + 0.08f);
        var toTag = position - head.position;
        var distance = Mathf.Max(0.01f, toTag.magnitude);
        var size =
            SettingPreviews.Value(ref nameTagScale, "UI", "NameTagScale", 1f)
            * 0.0006f
            * Mathf.Max(1f, distance);
        sampleTag.transform.SetPositionAndRotation(
            position,
            Quaternion.LookRotation(toTag, Vector3.up)
        );
        sampleTag.transform.localScale = new Vector3(size, size, size);
        tagText.alpha = alpha;
        if (!sampleTag.activeSelf)
            sampleTag.SetActive(true);
    }

    /// <summary>Spheres the size of each belt slot's catch area, on the live anchors.</summary>
    private void DrawSlots(VRRig rig, float alpha)
    {
        var inv = BodyInventory.Instance;
        var c = Accent;
        c.a = 0.3f * alpha;
        slotMaterial.color = c;
        var count = inv.Valid && rig.Origin != null ? inv.Count : 0;
        for (int i = 0; i < slots.Length; i++)
        {
            var show = i < count && inv.Slot(i).GameSlot != BodySlotLayout.MaxHotbar;
            if (show)
            {
                slots[i].position = rig.Origin.TransformPoint(inv.Anchor(i));
                var d = inv.Slot(i).Radius * 2f;
                slots[i].localScale = new Vector3(d, d, d);
            }
            if (slots[i].gameObject.activeSelf != show)
                slots[i].gameObject.SetActive(show);
        }
    }

    // ---- hiding ----

    private static void Show(LineRenderer line)
    {
        if (!line.gameObject.activeSelf)
            line.gameObject.SetActive(true);
    }

    private static void Hide(LineRenderer line)
    {
        if (line != null && line.gameObject.activeSelf)
            line.gameObject.SetActive(false);
    }

    private void HideVisuals()
    {
        vignette.Show(null, 0f);
        if (rim != null && rim.Alive)
            rim.Show(Color.clear);
        Hide(ring);
        Hide(startNeedle);
        Hide(needle);
        Hide(outline);
        Hide(arrow);
        if (sampleTag != null && sampleTag.activeSelf)
            sampleTag.SetActive(false);
        foreach (var s in slots)
            if (s != null && s.gameObject.activeSelf)
                s.gameObject.SetActive(false);
    }

    private void HideAll()
    {
        shown = false;
        HideVisuals();
        if (card != null && card.activeSelf)
            card.SetActive(false);
        if (fadeUntil >= 0f)
            EndFade();
    }
}
