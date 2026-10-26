using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Interact;
using PeakXR.UI.Common;
using PeakXR.VR;
using TMPro;
using UnityEngine;

namespace PeakXR.UI.Spatial;

/// <summary>
/// U2: a held item's prompts as labels beside the hand whose buttons do them. The game writes
/// <c>itemPromptMain/Secondary/Scroll/Drop/Throw</c> (A/GUIManager.cs:102-110) in <c>UpdateItemPrompts</c>
/// (A/GUIManager.cs:1170) from <c>Item.UIData</c>, localised and swapped at runtime (Lantern, A/Lantern.cs:150); the
/// texts go to the hand <see cref="ControllerLabels"/> picks from the binding table, the item hand, the dominant hand
/// and the drop-button setting, so they always say what your controller does. Each hand's labels stand in a column on
/// its outer side, clear of the hand mesh as you see it (<see cref="SpatialMath.BesideHand"/>), ordered top to bottom
/// like their buttons and stacked so they never overlap. Which button does what reads three ways: each label starts
/// with the button's name as printed on the controller (A, B, TRIGGER, GRIP, STICK) on the hand side, a thin leader
/// runs from that name to the button itself, and resting a finger on a button (capacitive touch) lights its label and
/// dims the others on that hand. Labels and leaders draw on top of the hand (ui.spatial's <see cref="SpatialBox"/>,
/// <see cref="DrawOrder.HandUi"/>). They show for a few seconds after a change and while you look at a hand.
/// </summary>
internal sealed class ButtonLabels
{
    /// <summary>Smallest text, in HUD pixels: <see cref="ReadableText.MinDegrees"/> at the smallest HUD pixel
    /// (<see cref="SpatialRuntime.MinDegreesPerPixel"/>). The game's prompts are sized for a monitor and came out 6 to
    /// 10 px tall in the headset.</summary>
    private static readonly float MinFontPixels = ReadableText.MinPixels(
        SpatialRuntime.MinDegreesPerPixel
    );

    /// <summary>Space between a label's text and the edge of its plate, in box pixels.</summary>
    private const float PlatePadPixels = 8f;

    /// <summary>The game's hand mesh around the grip pose is chunky; this clears it.</summary>
    private const float HandRadius = 0.08f;

    private const float RowGapPixels = 4f;

    /// <summary>Gap between the button name and the label text, in box pixels.</summary>
    private const float GlyphGapPixels = 10f;

    private const float LeaderWidth = 0.0012f;
    private const float LeaderAlpha = 0.55f;

    /// <summary>Emphasis change per second (fade between lit, rest and dimmed).</summary>
    private const float EmphasisRate = 6f;

    /// <summary>Frames to keep measuring after a change: the game writes new prompt text a frame later.</summary>
    private const int RestackFrames = 3;
    private const int Count = 5;

    private static readonly Color GlyphColor = new(
        ButtonLabelStyle.GlyphColor.X,
        ButtonLabelStyle.GlyphColor.Y,
        ButtonLabelStyle.GlyphColor.Z,
        1f
    );

    private static ButtonLabels instance;

    private readonly SpatialBox[] boxes = new SpatialBox[Count];
    private readonly TMP_Text[] texts = new TMP_Text[Count];
    private readonly string[] lastText = new string[Count];
    private readonly bool[] lastActive = new bool[Count];
    private readonly float[] widths = new float[Count];
    private readonly float[] heights = new float[Count];
    private readonly bool[] onLeft = new bool[Count];
    private readonly float[] rank = new float[Count];
    private readonly int[] order = new int[Count];
    private readonly float[] rowHeights = new float[Count];
    private readonly float[] rowCentres = new float[Count];
    private readonly VRControl[] controls = new VRControl[Count];
    private readonly bool[] hasGlyph = new bool[Count];
    private readonly TMP_Text[] glyphs = new TMP_Text[Count];

