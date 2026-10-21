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
/// texts go to the hand <see cref="ControllerLabels"/> picks from the PeakXR layout, so they always say what your
/// controller does. Each hand's labels stand in a column on its outer side, clear of the hand mesh as you see it
/// (<see cref="SpatialMath.BesideHand"/>), ordered top to bottom like their buttons on the controller and stacked so
/// they never overlap. Labels drawn on top of the hand read as paint on the skin, and buttons a centimetre apart put
/// two labels on top of each other. They show for a few seconds after a change and while you look at a hand.
/// </summary>
internal sealed class ButtonLabels
{
    /// <summary>Smallest text, in HUD pixels (about 1.2 degrees at the floor <see cref="SpatialRuntime.DegreesPerPixel"/>):
    /// the game's prompts are sized for a monitor and came out 6 to 10 px tall in the headset.</summary>
    private const float MinFontPixels = 26f;

    /// <summary>The game's hand mesh around the grip pose is chunky; this clears it.</summary>
    private const float HandRadius = 0.08f;

    private const float RowGapPixels = 4f;

    /// <summary>Frames to keep measuring after a change: the game writes new prompt text a frame later.</summary>
    private const int RestackFrames = 3;
    private const int Count = 5;

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
    private readonly LabelReveal reveal = new();
    private Item lastItem;
    private int restack;

    public ButtonLabels()
    {
        for (int i = 0; i < Count; i++)
            boxes[i] = new SpatialBox("PeakXR_ButtonLabel_" + (ItemPrompt)i);
    }

    public void Release()
    {
        foreach (var b in boxes)
            b.Restore();
        reveal.Reset();
        lastItem = null;
    }

    public void Forget()
    {
        foreach (var b in boxes)
            b.Forget();
        reveal.Reset();
        lastItem = null;
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
        Pose(rig);
    }

    public void Pose(VRRig rig)
    {
        if (!reveal.Visible)
            return;
        bool itemInLeft = LocalHold.ItemInLeftHand;
        bool dominantLeft = VRInput.LeftHanded;
        var drop = InteractConfig.Instance.DropButton;
        for (int i = 0; i < Count; i++)
        {
            var box = boxes[i];
            rank[i] = float.NaN;
            if (box.Rect == null || !box.Rect.gameObject.activeSelf)
                continue;
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
                continue;
            }
            onLeft[i] = left;
            // Top to bottom as the buttons sit on the controller.
            rank[i] = ControllerLabels.Offset(control, left).Y;
        }
        Column(rig, true);
        Column(rig, false);
    }

    /// <summary>Lays out one hand's labels in a column on its outer side.</summary>
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
        for (int k = 0; k < n; k++)
        {
            int i = order[k];
            var p = SpatialMath
                .BesideHand(eyeN, handN, HandRadius, !left, widths[i] * 0.5f * dpp, rowCentres[k])
                .ToUnity();
            boxes[i]
                .Pose(
                    p,
                    SpatialRuntime.FaceEye(p, eye),
                    SpatialMath.MetersPerPixel(Vector3.Distance(p, eye), dpp)
                );
        }
    }

    /// <summary>Centres each label's text on its box origin and measures it, in box pixels.</summary>
    private void Measure()
    {
        for (int i = 0; i < Count; i++)
        {
            var box = boxes[i];
            if (!box.Holding || box.Item(0) == null || !box.Item(0).gameObject.activeSelf)
                continue;
            heights[i] = box.Stack(0f);
            widths[i] = box.ContentBounds(box.Item(0)).width;
        }
    }

    private static float Gaze(Transform head, Transform hand) =>
        hand != null ? Vector3.Angle(head.forward, hand.position - head.position) : 180f;
}
