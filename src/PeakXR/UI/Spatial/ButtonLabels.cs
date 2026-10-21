using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Interact;
using PeakXR.UI.Common;
using PeakXR.VR;
using TMPro;
using UnityEngine;

namespace PeakXR.UI.Spatial;

/// <summary>
/// U2: a held item's prompts as small labels beside the controller buttons that do them. The game writes
/// <c>itemPromptMain/Secondary/Scroll/Drop/Throw</c> (A/GUIManager.cs:102-110) in <c>UpdateItemPrompts</c>
/// (A/GUIManager.cs:1170) from <c>Item.UIData</c>, localised and swapped at runtime (Lantern, A/Lantern.cs:150); the
/// texts move to the buttons <see cref="ControllerLabels"/> picks from the PeakXR layout, so they always say what
/// your controller does. They show for a few seconds after a change and while you look at a hand.
/// </summary>
internal sealed class ButtonLabels
{
    private const float SizeScale = 0.8f;
    private const int Count = 5;

    private readonly SpatialBox[] boxes = new SpatialBox[Count];
    private readonly TMP_Text[] texts = new TMP_Text[Count];
    private readonly string[] lastText = new string[Count];
    private readonly bool[] lastActive = new bool[Count];
    private readonly LabelReveal reveal = new();
    private Item lastItem;

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
                if (texts[i] != null)
                    boxes[i].Move(hudRoot, texts[i].transform, Vector2.zero);
            }
            if (!boxes[0].Holding)
                return;
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
        var eye = rig.Head.position;
        var dpp = SpatialRuntime.DegreesPerPixel * SizeScale;
        for (int i = 0; i < Count; i++)
        {
            var box = boxes[i];
            if (box.Rect == null || !box.Rect.gameObject.activeSelf)
                continue;
            if (
                !ControllerLabels.ForPrompt(
                    (ItemPrompt)i,
                    LocalHold.ItemInLeftHand,
                    VRInput.LeftHanded,
                    InteractConfig.Instance.DropButton,
                    out var left,
                    out var control
                )
            )
            {
                box.SetVisible(false);
                continue;
            }
            var hand = rig.Hand(left);
            if (hand == null)
                continue;
            var p = hand.TransformPoint(ControllerLabels.Offset(control, left).ToUnity());
            box.Pose(
                p,
                SpatialRuntime.FaceEye(p, eye),
                SpatialMath.MetersPerPixel(Vector3.Distance(p, eye), dpp)
            );
        }
    }

    private static float Gaze(Transform head, Transform hand) =>
        hand != null ? Vector3.Angle(head.forward, hand.position - head.position) : 180f;
}