    /// <summary>The dark plate under each label and its button name: the game's prompt text over fog or a coloured
    /// controller read at 1.2:1 in run 43.</summary>
    private readonly UnityEngine.UI.Image[] plates = new UnityEngine.UI.Image[Count];
    private readonly float[] glyphWidths = new float[Count];
    private readonly CanvasGroup[] groups = new CanvasGroup[Count];
    private readonly float[] emphasis = new float[Count];
    private readonly LineRenderer[] leaders = new LineRenderer[Count];
    private readonly Material[] leaderMats = new Material[Count];
    private readonly LabelReveal reveal = new();
    private GameObject leaderRoot;
    private Item lastItem;
    private int restack;

    public ButtonLabels()
    {
        instance = this;
        for (int i = 0; i < Count; i++)
        {
            boxes[i] = new SpatialBox("PeakXR_ButtonLabel_" + (ItemPrompt)i);
            emphasis[i] = 1f;
        }
    }

    public void Release()
    {
        foreach (var b in boxes)
            b.Restore();
        reveal.Reset();
        lastItem = null;
        DestroyLeaders();
    }

    public void Forget()
    {
        foreach (var b in boxes)
            b.Forget();
        reveal.Reset();
        lastItem = null;
        for (int i = 0; i < Count; i++)
        {
            glyphs[i] = null;
            plates[i] = null;
            groups[i] = null;
            hasGlyph[i] = false;
        }
    }

    public void Tick(GUIManager gui, RectTransform hudRoot, VRRig rig, float dt)
    {
        if (!boxes[0].Holding)
        {
            texts[(int)ItemPrompt.Main] = gui.itemPromptMain;
            texts[(int)ItemPrompt.Secondary] = gui.itemPromptSecondary;
            texts[(int)ItemPrompt.Scroll] = gui.itemPromptScroll;
            texts[(int)ItemPrompt.Drop] = gui.itemPromptDrop;
            texts[(int)ItemPrompt.Throw] = gui.itemPromptThrow;
            for (int i = 0; i < Count; i++)
            {
                if (texts[i] == null)
                    continue;
                boxes[i].Move(hudRoot, texts[i].transform, Vector2.zero);
                SpatialBox.Enlarge(texts[i].transform, MinFontPixels);
            }
            if (!boxes[0].Holding)
                return;
            restack = RestackFrames;
        }

        var c = Character.localCharacter;
        var item = c != null ? c.data.currentItem : null;
        bool changed = item != lastItem;
        lastItem = item;
        for (int i = 0; i < Count; i++)
        {
            var t = texts[i];
            if (t == null)
                continue;
            var text = t.text;
            bool active = t.gameObject.activeSelf;
            if (!ReferenceEquals(text, lastText[i]) || active != lastActive[i])
                changed = true;
            lastText[i] = text;
            lastActive[i] = active;
        }
        if (changed)
            restack = RestackFrames;
        if (restack > 0)
        {
            restack--;
            Measure();
        }

        var head = rig.Head;
        float gaze = Mathf.Min(Gaze(head, rig.LeftHand), Gaze(head, rig.RightHand));
        bool visible = item != null && reveal.Update(changed, gaze, dt);
        for (int i = 0; i < Count; i++)
            boxes[i].SetVisible(visible && texts[i] != null && lastActive[i]);
        if (!visible)
            HideLeaders();
        Pose(rig);
    }

    public void Pose(VRRig rig)
    {
        if (!reveal.Visible)
            return;
        bool itemInLeft = LocalHold.ItemInLeftHand;
        bool dominantLeft = VRInput.LeftHanded;
        var drop = InteractConfig.Instance.DropButton;
        bool anyLeft = false,
            anyRight = false;
        var touchLeft = Touch(in VRInput.Left);
        var touchRight = Touch(in VRInput.Right);
        for (int i = 0; i < Count; i++)
        {
            var box = boxes[i];
            rank[i] = float.NaN;
            if (box.Rect == null || !box.Rect.gameObject.activeSelf)
            {
                ShowLeader(i, false);
                continue;
            }
            if (
                !ControllerLabels.ForPrompt(
                    (ItemPrompt)i,
                    itemInLeft,
                    dominantLeft,
                    drop,
                    out var left,
                    out var control
                )
            )
            {
                box.SetVisible(false);
                ShowLeader(i, false);
                continue;
            }
            // A new button or hand renames the label and moves its name to the hand side.
            if (!hasGlyph[i] || control != controls[i] || left != onLeft[i])
            {
                controls[i] = control;
                onLeft[i] = left;
                SetGlyph(i);
            }
            // Top to bottom as the buttons sit on the controller: up the handle is the grip's +Z (GripFrame).
            rank[i] = ControllerLabels.Spot(control, left).Z;
            if (ControllerLabels.Touched(control, left ? touchLeft : touchRight))
            {
                if (left)
                    anyLeft = true;
                else
                    anyRight = true;
            }
        }
        float step = EmphasisRate * Time.unscaledDeltaTime;
        for (int i = 0; i < Count; i++)
        {
            if (float.IsNaN(rank[i]))
                continue;
            bool left = onLeft[i];
            bool touched = ControllerLabels.Touched(controls[i], left ? touchLeft : touchRight);
            float target = ControllerLabels.Emphasis(touched, left ? anyLeft : anyRight);
            emphasis[i] = Mathf.MoveTowards(emphasis[i], target, step);
            if (groups[i] != null && !Mathf.Approximately(groups[i].alpha, emphasis[i]))
                groups[i].alpha = emphasis[i];
        }
        Column(rig, true);
        Column(rig, false);
    }

    /// <summary>Lays out one hand's labels in a column on its outer side, each joined to its button.</summary>
    private void Column(VRRig rig, bool left)
    {
        int n = 0;
        for (int i = 0; i < Count; i++)
        {
            if (float.IsNaN(rank[i]) || onLeft[i] != left)
                continue;
            // Insertion by rank, highest first; prompt order breaks ties.
            int k = n++;
            while (k > 0 && rank[order[k - 1]] < rank[i])
            {
                order[k] = order[k - 1];
                k--;
            }
            order[k] = i;
        }
        if (n == 0)
            return;
        var hand = rig.Hand(left);
        if (hand == null)
            return;
        var dpp = SpatialRuntime.DegreesPerPixel;
        for (int k = 0; k < n; k++)
            rowHeights[k] = heights[order[k]] * dpp;
        SpatialMath.StackRows(rowHeights, n, RowGapPixels * dpp, rowCentres);
        var eye = rig.Head.position;
        var eyeN = eye.ToNumerics();
        var handN = hand.position.ToNumerics();
        // The button name sits on the hand side: the viewer's left of a right-hand column, the right of a left one.
        float side = left ? 1f : -1f;
        for (int k = 0; k < n; k++)
        {
            int i = order[k];
            float glyphSpan = glyphWidths[i] > 0f ? glyphWidths[i] + GlyphGapPixels : 0f;
            float halfWidth = (widths[i] + glyphSpan) * 0.5f;
            var centre = SpatialMath
                .BesideHand(eyeN, handN, HandRadius, !left, halfWidth * dpp, rowCentres[k])
                .ToUnity();
            var rotation = SpatialRuntime.FaceEye(centre, eye);
            float mpp = SpatialMath.MetersPerPixel(Vector3.Distance(centre, eye), dpp);
            // The box origin is the text's centre: shift it away from the hand by half the name's span.
            var p = centre - rotation * Vector3.right * (side * glyphSpan * 0.5f * mpp);
            boxes[i].Pose(p, rotation, mpp);
            Leader(i, hand, left, side);
        }
    }

    /// <summary>Draws label <paramref name="i"/>'s leader from its button name to the button.</summary>
    private void Leader(int i, Transform hand, bool left, float side)
    {
        var rect = boxes[i].Rect;
        if (rect == null || glyphs[i] == null)
        {
            ShowLeader(i, false);
            return;
        }
        EnsureLeaders();
        var line = leaders[i];
        // Inner edge of the name, in box pixels.
        float x = side * (widths[i] * 0.5f + GlyphGapPixels + glyphWidths[i]);
        line.SetPosition(0, rect.TransformPoint(new Vector3(x, 0f, 0f)));
        line.SetPosition(
            1,
            hand.TransformPoint(ControllerLabels.Spot(controls[i], left).ToUnity())
        );
        var color = GlyphColor;
        color.a = LeaderAlpha * emphasis[i];
        if (leaderMats[i].color != color)
            leaderMats[i].color = color;
        ShowLeader(i, true);
    }

    private void SetGlyph(int i)
    {
        var glyph = EnsureGlyph(i);
        if (glyph == null)
            return;
        hasGlyph[i] = true;
        glyph.text = ControllerLabels.Glyph(controls[i]);
        restack = RestackFrames;
    }

    /// <summary>The button-name text in label <paramref name="i"/>'s box, made once per box.</summary>
    private TMP_Text EnsureGlyph(int i)
    {
        var rect = boxes[i].Rect;
        if (rect == null)
            return null;
        if (glyphs[i] != null && glyphs[i].transform.parent == rect)
            return glyphs[i];
        var go = new GameObject("PeakXR_ButtonGlyph", typeof(RectTransform));
        go.transform.SetParent(rect, false);
        var glyph = go.AddComponent<TextMeshProUGUI>();
        glyph.raycastTarget = false;
        glyph.textWrappingMode = TextWrappingModes.NoWrap;
        glyph.alignment = TextAlignmentOptions.Center;
        glyph.fontStyle = FontStyles.Bold;
        glyph.color = GlyphColor;
        glyphs[i] = glyph;
        plates[i] = Plate(rect);
        groups[i] = rect.TryGetComponent<CanvasGroup>(out var g)
            ? g
            : rect.gameObject.AddComponent<CanvasGroup>();
        groups[i].blocksRaycasts = false;
        groups[i].interactable = false;
        return glyph;
    }

    /// <summary>A dark rounded plate as the box's first child, drawn on top of the hand like the label.</summary>
    private static UnityEngine.UI.Image Plate(RectTransform rect)
    {
        var go = new GameObject("PeakXR_ButtonPlate", typeof(RectTransform));
        go.transform.SetParent(rect, false);
        go.transform.SetAsFirstSibling();
        var image = go.AddComponent<UnityEngine.UI.Image>();
        image.sprite = World.BackplateView.PlateSprite();
        image.type = UnityEngine.UI.Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 2f;
        image.raycastTarget = false;
        var c = MenuBackplate.Color;
        image.color = new Color(c.X, c.Y, c.Z, ButtonLabelStyle.PlateOpacity);
        OnTop.Set(go.transform, true);
        return image;
    }

    /// <summary>Centres each label's text on its box origin, measures it and places its button name beside it, in box
    /// pixels.</summary>
    private void Measure()
    {
        for (int i = 0; i < Count; i++)
        {
            var box = boxes[i];
            if (!box.Holding || box.Item(0) == null || !box.Item(0).gameObject.activeSelf)
                continue;
            heights[i] = box.Stack(0f);
            widths[i] = box.ContentBounds(box.Item(0)).width;
            var glyph = glyphs[i];
            var text = texts[i];
            if (glyph == null || text == null || text.font == null)
            {
                glyphWidths[i] = 0f;
                continue;
            }
            // Same font and on-top material (with its halo) as the label, a little smaller than its text.
            glyph.font = text.font;
            glyph.fontSharedMaterial = text.fontSharedMaterial;
            float boxScale = Mathf.Max(1e-9f, Mathf.Abs(box.Rect.lossyScale.x));
            glyph.fontSize =
                text.fontSize
                * Mathf.Abs(text.transform.lossyScale.x)
                / boxScale
                * ButtonLabelStyle.GlyphScale;
            glyphWidths[i] = glyph.GetPreferredValues(glyph.text).x;
            var r = glyph.rectTransform;
            r.sizeDelta = new Vector2(glyphWidths[i], glyph.fontSize * 1.4f);
            float side = onLeft[i] ? 1f : -1f;
            r.anchoredPosition = new Vector2(
                side * (widths[i] * 0.5f + GlyphGapPixels + glyphWidths[i] * 0.5f),
                0f
            );
            // The plate covers the text and the button name beside it.
            float extra = GlyphGapPixels + glyphWidths[i];
            var plate = plates[i] != null ? plates[i].rectTransform : null;
            if (plate != null)
            {
                plate.anchoredPosition = new Vector2(side * extra * 0.5f, 0f);
                plate.sizeDelta = new Vector2(
                    widths[i] + extra + 2f * PlatePadPixels,
                    Mathf.Max(heights[i], r.sizeDelta.y) + 2f * PlatePadPixels
                );
            }
        }
    }

    private void EnsureLeaders()
    {
        if (leaderRoot != null)
            return;
        leaderRoot = new GameObject("PeakXR_ButtonLeaders");
        Object.DontDestroyOnLoad(leaderRoot);
        for (int i = 0; i < Count; i++)
        {
            var go = new GameObject("Leader" + (ItemPrompt)i);
            go.transform.SetParent(leaderRoot.transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.widthMultiplier = LeaderWidth;
            line.numCapVertices = 0;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            leaderMats[i] = UIMaterials.Create(GlyphColor, drawOnTop: true, renderQueue: 3000);
            line.sharedMaterial = leaderMats[i];
            line.startColor = line.endColor = Color.white;
            line.sortingOrder = DrawOrder.HandUi;
            line.enabled = false;
            leaders[i] = line;
        }
    }

    private void ShowLeader(int i, bool on)
    {
        var line = leaders[i];
        if (line != null && line.enabled != on)
            line.enabled = on;
    }

    private void HideLeaders()
    {
        for (int i = 0; i < Count; i++)
            ShowLeader(i, false);
    }

    private void DestroyLeaders()
    {
        if (leaderRoot == null)
            return;
        for (int i = 0; i < Count; i++)
        {
            if (leaderMats[i] != null)
                Object.Destroy(leaderMats[i]);
            leaderMats[i] = null;
            leaders[i] = null;
        }
        Object.Destroy(leaderRoot);
        leaderRoot = null;
    }

    private static ControlTouch Touch(in HandState s) =>
        new()
        {
            Trigger = s.TriggerTouch,
            Primary = s.PrimaryTouch,
            Secondary = s.SecondaryTouch,
            Stick = s.StickTouch,
        };

    private static float Gaze(Transform head, Transform hand) =>
        hand != null ? Vector3.Angle(head.forward, hand.position - head.position) : 180f;

    /// <summary>For the self-test: label <paramref name="prompt"/>'s button name and plate, once made.</summary>
    internal static bool TryParts(
        ItemPrompt prompt,
        out TMP_Text glyph,
        out UnityEngine.UI.Image plate
    )
    {
        var self = instance;
        glyph = self != null ? self.glyphs[(int)prompt] : null;
        plate = self != null ? self.plates[(int)prompt] : null;
        return glyph != null && plate != null;
    }

    /// <summary>Self-test: prompt <paramref name="prompt"/>'s button name, emphasis 0..1 and leader ends while it shows.</summary>
    internal static bool TryShown(
        ItemPrompt prompt,
        out string glyph,
        out float emphasis,
        out Vector3 from,
        out Vector3 to
    )
    {
        int i = (int)prompt;
        glyph = null;
        emphasis = 0f;
        from = to = default;
        var self = instance;
        if (
            self == null
            || self.glyphs[i] == null
            || self.leaders[i] == null
            || !self.leaders[i].enabled
        )
            return false;
        glyph = self.glyphs[i].text;
        emphasis = self.emphasis[i];
        from = self.leaders[i].GetPosition(0);
        to = self.leaders[i].GetPosition(1);
        return true;
    }
}
